using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HeartBeat.Core;

namespace HeartBeat.Desktop;

public sealed class HeartRateChart : Control
{
    private IReadOnlyList<HeartRateSample> _points = Array.Empty<HeartRateSample>();
    private DateTimeOffset _now;
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(128, 140, 158));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(251, 113, 133));
    public void Update(IReadOnlyList<HeartRateSample> points, DateTimeOffset now) { _points = points; _now = now; InvalidateVisual(); }
    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        var width = Math.Max(1, Bounds.Width - 28);
        var height = Math.Max(1, Bounds.Height - 17);
        var values = _points.Where(p => p.Bpm.HasValue).Select(p => p.Bpm!.Value).ToArray();
        var low = values.Length == 0 ? 50 : Math.Max(0, Math.Floor((values.Min() - 10) / 10d) * 10);
        var high = values.Length == 0 ? 110 : Math.Ceiling((values.Max() + 10) / 10d) * 10;
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(38, 160, 175, 192)), 0.7) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
        dc.DrawLine(gridPen, new(0, 2), new(width, 2));
        dc.DrawLine(gridPen, new(0, height), new(width, height));
        Text(dc, high.ToString(CultureInfo.InvariantCulture), new(width + 5, -3), 9);
        Text(dc, low.ToString(CultureInfo.InvariantCulture), new(width + 5, height - 6), 9);
        Text(dc, "−5 分钟", new(0, height + 4), 9);
        Text(dc, "现在", new(width - 22, height + 4), 9);
        if (values.Length == 0)
        { Text(dc, "等待心率数据", new(65, height / 2 - 6), 10); return; }
        Point Position(HeartRateSample p) => new(width * (1 - (_now - p.Timestamp).TotalSeconds / 300), 2 + (height - 2) * (high - p.Bpm!.Value) / (high - low));
        var geometry = new StreamGeometry();
        HeartRateSample? previous = null;
        using (var ctx = geometry.Open())
        {
            foreach (var point in _points)
            {
                if (point.Bpm is null || point.Timestamp > _now || _now - point.Timestamp >= HeartRateHistory.Window) { previous = null; continue; }
                var position = Position(point);
                if (previous is null || point.Timestamp - previous.Timestamp >= HeartRateHistory.Freshness)
                    ctx.BeginFigure(position, false);
                else ctx.LineTo(position);
                previous = point;
            }
        }
        using var clip = dc.PushClip(new Rect(-3, -2, width + 6, height + 5));
        dc.DrawGeometry(null, new Pen(Accent, 1.8) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
        if (previous is not null && _now - previous.Timestamp < HeartRateHistory.Freshness)
        {
            var last = Position(previous);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(38, 251, 113, 133)), null, last, 6, 6);
            dc.DrawEllipse(Accent, null, last, 2.5, 2.5);
        }
    }
    private void Text(DrawingContext dc, string text, Point point, double size) => dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Segoe UI, Microsoft YaHei UI"), size, Muted), point);
}
