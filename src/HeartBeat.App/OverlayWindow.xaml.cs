using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HeartBeat.Core;

namespace HeartBeat.App;

public partial class OverlayWindow : Window
{
    public bool IsLocked { get; private set; }
    public bool AllowClose { get; set; }
    public string DisplayedBpm => BpmLabel.Text;
    public event Action? PositionEdited;
    private int? _currentBpm;
    private int? _animatedBpm;
    private bool _animateHeart = true;
    public OverlayWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateScale();
        IsVisibleChanged += (_, _) => UpdatePulse();
    }
    public void SetAppearance(bool locked, double opacity, bool demo, bool animateHeart = true)
    {
        IsLocked = locked;
        Card.Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(opacity, 0.4, 0.95) * 255), 17, 23, 34));
        ResizeMode = locked ? ResizeMode.NoResize : ResizeMode.CanResize;
        ResizeGrip.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        ModeLabel.Text = locked ? "已锁定" : "拖动边缘缩放";
        TitleLabel.Text = demo ? "演示 · 模拟数据" : "HEARTBEAT";
        _animateHeart = animateHeart; UpdatePulse();
    }
    public void Update(int? bpm, IReadOnlyList<HeartRateSample> points, DateTimeOffset now, string status)
    {
        BpmLabel.Text = bpm?.ToString() ?? "--";
        StatusLabel.Text = status;
        Chart.Update(points, now);
        var stats = HeartRateStatistics.Calculate(points, now);
        AverageLabel.Text = stats.Average?.ToString() ?? "--";
        MinimumLabel.Text = stats.Minimum?.ToString() ?? "--";
        MaximumLabel.Text = stats.Maximum?.ToString() ?? "--";
        _currentBpm = bpm; UpdatePulse();
    }
    private void UpdatePulse()
    {
        var target = _animateHeart && IsVisible && _currentBpm is > 0 ? _currentBpm : null;
        PulseVisual.Opacity = _currentBpm is > 0 ? 1 : 0.35;
        if (target == _animatedBpm) return;
        _animatedBpm = target;
        if (target is int bpm)
        {
            var beat = new DoubleAnimation(1, 1.18, TimeSpan.FromSeconds(30d / Math.Clamp(bpm, 20, 300)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
            PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
        }
        else
        {
            PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }
    private void UpdateScale()
    {
        var scale = Math.Clamp(Math.Min(ActualWidth / OverlayLayout.DefaultWidth, ActualHeight / OverlayLayout.DefaultHeight), 0.875, 2.5);
        BpmLabel.FontSize = 48 * scale;
        PulseHost.Width = PulseHost.Height = 36 * scale;
        ReadingRow.Height = new GridLength(62 * scale);
        StatisticsRow.Height = new GridLength(42 * scale);
        AverageLabel.FontSize = MinimumLabel.FontSize = MaximumLabel.FontSize = 16 * scale;
        UnitLabel.Margin = new Thickness(7, 29 * scale, 0, 0);
    }
    private void DragCard(object sender, MouseButtonEventArgs e)
    {
        if (IsLocked || e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); PositionEdited?.Invoke(); }
        catch (InvalidOperationException) { /* Mouse released before Windows began dragging. */ }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
