using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using HeartBeat.Core;

namespace HeartBeat.App;

public partial class ControlWindow : Window
{
    private readonly App _app;
    private CancellationTokenSource? _scan;
    private bool _refreshing;
    public bool AllowClose { get; set; }
    public ControlWindow(App app)
    {
        _app = app; InitializeComponent();
        Refresh();
    }
    public void Refresh()
    {
        _refreshing = true;
        LockCheck.IsChecked = _app.Settings.Locked;
        AnimationCheck.IsChecked = _app.Settings.AnimateHeart;
        OpacitySlider.Value = _app.Settings.BackgroundOpacity * 100;
        OpacityLabel.Text = $"{OpacitySlider.Value:0}%";
        ConnectionLabel.Text = (_app.IsDemo ? "演示模式 · " : "") + _app.CurrentStatus.Message;
        _refreshing = false;
    }
    public void ShowMessage(string message) => MessageLabel.Text = message;
    private async void ScanClick(object sender, RoutedEventArgs e)
    {
        if (_scan is not null) return;
        var cancellation = _scan = new();
        ScanButton.IsEnabled = false; ConnectButton.IsEnabled = false;
        MessageLabel.Text = "正在扫描，请保持手表的心率广播开启…"; DeviceList.ItemsSource = null;
        try
        {
            var devices = await _app.ScanAsync(cancellation.Token);
            DeviceList.ItemsSource = devices;
            DeviceCount.Text = $"{devices.Count} 台设备";
            if (devices.Count > 0) DeviceList.SelectedIndex = 0;
            MessageLabel.Text = devices.Count == 0 ? "未发现心率设备。请确认手表已按 START 开始广播、电脑蓝牙已开启，然后重试。" : "请选择你的手表，然后点击连接。";
        }
        catch (OperationCanceledException) { MessageLabel.Text = "扫描已取消"; }
        catch (Exception ex) { MessageLabel.Text = ex.Message; }
        finally { cancellation.Dispose(); _scan = null; ScanButton.IsEnabled = true; ConnectButton.IsEnabled = true; }
    }
    private async void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not HeartRateDevice device) { ShowMessage("请先扫描并选择手表。"); return; }
        ConnectButton.IsEnabled = false;
        try { await _app.ConnectDeviceAsync(device); ShowMessage(""); }
        catch (Exception ex) { ShowMessage("连接失败：" + ex.Message); }
        finally { ConnectButton.IsEnabled = true; }
    }
    private async void DisconnectClick(object sender, RoutedEventArgs e)
    {
        _scan?.Cancel();
        DisconnectButton.IsEnabled = false;
        try { await _app.DisconnectAsync(); }
        catch (Exception ex) { ShowMessage(ex.Message); }
        finally { DisconnectButton.IsEnabled = true; }
    }
    private async void DemoClick(object sender, RoutedEventArgs e)
    {
        _scan?.Cancel(); DemoButton.IsEnabled = false;
        try { await _app.StartDemoAsync(); ShowMessage("当前显示模拟数据。连接真实手表即可退出演示。"); }
        catch (Exception ex) { ShowMessage(ex.Message); }
        finally { DemoButton.IsEnabled = true; }
    }
    private void VisibilityClick(object sender, RoutedEventArgs e) => _app.ToggleVisibility();
    private void LockChanged(object sender, RoutedEventArgs e) { if (!_refreshing && IsLoaded) _app.SetLocked(LockCheck.IsChecked == true); }
    private void AnimationChanged(object sender, RoutedEventArgs e) { if (!_refreshing && IsLoaded) _app.SetHeartAnimation(AnimationCheck.IsChecked == true); }
    private void OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (!_refreshing && IsLoaded) _app.SetOpacity(e.NewValue / 100); }
    protected override void OnClosing(CancelEventArgs e)
    {
        _scan?.Cancel();
        if (!AllowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
