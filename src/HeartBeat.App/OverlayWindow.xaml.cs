using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HeartBeat.Core;

namespace HeartBeat.App;

public partial class OverlayWindow : Window
{
    public bool IsLocked { get; private set; }
    public bool AllowClose { get; set; }
    public string DisplayedBpm => BpmLabel.Text;
    public event Action? PositionEdited;
    public OverlayWindow() { InitializeComponent(); }
    public void SetAppearance(bool locked, double opacity, bool demo)
    {
        IsLocked = locked;
        Card.Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(opacity, 0.4, 0.95) * 255), 17, 23, 34));
        ModeLabel.Text = locked ? "已锁定" : "可拖动";
        TitleLabel.Text = demo ? "演示 · 模拟数据" : "HEARTBEAT";
    }
    public void Update(int? bpm, IReadOnlyList<HeartRateSample> points, DateTimeOffset now, string status)
    {
        BpmLabel.Text = bpm?.ToString() ?? "--";
        StatusLabel.Text = status;
        Chart.Update(points, now);
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
