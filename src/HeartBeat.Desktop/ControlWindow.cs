using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using HeartBeat.Core;

namespace HeartBeat.Desktop;

public sealed class ControlWindow : Window
{
    private readonly DesktopApplication _app;
    private readonly CheckBox _locked = new() { Content = "锁定位置并开启鼠标穿透" };
    private readonly CheckBox _animation = new() { Content = "心跳动效（节奏按 BPM 推算）" };
    private readonly Slider _opacity = new() { Minimum = 40, Maximum = 95, Value = 80, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBlock _opacityLabel = new();
    private readonly TextBlock _connection = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#FBBF82") };
    private readonly ListBox _devices = new() { Height = 100 };
    private readonly Button _scanButton = new() { Content = "扫描设备" };
    private readonly Button _connectButton = new() { Content = "连接" };
    private CancellationTokenSource? _scan;
    private bool _refreshing;
    public bool AllowClose { get; set; }
    public ControlWindow(DesktopApplication app)
    {
        _app = app; Title = "HeartBeat · 连接与设置"; Width = 500; Height = 760; MinWidth = 460; MinHeight = 550;
        Background = Brush.Parse("#10141C"); Foreground = Brush.Parse("#EDF1F7");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var body = new StackPanel { Margin = new Thickness(28, 24, 28, 20), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "♥  HEARTBEAT", Foreground = Brush.Parse("#FB7185"), FontSize = 12 });
        body.Children.Add(new TextBlock { Text = "连接你的手表", FontSize = 28, FontWeight = FontWeight.SemiBold });
        body.Children.Add(Text("Forerunner 255 · 实时心率悬浮窗"));
        body.Children.Add(new Border { Background = Brush.Parse("#1A202C"), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Child = Text("先在手表上开启心率广播\n长按 UP → 腕式心率 → 广播心率 → START。部分固件的“腕式心率”位于健康相关菜单内。\n电脑开启蓝牙，手表保持在附近。游戏使用无边框模式。") });
        body.Children.Add(Text("附近的心率设备"));
        _devices.ItemTemplate = new FuncDataTemplate<HeartRateDevice>((device, _) => Text(device?.Name ?? ""));
        body.Children.Add(_devices);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_scanButton); buttons.Children.Add(_connectButton);
        buttons.Children.Add(Button("断开", async () => { _scan?.Cancel(); await _app.DisconnectAsync(); })); body.Children.Add(buttons);
        _scanButton.Click += async (_, _) => await ScanAsync();
        _connectButton.Click += async (_, _) => {
            if (_devices.SelectedItem is not HeartRateDevice device) { ShowMessage("请先扫描并选择手表。"); return; }
            _connectButton.IsEnabled = false;
            try { await _app.ConnectDeviceAsync(device); ShowMessage(""); }
            catch (Exception ex) { ShowMessage(ex.Message); }
            finally { _connectButton.IsEnabled = true; }
        };
        body.Children.Add(_connection); body.Children.Add(_message);
        body.Children.Add(new Border { Height = 1, Background = Brush.Parse("#2C3546") });
        var windowActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        windowActions.Children.Add(Button("显示 / 隐藏", () => { _app.ToggleVisibility(); return Task.CompletedTask; }));
        windowActions.Children.Add(Button("重置位置", () => { _app.ResetPosition(); return Task.CompletedTask; })); body.Children.Add(windowActions);
        if (!_app.SupportsClickThrough) _locked.Content = "锁定位置（当前桌面不支持鼠标穿透）";
        body.Children.Add(_locked); body.Children.Add(Text("解锁后拖动窗口边缘或右下角缩放，大小会自动保存。"));
        body.Children.Add(_animation); body.Children.Add(_opacityLabel); body.Children.Add(_opacity);
        body.Children.Add(Text(OperatingSystem.IsMacOS() ? "Control + Option + H  显示 / 隐藏 · Control + Option + L  锁定" : "Ctrl + Alt + H  显示 / 隐藏 · Ctrl + Alt + L  锁定 / 解锁"));
        body.Children.Add(Text(OperatingSystem.IsLinux() ? "托盘支持取决于桌面环境；没有托盘时请保留此设置窗口。" : "关闭此窗口后，仍可从系统托盘打开设置或退出。"));
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        footer.Children.Add(Button("试用模拟数据", async () => { _scan?.Cancel(); await _app.StartDemoAsync(); ShowMessage("当前显示模拟数据。连接真实手表即可退出演示。"); }));
        footer.Children.Add(Button("退出", () => _app.ExitAsync())); body.Children.Add(footer);
        Content = new ScrollViewer { Content = body };
        _locked.IsCheckedChanged += (_, _) => { if (!_refreshing) _app.SetLocked(_locked.IsChecked == true); };
        _animation.IsCheckedChanged += (_, _) => { if (!_refreshing) _app.SetHeartAnimation(_animation.IsChecked == true); };
        _opacity.ValueChanged += (_, _) => { if (!_refreshing) _app.SetOpacity(_opacity.Value / 100); };
        Closing += (_, e) => { _scan?.Cancel(); if (!AllowClose) { e.Cancel = true; if (OperatingSystem.IsLinux()) ShowMessage("请最小化此窗口；使用“退出”结束应用。"); else Hide(); } };
        Refresh();
    }
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#A5B1C3"), FontSize = 12 };
    private Button Button(string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { ShowMessage(ex.Message); } finally { button.IsEnabled = true; } };
        return button;
    }
    public void Refresh()
    {
        _refreshing = true;
        _locked.IsChecked = _app.Settings.Locked; _animation.IsChecked = _app.Settings.AnimateHeart;
        _opacity.Value = _app.Settings.BackgroundOpacity * 100; _opacityLabel.Text = $"背景不透明度　{_opacity.Value:0}%";
        _connection.Text = (_app.IsDemo ? "演示模式 · " : "") + _app.CurrentStatus.Message;
        _refreshing = false;
    }
    public void ShowMessage(string message) => _message.Text = message;
    private async Task ScanAsync()
    {
        if (_scan is not null) return;
        using var cancellation = _scan = new(); _scanButton.IsEnabled = _connectButton.IsEnabled = false;
        _devices.ItemsSource = null; ShowMessage("正在扫描，请保持手表的心率广播开启…");
        try {
            var devices = await _app.ScanAsync(cancellation.Token); _devices.ItemsSource = devices;
            if (devices.Count > 0) _devices.SelectedIndex = 0;
            ShowMessage(devices.Count == 0 ? "未发现心率设备。请确认手表已按 START 开始广播、电脑蓝牙已开启，然后重试。" : $"发现 {devices.Count} 台设备，请选择手表并连接。");
        } catch (OperationCanceledException) { ShowMessage("扫描已取消"); }
        catch (Exception ex) { ShowMessage(ex.Message); }
        finally { _scan = null; _scanButton.IsEnabled = _connectButton.IsEnabled = true; }
    }
}
