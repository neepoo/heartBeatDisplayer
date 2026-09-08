using System.Runtime.InteropServices;
using Avalonia.Controls;
using HeartBeat.Desktop;

namespace HeartBeat.App;

public sealed class WindowsDesktopIntegration : IDesktopIntegration, IDesktopSmokeChecks
{
    private Window? _window;
    private nint _handle;
    private nint _previousProc;
    private WndProc? _proc;
    private readonly List<int> _hotkeys = [];
    private bool _locked;
    public string? Warning { get; private set; }
    public bool SupportsClickThrough => true;
    public event Action? ToggleVisibility;
    public event Action? ToggleLock;
    public void Attach(Window window)
    {
        _window = window;
        window.Opened += OnOpened;
        Install();
    }
    private void OnOpened(object? sender, EventArgs e) { Install(); Apply(_locked); }
    private void Install()
    {
        if (_handle != 0) return;
        _handle = _window!.TryGetPlatformHandle()?.Handle ?? 0;
        if (_handle == 0) return;
        _proc = WindowMessage;
        _previousProc = SetWindowLongPtr(_handle, -4, Marshal.GetFunctionPointerForDelegate(_proc));
        if (_previousProc == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Register(1, 0x48, "Ctrl+Alt+H"); Register(2, 0x4C, "Ctrl+Alt+L");
        Apply(_locked);
    }
    public void Apply(bool locked)
    {
        _locked = locked; if (_handle == 0) return;
        var style = GetWindowLongPtr(_handle, -20).ToInt64() | 0x08000000L | 0x80L;
        style = locked ? style | 0x20L : style & ~0x20L;
        SetWindowLongPtr(_handle, -20, new nint(style));
        SetWindowPos(_handle, new nint(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0020);
    }
    private void Register(int id, uint key, string label)
    {
        if (RegisterHotKey(_handle, id, 0x4003, key)) _hotkeys.Add(id);
        else Warning = (Warning is null ? "快捷键被占用，请使用托盘菜单：" : Warning + "、") + label;
    }
    private nint WindowMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == 0x21) return 3; // MA_NOACTIVATE, including mouse dragging.
        if (msg == 0x84)
        {
            if (_locked) return 1;
            if (GetWindowRect(hwnd, out var rect)) {
                var x = (short)(lParam.ToInt64() & 0xffff); var y = (short)((lParam.ToInt64() >> 16) & 0xffff);
                var edge = 8 * (_window?.RenderScaling ?? 1);
                if (x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom) {
                    var left = x < rect.Left + edge; var right = x >= rect.Right - edge;
                    var top = y < rect.Top + edge; var bottom = y >= rect.Bottom - edge;
                    var hit = top ? (left ? 13 : right ? 14 : 12) : bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 0;
                    if (hit != 0) return hit;
                }
            }
        }
        if (msg == 0x312) {
            if (wParam == 1) ToggleVisibility?.Invoke();
            if (wParam == 2) ToggleLock?.Invoke();
            return 0;
        }
        return CallWindowProc(_previousProc, hwnd, msg, wParam, lParam);
    }
    public void Dispose()
    {
        if (_window is not null) _window.Opened -= OnOpened;
        foreach (var id in _hotkeys) UnregisterHotKey(_handle, id);
        _hotkeys.Clear();
        if (_handle != 0 && _previousProc != 0) SetWindowLongPtr(_handle, -4, _previousProc);
        _handle = 0; _previousProc = 0; _proc = null;
    }
    public IReadOnlyList<(string Name, bool Passed)> CheckNativeState(bool locked)
    {
        var style = GetWindowLongPtr(_handle, -20).ToInt64();
        GetWindowRect(_handle, out var rect);
        var point = (long)(ushort)(rect.Right - 5) | ((long)(ushort)(rect.Bottom - 5) << 16);
        var hit = SendMessage(_handle, 0x84, 0, new nint(point)).ToInt32();
        return [
            ("Windows native nonactivation style", (style & 0x08000000) != 0),
            ("Windows native topmost style", (style & 0x8) != 0),
            ("Windows native mouse activation is suppressed", SendMessage(_handle, 0x21, 0, 0) == 3),
            ($"Windows native clickthrough matches locked={locked}", ((style & 0x20) != 0) == locked),
            ($"Windows native resize target matches locked={locked}", locked ? hit < 10 || hit > 17 : hit == 17)
        ];
    }
    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern nint CallWindowProc(nint previous, nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out WindowRect rect);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
}
