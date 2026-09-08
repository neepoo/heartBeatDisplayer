using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HeartBeat.Core;

namespace HeartBeat.Desktop;

public sealed class OverlayWindow : Window
{
    private readonly Border _card;
    private readonly Grid _layout;
    private readonly TextBlock _title = Label("HEARTBEAT", 10, "#A7B4C7");
    private readonly TextBlock _mode = Label("拖动边缘缩放", 9, "#7F8B9E");
    private readonly TextBlock _bpm = Label("--", 48, "#F4F7FC");
    private readonly TextBlock _status = Label("未连接", 9, "#9AA7BA");
    private readonly TextBlock _heart = Label("♥", 32, "#FB7185");
    private readonly TextBlock _grip = Label("◢", 12, "#8795A9");
    private readonly ScaleTransform _pulse = new(1, 1);
    private readonly DispatcherTimer _animation;
    private int? _currentBpm;
    private bool _animateHeart = true;
    private readonly long _epoch = Environment.TickCount64;
    public TextBlock AverageLabel { get; } = Label("--", 16, "#EDF1F7");
    public TextBlock MinimumLabel { get; } = Label("--", 16, "#A8D7CE");
    public TextBlock MaximumLabel { get; } = Label("--", 16, "#FDB1BB");
    public HeartRateChart Chart { get; } = new();
    public bool IsLocked { get; private set; }
    public bool AllowClose { get; set; }
    public bool IsHeartAnimating => _animation.IsEnabled;
    public string DisplayedBpm => _bpm.Text ?? "--";
    public event Action? PositionEdited;

