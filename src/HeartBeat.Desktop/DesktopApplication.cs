using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using HeartBeat.Core;

namespace HeartBeat.Desktop;

public sealed class DesktopApplication(DesktopOptions options, Func<IHeartRateTransport> transportFactory, Func<IDesktopIntegration> desktopFactory) : Application
{
    private readonly Lazy<IHeartRateTransport> _bluetooth = new(transportFactory);
    private readonly HeartRateHistory _history = new();
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private HeartRateController? _controller;
    private HeartRateDevice? _pendingDevice;
    private SettingsStore _store = null!;
    private IDesktopIntegration _native = null!;
    private TrayController? _tray;
    private DispatcherTimer _timer = null!;
    private DispatcherTimer _saveTimer = null!;
    private SingleInstance? _instance;
    private IClassicDesktopStyleApplicationLifetime _lifetime = null!;
    private bool _exiting;
    private bool _placing;
    public OverlayWindow Overlay { get; private set; } = null!;
    public ControlWindow? Controls { get; private set; }
    public UserSettings Settings { get; private set; } = new();
    public bool IsDemo { get; private set; }
    public bool SupportsClickThrough => _native.SupportsClickThrough;
    public IReadOnlyList<(string Name, bool Passed)> CheckNativeState(bool locked) =>
        _native is IDesktopSmokeChecks checks ? checks.CheckNativeState(locked) : [];
    public ConnectionStatus CurrentStatus { get; private set; } = new(ConnectionState.Idle, "尚未连接");

    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        _lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        _instance = SingleInstance.TryAcquire(options.SettingsDirectory);
        if (_instance is null)
        {
            Console.Error.WriteLine("HeartBeat 已在运行，请从系统托盘打开设置。");
            _lifetime.Shutdown(2); return;
        }
        _lifetime.Exit += (_, _) => _instance.Dispose();
        _lifetime.ShutdownRequested += (_, e) => { if (!_exiting) { e.Cancel = true; _ = ExitAsync(); } };
        _store = new(options.SettingsDirectory); Settings = _store.Load(); IsDemo = options.Demo;
        // Position recovery can save successfully before startup warnings are shown.
        // Keep the load failure even when that later save clears LastError.
        var loadWarning = _store.LastError;
        Overlay = new OverlayWindow { Width = Settings.Width, Height = Settings.Height };
        _lifetime.MainWindow = Overlay;
        Overlay.SetAppearance(Settings.Locked, Settings.BackgroundOpacity, IsDemo, Settings.AnimateHeart);
        _native = desktopFactory(); _native.Attach(Overlay);
        _native.ToggleVisibility += () => Dispatcher.UIThread.Post(ToggleVisibility);
        _native.ToggleLock += () => Dispatcher.UIThread.Post(() => SetLocked(!Settings.Locked));
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); ClampToScreen(); SaveSettings(); };
        Overlay.PositionEdited += () => { if (_placing || _exiting) return; _saveTimer.Stop(); _saveTimer.Start(); };
        Overlay.Screens.Changed += OnScreensChanged;
        Overlay.ScalingChanged += OnScreensChanged;
        Overlay.Show();
        if (Settings.Left is double x && Settings.Top is double y) Overlay.Position = new((int)x, (int)y);
        else ResetPosition();
        ClampToScreen(); _native.Apply(Settings.Locked);
        _tray = new TrayController(ShowSettings, ToggleVisibility, () => SetLocked(!Settings.Locked), ResetPosition, () => _ = ExitAsync());
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Render(); _timer.Start();
        AttachController(IsDemo ? new SimulatedHeartRateTransport() : _bluetooth.Value);
        if (IsDemo) _controller!.Start(SimulatedHeartRateTransport.Device);
        else if (Settings.Device is not null) { _pendingDevice = Settings.Device; _controller!.Start(Settings.Device); }
        else ShowSettings();
        var warnings = new[] { _native.Warning, loadWarning, _store.LastError }.Where(s => s is not null).Distinct().ToArray();
        if (warnings.Length > 0) { ShowSettings(); Controls!.ShowMessage(string.Join("\n", warnings)); }
        // Linux desktops without a status-notifier host need an always-reachable settings window.
        if (OperatingSystem.IsLinux() && !options.SmokeTest) ShowSettings();
        Render();
        if (options.SmokeTest) Dispatcher.UIThread.Post(async () => await SmokeChecks.RunAsync(this, options.SettingsDirectory));
        base.OnFrameworkInitializationCompleted();
    }
    public Task<IReadOnlyList<HeartRateDevice>> ScanAsync(CancellationToken token) => _bluetooth.Value.ScanAsync(token);
    private void AttachController(IHeartRateTransport transport)
    {
        var controller = _controller = new HeartRateController(transport);
        controller.SampleReceived += sample => {
            var version = controller.Version;
            Dispatcher.UIThread.Post(() => {
                if (_exiting || controller != _controller || version != controller.Version) return;
                _history.Add(sample); Render();
            });
        };
        controller.StatusChanged += status => {
            var version = controller.Version;
            Dispatcher.UIThread.Post(() => {
                if (_exiting || controller != _controller || version != controller.Version) return;
                CurrentStatus = status;
                if (status.State != ConnectionState.Connected) _history.Add(new(DateTimeOffset.UtcNow, null));
                if (status.State == ConnectionState.Connected && !IsDemo && _pendingDevice is not null)
                { Settings.Device = _pendingDevice; SaveSettings(); }
                Controls?.Refresh(); Render();
            });
        };
    }
    public async Task ConnectDeviceAsync(HeartRateDevice device)
    {
        await _switchGate.WaitAsync();
        try {
            if (_exiting) return;
            if (IsDemo) { await _controller!.DisposeAsync(); IsDemo = false; AttachController(_bluetooth.Value); }
            _pendingDevice = device; _history.Clear();
            CurrentStatus = new(ConnectionState.Connecting, "正在连接 " + device.Name);
            _controller!.Start(device); ApplyAppearance();
        } finally { _switchGate.Release(); }
    }
    public async Task StartDemoAsync()
    {
        await _switchGate.WaitAsync();
        try {
            if (_exiting) return;
            await _controller!.DisposeAsync(); IsDemo = true; _pendingDevice = null; _history.Clear();
            CurrentStatus = new(ConnectionState.Connecting, "正在启动模拟数据");
            AttachController(new SimulatedHeartRateTransport()); _controller!.Start(SimulatedHeartRateTransport.Device); ApplyAppearance();
            if (!Overlay.IsVisible) { Overlay.Show(); _native.Apply(Settings.Locked); }
        } finally { _switchGate.Release(); }
    }
    public async Task DisconnectAsync()
    {
        await _switchGate.WaitAsync();
        try {
            if (_exiting) return;
            var stopping = _controller!.StopAsync(); CurrentStatus = new(ConnectionState.Idle, "已断开");
            _history.Add(new(DateTimeOffset.UtcNow, null)); Controls?.Refresh(); Render(); await stopping;
        } finally { _switchGate.Release(); }
    }
    public void ShowSettings()
    {
        if (_exiting) return; Controls ??= new ControlWindow(this);
        Controls.Refresh(); Controls.Show(); Controls.WindowState = WindowState.Normal; Controls.Activate();
    }
    public void ToggleVisibility()
    {
        if (_exiting) return;
        if (Overlay.IsVisible) Overlay.Hide(); else { Overlay.Show(); ClampToScreen(); _native.Apply(Settings.Locked); }
    }
    public void SetLocked(bool locked) { if (_exiting) return; Settings.Locked = locked; ApplyAppearance(); SaveSettings(); }
    public void SetOpacity(double opacity) { if (_exiting) return; Settings.BackgroundOpacity = Math.Clamp(opacity, .4, .95); ApplyAppearance(); SaveSettings(); }
    public void SetHeartAnimation(bool enabled) { if (_exiting) return; Settings.AnimateHeart = enabled; ApplyAppearance(); SaveSettings(); }
    public void ResetPosition()
    {
        if (_exiting) return;
        var screen = Overlay.Screens.Primary ?? Overlay.Screens.All.FirstOrDefault();
        if (screen is null) { Overlay.Position = new(24, 24); return; }
        Overlay.Position = new((int)(screen.WorkingArea.Right - Overlay.Width * screen.Scaling - 24), screen.WorkingArea.Y + 24);
        ClampToScreen(); SaveSettings();
    }
    public void ClampToScreen()
    {
        if (_placing) return;
        _placing = true;
        try {
            var screen = Overlay.Screens.ScreenFromWindow(Overlay) ?? Overlay.Screens.Primary;
            if (screen is null) return;
            var area = screen.WorkingArea; var scale = screen.Scaling;
            Overlay.Width = Math.Min(Overlay.Width, Math.Max(Overlay.MinWidth, area.Width / scale));
            Overlay.Height = Math.Min(Overlay.Height, Math.Max(Overlay.MinHeight, area.Height / scale));
            Overlay.Position = new(Math.Clamp(Overlay.Position.X, area.X, Math.Max(area.X, (int)(area.Right - Overlay.Width * scale))),
                Math.Clamp(Overlay.Position.Y, area.Y, Math.Max(area.Y, (int)(area.Bottom - Overlay.Height * scale))));
        } finally { _placing = false; }
    }
    private void OnScreensChanged(object? sender, EventArgs e)
    { if (!_exiting) Dispatcher.UIThread.Post(() => { ClampToScreen(); SaveSettings(); }); }
    private void ApplyAppearance()
    { Overlay.SetAppearance(Settings.Locked, Settings.BackgroundOpacity, IsDemo, Settings.AnimateHeart); _native.Apply(Settings.Locked); Controls?.Refresh(); Render(); }
    private void Render()
    {
        if (_exiting) return;
        var now = DateTimeOffset.UtcNow;
        var bpm = CurrentStatus.State == ConnectionState.Connected ? _history.CurrentBpm(now) : null;
        var status = CurrentStatus.State switch {
            ConnectionState.Connected when bpm.HasValue => IsDemo ? "模拟数据" : "实时 · BLE",
            ConnectionState.Connected => "等待数据", ConnectionState.Connecting => "连接中…",
            ConnectionState.Reconnecting => "重新连接…", ConnectionState.Error => "连接异常", _ => "未连接"
        };
        Overlay.Update(bpm, _history.Snapshot(now), now, status); _tray?.Update(Settings.Locked, bpm, IsDemo);
    }
    public void SaveSettings()
    {
        Settings.Left = Overlay.Position.X; Settings.Top = Overlay.Position.Y;
        Settings.Width = Overlay.Width; Settings.Height = Overlay.Height; _store.Save(Settings);
        if (_store.LastError is not null) Controls?.ShowMessage(_store.LastError);
    }
    public async Task ExitAsync(int exitCode = 0)
    {
        if (_exiting) return; _exiting = true;
        SaveSettings(); _timer.Stop(); _saveTimer.Stop(); Overlay.Screens.Changed -= OnScreensChanged; Overlay.ScalingChanged -= OnScreensChanged;
        _tray?.Dispose(); _native.Dispose(); Overlay.Hide();
        if (Controls is not null) { Controls.AllowClose = true; Controls.Close(); }
        try {
            await _switchGate.WaitAsync();
            try { if (_controller is not null) await _controller.DisposeAsync(); }
            finally { _switchGate.Release(); }
        } catch { exitCode = 1; throw; }
        finally { Overlay.AllowClose = true; Overlay.Close(); _lifetime.Shutdown(exitCode); }
    }
}
