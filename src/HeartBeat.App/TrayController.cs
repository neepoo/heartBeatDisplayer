using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace HeartBeat.App;

public sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon _tray;
    private readonly Icon _icon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.ToolStripMenuItem _lock;
    public TrayController(Action settings, Action toggleVisible, Action toggleLock, Action reset, Action exit)
    {
        _icon = CreateIcon();
        _menu.Items.Add("连接与设置…", null, (_, _) => settings());
        _menu.Items.Add("显示 / 隐藏    Ctrl+Alt+H", null, (_, _) => toggleVisible());
        _lock = new Forms.ToolStripMenuItem("锁定 / 鼠标穿透    Ctrl+Alt+L", null, (_, _) => toggleLock());
        _menu.Items.Add(_lock);
        _menu.Items.Add("重置悬浮窗位置", null, (_, _) => reset());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => exit());
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "HeartBeat · 心率悬浮窗", ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (_, _) => settings();
    }
    public void Update(bool locked, int? bpm, bool demo)
    {
        _lock.Checked = locked;
        _tray.Text = $"HeartBeat · {(demo ? "模拟 " : "")}{(bpm.HasValue ? bpm + " BPM" : "等待心率")}";
    }
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(23, 27, 36));
            g.FillEllipse(background, 1, 1, 30, 30);
            using var pen = new Pen(Color.FromArgb(251, 113, 133), 2.6f) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, new PointF[] { new(5, 17), new(11, 17), new(14, 9), new(18, 24), new(21, 14), new(24, 17), new(28, 17) });
        }
        var handle = bitmap.GetHicon();
        try { using var temporary = Icon.FromHandle(handle); return (Icon)temporary.Clone(); }
        finally { DestroyIcon(handle); }
    }
    public void Dispose() { _tray.Visible = false; _tray.Dispose(); _menu.Dispose(); _icon.Dispose(); }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}