    public OverlayWindow()
    {
        Title = "HeartBeat · 心率悬浮窗";
        Width = OverlayLayout.DefaultWidth; Height = OverlayLayout.DefaultHeight;
        MinWidth = OverlayLayout.MinimumWidth; MinHeight = OverlayLayout.MinimumHeight;
        MaxWidth = OverlayLayout.MaximumWidth; MaxHeight = OverlayLayout.MaximumHeight;
        WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; CanResize = true;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, PingFang SC, Noto Sans CJK SC, sans-serif");
        _layout = new Grid { RowDefinitions = new RowDefinitions("20,62,*,42") };
        var heading = new Grid(); heading.Children.Add(_title);
        _mode.HorizontalAlignment = HorizontalAlignment.Right; heading.Children.Add(_mode);
        _layout.Children.Add(heading);
        var reading = new Grid(); Grid.SetRow(reading, 1);
        var values = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 8 };
        _heart.RenderTransform = _pulse; _heart.RenderTransformOrigin = RelativePoint.Center;
        _heart.VerticalAlignment = VerticalAlignment.Center; values.Children.Add(_heart); values.Children.Add(_bpm);
        values.Children.Add(new TextBlock { Text = "BPM", FontSize = 10, Foreground = Brush.Parse("#8795A9"), Margin = new Thickness(0, 28, 0, 0) });
        reading.Children.Add(values); _status.HorizontalAlignment = HorizontalAlignment.Right;
        _status.VerticalAlignment = VerticalAlignment.Center; _status.MaxWidth = 80; _status.TextTrimming = TextTrimming.CharacterEllipsis;
        reading.Children.Add(_status); _layout.Children.Add(reading);
        Chart.Margin = new Thickness(0, 4, 0, 6); Grid.SetRow(Chart, 2); _layout.Children.Add(Chart);
        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var averages = Stat("5分钟均值", AverageLabel); stats.Children.Add(averages);
        var minimum = Stat("最低", MinimumLabel); minimum.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(minimum, 1); stats.Children.Add(minimum);
        var maximum = Stat("最高", MaximumLabel); maximum.HorizontalAlignment = HorizontalAlignment.Right; maximum.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(maximum, 2); stats.Children.Add(maximum);
        var rule = new Border { BorderBrush = Brush.Parse("#30465268"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 7, 0, 0), Child = stats };
        Grid.SetRow(rule, 3); _layout.Children.Add(rule);
        _grip.HorizontalAlignment = HorizontalAlignment.Right; _grip.VerticalAlignment = VerticalAlignment.Bottom;
        _grip.Margin = new Thickness(0, 0, -8, -6); _grip.IsHitTestVisible = false; Grid.SetRowSpan(_grip, 4); _layout.Children.Add(_grip);
        _card = new Border { CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), BorderBrush = Brush.Parse("#35465268"), Padding = new Thickness(14, 10, 14, 12), Child = _layout };
        Content = _card;
        PointerPressed += BeginDrag;
        PointerReleased += (_, _) => PositionEdited?.Invoke();
        PositionChanged += (_, _) => PositionEdited?.Invoke();
        SizeChanged += (_, _) => { UpdateScale(); PositionEdited?.Invoke(); };
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdatePulse(); };
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        _animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animation.Tick += (_, _) => {
            var phase = (Environment.TickCount64 - _epoch) / 1000d * Math.Clamp(_currentBpm ?? 60, 20, 300) / 60d;
            _pulse.ScaleX = _pulse.ScaleY = 1 + 0.09 * (1 - Math.Cos(phase * Math.PI * 2));
        };
        SetAppearance(false, .8, false);
    }
    private static StackPanel Stat(string title, TextBlock value)
    { var panel = new StackPanel(); panel.Children.Add(Label(title, 9, "#8492A7")); panel.Children.Add(value); return panel; }
    private static TextBlock Label(string text, double size, string color) => new() { Text = text, FontSize = size, Foreground = Brush.Parse(color) };
    private void BeginDrag(object? sender, PointerPressedEventArgs e)
    {
        if (IsLocked || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this); const double edge = 8;
        var left = p.X < edge; var right = p.X >= Bounds.Width - edge;
        var top = p.Y < edge; var bottom = p.Y >= Bounds.Height - edge;
        if (left || right || top || bottom)
            BeginResizeDrag(top ? (left ? WindowEdge.NorthWest : right ? WindowEdge.NorthEast : WindowEdge.North)
                : bottom ? (left ? WindowEdge.SouthWest : right ? WindowEdge.SouthEast : WindowEdge.South)
                : left ? WindowEdge.West : WindowEdge.East, e);
        else BeginMoveDrag(e);
        e.Handled = true;
    }
    public void SetAppearance(bool locked, double opacity, bool demo, bool animateHeart = true)
    {
        IsLocked = locked; CanResize = !locked;
        _card.Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(opacity, .4, .95) * 255), 17, 23, 34));
        _grip.IsVisible = !locked; _mode.Text = locked ? "已锁定" : "拖动边缘缩放";
        _title.Text = demo ? "演示 · 模拟数据" : "HEARTBEAT";
        _animateHeart = animateHeart; UpdatePulse();
    }
    public void Update(int? bpm, IReadOnlyList<HeartRateSample> points, DateTimeOffset now, string status)
    {
        _bpm.Text = bpm?.ToString() ?? "--"; _status.Text = status; Chart.Update(points, now);
        var stats = HeartRateStatistics.Calculate(points, now);
        AverageLabel.Text = stats.Average?.ToString() ?? "--"; MinimumLabel.Text = stats.Minimum?.ToString() ?? "--";
        MaximumLabel.Text = stats.Maximum?.ToString() ?? "--"; _currentBpm = bpm; UpdatePulse();
    }
    private void UpdatePulse()
    {
        if (_animation is null) return;
        _heart.Opacity = _currentBpm is > 0 ? 1 : .35;
        if (_animateHeart && IsVisible && _currentBpm is > 0) _animation.Start();
        else { _animation.Stop(); _pulse.ScaleX = _pulse.ScaleY = 1; }
    }
    private void UpdateScale()
    {
        var scale = Math.Clamp(Math.Min(Bounds.Width / 320, Bounds.Height / 240), .875, 2.5);
        _bpm.FontSize = 48 * scale; _heart.FontSize = 32 * scale;
        _layout.RowDefinitions[1].Height = new GridLength(62 * scale);
        _layout.RowDefinitions[3].Height = new GridLength(42 * scale);
        AverageLabel.FontSize = MinimumLabel.FontSize = MaximumLabel.FontSize = 16 * scale;
    }
}
