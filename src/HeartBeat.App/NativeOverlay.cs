using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace HeartBeat.App;

public sealed class NativeOverlay : IDisposable
{
    private readonly OverlayWindow _window;
    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly List<int> _hotkeys = new();
    public string? HotkeyWarning { get; private set; }
    public event Action? ToggleVisibility;
    public event Action? ToggleLock;
    public NativeOverlay(OverlayWindow window)
    {
        _window = window;
        _handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle)!;
        _source.AddHook(WindowMessage);
        Register(1, 0x48, "Ctrl+Alt+H"); Register(2, 0x4C, "Ctrl+Alt+L");
    }
    public void Apply(bool locked)
    {
        var style = GetWindowLongPtr(_handle, -20).ToInt64() | 0x08000000L | 0x80L;
        style = locked ? style | 0x20L : style & ~0x20L;
        SetWindowLongPtr(_handle, -20, new IntPtr(style));
        SetWindowPos(_handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0020);
    }
    public bool IsClickThrough => (GetWindowLongPtr(_handle, -20).ToInt64() & 0x20L) != 0;
    public bool IsNonActivating => (GetWindowLongPtr(_handle, -20).ToInt64() & 0x08000000L) != 0;
    private void Register(int id, uint key, string label)
    {
        if (RegisterHotKey(_handle, id, 0x4003, key)) _hotkeys.Add(id);
        else HotkeyWarning = (HotkeyWarning is null ? "快捷键被占用，请使用托盘菜单：" : HotkeyWarning + "、") + label;
    }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312)
        {
            if (wParam.ToInt32() == 1) ToggleVisibility?.Invoke();
            if (wParam.ToInt32() == 2) ToggleLock?.Invoke();
            handled = true;
        }
        if (msg is 0x007E or 0x02E0)
            _window.Dispatcher.BeginInvoke(new Action(ClampToScreen));
        return IntPtr.Zero;
    }
    public void ResetPosition()
    {
        var area = WorkAreas().First(a => a.Primary).Area;
        _window.Left = area.Right - _window.Width - 24;
        _window.Top = area.Top + 24;
        ClampToScreen();
    }
    public void ClampToScreen()
    {
        if (!double.IsFinite(_window.Left) || !double.IsFinite(_window.Top)) { ResetPosition(); return; }
        var bounds = new Rect(_window.Left, _window.Top, _window.Width, _window.Height);
        var areas = WorkAreas();
        var target = areas.OrderByDescending(a => IntersectionArea(a.Area, bounds)).First();
        if (IntersectionArea(target.Area, bounds) == 0) { ResetPosition(); return; }
        _window.Left = Math.Clamp(_window.Left, target.Area.Left, Math.Max(target.Area.Left, target.Area.Right - _window.Width));
        _window.Top = Math.Clamp(_window.Top, target.Area.Top, Math.Max(target.Area.Top, target.Area.Bottom - _window.Height));
    }
    private List<(Rect Area, bool Primary)> WorkAreas()
    {
        var transform = _source.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return Forms.Screen.AllScreens.Select(s => {
            var r = s.WorkingArea;
            return (new Rect(transform.Transform(new Point(r.Left, r.Top)), transform.Transform(new Point(r.Right, r.Bottom))), s.Primary);
        }).ToList();
    }
    private static double IntersectionArea(Rect a, Rect b) { a.Intersect(b); return a.IsEmpty ? 0 : a.Width * a.Height; }
    public void Dispose()
    {
        foreach (var id in _hotkeys) UnregisterHotKey(_handle, id);
        _source.RemoveHook(WindowMessage);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
