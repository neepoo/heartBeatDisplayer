using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HeartBeat.App.Bluetooth;
using HeartBeat.Core;

namespace HeartBeat.App;

public partial class App : Application
{
    private readonly BleHeartRateTransport _bluetooth = new();
    private readonly HeartRateHistory _history = new();
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private HeartRateController? _controller;
    private HeartRateDevice? _pendingDevice;
    private SettingsStore _store = null!;
    private OverlayWindow _overlay = null!;
    private NativeOverlay _native = null!;
    private TrayController _tray = null!;
    private ControlWindow? _controls;
    private DispatcherTimer _timer = null!;
    private Mutex? _instance;
    private bool _ownsMutex;
    private bool _exiting;
    public UserSettings Settings { get; private set; } = new();
    public bool IsDemo { get; private set; }
    public ConnectionStatus CurrentStatus { get; private set; } = new(ConnectionState.Idle, "尚未连接");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A separate settings directory is useful for a portable test/sandbox session.
        var args = e.Args;
        var directoryIndex = Array.IndexOf(args, "--settings-dir");
        var directory = directoryIndex >= 0 && directoryIndex + 1 < args.Length
            ? Path.GetFullPath(args[directoryIndex + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeartBeatDisplayer");
        var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..16];
        _instance = new Mutex(true, @"Local\HeartBeat-" + identity, out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show("HeartBeat 已在运行，请从系统托盘打开设置。", "HeartBeat"); Shutdown(); return;
        }
        _store = new(directory); Settings = _store.Load();
        IsDemo = args.Contains("--demo");
        _overlay = new OverlayWindow(); MainWindow = _overlay;
        _overlay.SetAppearance(Settings.Locked, Settings.BackgroundOpacity, IsDemo);
        _native = new NativeOverlay(_overlay);
        if (Settings.Left is double left && Settings.Top is double top) { _overlay.Left = left; _overlay.Top = top; _native.ClampToScreen(); }
        else _native.ResetPosition();
        _overlay.PositionEdited += () => { _native.ClampToScreen(); SaveSettings(); };
        _native.ToggleVisibility += ToggleVisibility;
        _native.ToggleLock += () => SetLocked(!Settings.Locked);
        _tray = new TrayController(ShowSettings, ToggleVisibility, () => SetLocked(!Settings.Locked), ResetPosition, () => _ = ExitAsync());
        _overlay.Show(); _native.Apply(Settings.Locked);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Render(), Dispatcher);
        _timer.Start();
        if (IsDemo) { AttachController(new SimulatedHeartRateTransport()); _controller!.Start(SimulatedHeartRateTransport.Device); }
        else
        {
            AttachController(_bluetooth);
            if (Settings.Device is not null) { _pendingDevice = Settings.Device; _controller!.Start(Settings.Device); }
            else ShowSettings();
        }
        if (_native.HotkeyWarning is not null || _store.LastError is not null)
        { ShowSettings(); _controls!.ShowMessage(string.Join("\n", new[] { _native.HotkeyWarning, _store.LastError }.Where(s => s is not null))); }
        Render();
    }

    public Task<IReadOnlyList<HeartRateDevice>> ScanAsync(CancellationToken token) => _bluetooth.ScanAsync(token);

    private void AttachController(IHeartRateTransport transport)
    {
        var controller = _controller = new HeartRateController(transport);
        controller.SampleReceived += sample =>
        {
            var version = controller.Version;
            Dispatcher.BeginInvoke(new Action(() => {
                if (_exiting || controller != _controller || version != controller.Version) return;
                _history.Add(sample); Render();
            }));
        };
        controller.StatusChanged += status =>
        {
            var version = controller.Version;
            Dispatcher.BeginInvoke(new Action(() => {
                if (_exiting || controller != _controller || version != controller.Version) return;
                CurrentStatus = status;
                if (status.State is not ConnectionState.Connected) _history.Add(new(DateTimeOffset.UtcNow, null));
                if (status.State == ConnectionState.Connected && !IsDemo && _pendingDevice is not null)
                { Settings.Device = _pendingDevice; SaveSettings(); }
                _controls?.Refresh(); Render();
            }));
        };
    }

    public async Task ConnectDeviceAsync(HeartRateDevice device)
    {
        await _switchGate.WaitAsync();
        try
        {
            if (_exiting) return;
            if (IsDemo)
            {
                await _controller!.DisposeAsync();
                IsDemo = false; AttachController(_bluetooth);
            }
            _pendingDevice = device; _history.Clear();
            CurrentStatus = new(ConnectionState.Connecting, "正在连接 " + device.Name);
            _controller!.Start(device); ApplyAppearance();
        }
        finally { _switchGate.Release(); }
    }
    public async Task StartDemoAsync()
    {
        await _switchGate.WaitAsync();
        try
        {
            if (_exiting) return;
            await _controller!.DisposeAsync();
            IsDemo = true; _pendingDevice = null; _history.Clear();
            CurrentStatus = new(ConnectionState.Connecting, "正在启动模拟数据");
            AttachController(new SimulatedHeartRateTransport());
            _controller!.Start(SimulatedHeartRateTransport.Device); ApplyAppearance();
            if (!_overlay.IsVisible) { _overlay.Show(); _native.Apply(Settings.Locked); }
        }
        finally { _switchGate.Release(); }
    }
    public async Task DisconnectAsync()
    {
        await _switchGate.WaitAsync();
        try
        {
            if (_exiting) return;
            var stopping = _controller!.StopAsync();
            CurrentStatus = new(ConnectionState.Idle, "已断开");
            _history.Add(new(DateTimeOffset.UtcNow, null)); _controls?.Refresh(); Render();
            await stopping;
        }
        finally { _switchGate.Release(); }
    }
    public void ShowSettings()
    {
        if (_exiting) return;
        _controls ??= new ControlWindow(this);
        _controls.Refresh(); _controls.Show();
        if (_controls.WindowState == WindowState.Minimized) _controls.WindowState = WindowState.Normal;
        _controls.Activate();
    }
    public void ToggleVisibility()
    {
        if (_exiting) return;
        if (_overlay.IsVisible) _overlay.Hide();
        else { _overlay.Show(); _native.ClampToScreen(); _native.Apply(Settings.Locked); }
    }
    public void SetLocked(bool locked) { if (_exiting) return; Settings.Locked = locked; ApplyAppearance(); SaveSettings(); }
    public void SetOpacity(double opacity) { if (_exiting) return; Settings.BackgroundOpacity = Math.Clamp(opacity, 0.4, 0.95); ApplyAppearance(); SaveSettings(); }
    public void ResetPosition() { if (_exiting) return; _native.ResetPosition(); SaveSettings(); }
    private void ApplyAppearance()
    {
        _overlay.SetAppearance(Settings.Locked, Settings.BackgroundOpacity, IsDemo);
        _native.Apply(Settings.Locked); _controls?.Refresh(); Render();
    }
    private void Render()
    {
        if (_exiting) return;
        var now = DateTimeOffset.UtcNow;
        var bpm = CurrentStatus.State == ConnectionState.Connected ? _history.CurrentBpm(now) : null;
        var status = CurrentStatus.State switch {
            ConnectionState.Connected when bpm.HasValue => IsDemo ? "模拟数据" : "实时 · BLE",
            ConnectionState.Connected => "等待数据",
            ConnectionState.Connecting => "连接中…",
            ConnectionState.Reconnecting => "重新连接…",
            ConnectionState.Error => "连接异常",
            _ => "未连接"
        };
        _overlay.Update(bpm, _history.Snapshot(now), now, status);
        _tray.Update(Settings.Locked, bpm, IsDemo);
    }
    private void SaveSettings()
    {
        Settings.Left = _overlay.Left; Settings.Top = _overlay.Top;
        _store.Save(Settings);
        if (_store.LastError is not null) _controls?.ShowMessage(_store.LastError);
    }
    public async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        SaveSettings(); _timer.Stop(); _tray.Dispose(); _native.Dispose();
        _overlay.Hide();
        if (_controls is not null) { _controls.AllowClose = true; _controls.Close(); }
        try
        {
            await _switchGate.WaitAsync();
            try { if (_controller is not null) await _controller.DisposeAsync(); }
            finally { _switchGate.Release(); }
        }
        finally { _overlay.AllowClose = true; _overlay.Close(); Shutdown(); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
