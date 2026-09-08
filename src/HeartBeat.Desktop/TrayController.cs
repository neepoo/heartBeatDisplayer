using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace HeartBeat.Desktop;

public sealed class TrayController : IDisposable
{
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _locked;
    public TrayController(Action settings, Action visible, Action locked, Action reset, Action exit)
    {
        var menu = new NativeMenu();
        menu.Items.Add(Item("连接与设置…", settings));
        menu.Items.Add(Item("显示 / 隐藏", visible));
        _locked = Item("锁定 / 鼠标穿透", locked); _locked.ToggleType = MenuItemToggleType.CheckBox; menu.Items.Add(_locked);
        menu.Items.Add(Item("重置悬浮窗位置", reset)); menu.Items.Add(new NativeMenuItemSeparator()); menu.Items.Add(Item("退出", exit));
        using var bitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using (var dc = bitmap.CreateDrawingContext()) {
            dc.DrawEllipse(Brush.Parse("#171B24"), null, new Point(16, 16), 15, 15);
            var pen = new Pen(Brush.Parse("#FB7185"), 2.6);
            Point[] points = [new(5,17), new(11,17), new(14,9), new(18,24), new(21,14), new(24,17), new(28,17)];
            for (var i = 1; i < points.Length; i++) dc.DrawLine(pen, points[i - 1], points[i]);
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
        _tray = new TrayIcon { Icon = new WindowIcon(stream), ToolTipText = "HeartBeat · 心率悬浮窗", Menu = menu, IsVisible = true };
        _tray.Clicked += (_, _) => settings();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
    }
    private static NativeMenuItem Item(string label, Action action)
    { var item = new NativeMenuItem(label); item.Click += (_, _) => action(); return item; }
    public void Update(bool locked, int? bpm, bool demo)
    { _locked.IsChecked = locked; _tray.ToolTipText = $"HeartBeat · {(demo ? "模拟 " : "")}{(bpm.HasValue ? bpm + " BPM" : "等待心率")}"; }
    public void Dispose() { _tray.IsVisible = false; _tray.Dispose(); }
}
