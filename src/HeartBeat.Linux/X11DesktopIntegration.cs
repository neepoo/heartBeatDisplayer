using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using HeartBeat.Desktop;

namespace HeartBeat.Linux;

public sealed class X11DesktopIntegration : IDesktopIntegration, IDesktopSmokeChecks
{
    private nint _display, _window, _root;
    private Window? _managedWindow;
    private DispatcherTimer? _timer;
    private readonly List<(int Key, uint Modifiers)> _grabs = [];
    private readonly HashSet<uint> _pressed = [];
    private int _hideKey, _lockKey;
    private bool _shape, _disposed;
    private bool _locked;
    private bool _wayland;
    private int _expectedGrabs;
    public string? Warning { get; private set; }
    public bool SupportsClickThrough => _shape && _display != 0;
    public event Action? ToggleVisibility;
    public event Action? ToggleLock;

    public void Attach(Window window)
    {
        if (_display != 0 || _managedWindow is not null) throw new InvalidOperationException("Desktop integration is already attached.");
        _managedWindow = window;
        if (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            _wayland = true;
            Warning = "Wayland 会话不支持本应用的全局快捷键、鼠标穿透和可靠置顶；请登录 Ubuntu on Xorg 会话。";
            return;
        }
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor != "XID") { Warning = "当前窗口后端不是 X11，鼠标穿透和全局快捷键不可用。"; return; }
        _window = handle.Handle;
        _display = XOpenDisplay(0);
        if (_display == 0) { Warning = "无法打开 X11 DISPLAY，鼠标穿透和全局快捷键不可用。"; return; }
        _root = XDefaultRootWindow(_display);
        _shape = XShapeQueryExtension(_display, out _, out _) != 0;
        if (!_shape) Warning = "X11 缺少 Shape 扩展，鼠标穿透不可用。";
        _hideKey = XKeysymToKeycode(_display, 0x68); // h
        _lockKey = XKeysymToKeycode(_display, 0x6c); // l
        uint lockMask = ModifierFor(0xff7f) | ModifierFor(0xff14) | 2; // NumLock, ScrollLock, CapsLock
        var variants = new HashSet<uint> { 0 };
        for (uint bit = 1; bit <= 128; bit <<= 1)
            if ((lockMask & bit) != 0) foreach (uint value in variants.ToArray()) variants.Add(value | bit);
        _expectedGrabs = 2 * variants.Count;
        XkbSetDetectableAutoRepeat(_display, 1, out _);
        // X errors are asynchronous. Check each grab on this connection and preserve
        // Avalonia's existing error handler for errors belonging to its connection.
        nint previous = 0;
        bool failed = false;
        bool shortcutWarning = false;
        XErrorHandler handler = (display, error) => {
            if (display == _display) { failed = true; return 0; }
            return previous == 0 ? 0 : Marshal.GetDelegateForFunctionPointer<XErrorHandler>(previous)(display, error);
        };
        previous = XSetErrorHandler(handler);
        try
        {
            foreach (int key in new[] { _hideKey, _lockKey })
                foreach (uint modifiers in variants.Select(v => v | 4u | 8u))
                {
                    failed = false;
                    XGrabKey(_display, key, modifiers, _root, 0, 1, 1);
                    XSync(_display, 0);
                    if (!failed) _grabs.Add((key, modifiers));
                    else shortcutWarning = true;
                }
        }
        finally { XSetErrorHandlerPointer(previous); GC.KeepAlive(handler); }
        if (shortcutWarning) Warning = JoinWarning("部分 Ctrl+Alt+H / Ctrl+Alt+L 快捷键被其他应用占用，请使用托盘菜单。");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += PollKeys;
        _timer.Start();
        window.PropertyChanged += WindowPropertyChanged;
        Apply(false);
    }

    private string JoinWarning(string text) => Warning is null ? text : Warning + " " + text;
    private void WindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Avalonia.Visual.IsVisibleProperty)
            if (_managedWindow?.IsVisible == true) Apply(_locked);
    }
    // Window visibility changes can remap its XID; reassert EWMH hints after showing.

    public void Apply(bool locked)
    {
        _locked = locked;
        if (_display == 0 || _disposed) return;
        SendState("_NET_WM_STATE_ABOVE");
        SendState("_NET_WM_STATE_SKIP_TASKBAR");
        SendState("_NET_WM_STATE_SKIP_PAGER");
        // The overlay never takes keyboard focus; its separate settings window does.
        var hints = new WMHints { Flags = 1, Input = 0 };
        XSetWMHints(_display, _window, ref hints);
        if (_shape)
        {
            if (locked) XShapeCombineRectangles(_display, _window, 2, 0, 0, 0, 0, 0, 0);
            else XShapeCombineMask(_display, _window, 2, 0, 0, 0, 0);
        }
        XFlush(_display);
    }
    private void SendState(string atom)
    {
        var ev = new XEvent { Type = 33, SendEvent = 1, Display = _display, Window = _window,
            MessageType = XInternAtom(_display, "_NET_WM_STATE", 0), Format = 32,
            Data0 = 1, Data1 = XInternAtom(_display, atom, 0), Data3 = 1 };
        XSendEvent(_display, _root, 0, (nint)((1 << 20) | (1 << 19)), ref ev);
    }
    private void PollKeys(object? sender, EventArgs args)
    {
        if (_display == 0 || _disposed) return;
        while (XPending(_display) > 0)
        {
            XNextEvent(_display, out var ev);
            if (ev.Type == 3) { _pressed.Remove(ev.Keycode); continue; }
            if (ev.Type != 2 || !_pressed.Add(ev.Keycode)) continue;
            if (ev.Keycode == _hideKey) ToggleVisibility?.Invoke();
            else if (ev.Keycode == _lockKey) ToggleLock?.Invoke();
        }
    }
    private uint ModifierFor(nuint keysym)
    {
        byte key = XKeysymToKeycode(_display, keysym);
        nint pointer = XGetModifierMapping(_display);
        if (pointer == 0) return 0;
        try
        {
            var map = Marshal.PtrToStructure<ModifierMap>(pointer);
            uint mask = 0;
            for (int mod = 0; mod < 8; mod++)
                for (int i = 0; i < map.Count; i++)
                    if (key != 0 && Marshal.ReadByte(map.Keys, mod * map.Count + i) == key) mask |= 1u << mod;
            return mask;
        }
        finally { XFreeModifiermap(pointer); }
    }

    // The caller yields to the WM after Apply. These are server observations,
    // not cached requested state: a missing WM must fail the EWMH checks.
    public IReadOnlyList<(string Name, bool Passed)> CheckNativeState(bool locked)
    {
        if (_wayland) return []; // Explicitly unsupported, with Warning shown by the UI.
        if (_display == 0 || _window == 0 || _disposed) return [("native-x11-available", false)];
        var checks = new List<(string Name, bool Passed)>();
        bool nativeError = false;
        nint previous = 0;
        XErrorHandler handler = (display, error) => {
            if (display == _display) { nativeError = true; return 0; }
            return previous == 0 ? 0 : Marshal.GetDelegateForFunctionPointer<XErrorHandler>(previous)(display, error);
        };
        previous = XSetErrorHandler(handler);
        try
        {
            XSync(_display, 0);
            var states = ReadAtoms("_NET_WM_STATE");
            checks.Add(("native-x11-above", states.Contains(XInternAtom(_display, "_NET_WM_STATE_ABOVE", 0))));
            checks.Add(("native-x11-skip-taskbar", states.Contains(XInternAtom(_display, "_NET_WM_STATE_SKIP_TASKBAR", 0))));
            nint hintsPointer = XGetWMHints(_display, _window);
            try
            {
                var hints = hintsPointer == 0 ? default : Marshal.PtrToStructure<WMHints>(hintsPointer);
                checks.Add(("native-x11-no-activation", hintsPointer != 0 && (hints.Flags & 1) != 0 && hints.Input == 0));
            }
            finally { if (hintsPointer != 0) XFree(hintsPointer); }

            bool correctInput = false;
            if (_shape)
            {
                nint rectangles = XShapeGetRectangles(_display, _window, 2, out int count, out _);
                try
                {
                    if (locked) correctInput = count == 0;
                    else if (rectangles != 0 && count > 0 &&
                        XGetGeometry(_display, _window, out _, out _, out _, out uint width, out uint height, out _, out _) != 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var rect = Marshal.PtrToStructure<XRectangle>(rectangles + i * Marshal.SizeOf<XRectangle>());
                            if (rect.X <= 0 && rect.Y <= 0 && rect.X + rect.Width >= width && rect.Y + rect.Height >= height)
                                correctInput = true;
                        }
                    }
                }
                finally { if (rectangles != 0) XFree(rectangles); }
            }
            checks.Add((locked ? "native-x11-input-empty" : "native-x11-input-restored", correctInput));
            checks.Add(("native-x11-hotkey-grabs", _hideKey != 0 && _lockKey != 0 && _expectedGrabs > 0 && _grabs.Count == _expectedGrabs));
            XSync(_display, 0);
        }
        catch (Exception ex)
        {
            nativeError = true;
            System.Diagnostics.Trace.WriteLine("X11 native smoke query: " + ex.Message);
        }
        finally { XSetErrorHandlerPointer(previous); GC.KeepAlive(handler); }
        checks.Add(("native-x11-query-success", !nativeError));
        return checks;
    }

    private HashSet<nint> ReadAtoms(string name)
    {
        var result = new HashSet<nint>();
        int status = XGetWindowProperty(_display, _window, XInternAtom(_display, name, 0), 0, 1024, 0, 4,
            out nint actualType, out int format, out nuint count, out nuint remaining, out nint data);
        try
        {
            if (status != 0 || actualType != 4 || format != 32 || remaining != 0) return result;
            // Xlib expands format-32 properties into native longs on Linux x64.
            for (nuint i = 0; i < count; i++) result.Add(Marshal.ReadIntPtr(data, checked((int)i * IntPtr.Size)));
            return result;
        }
        finally { if (data != 0) XFree(data); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= PollKeys;
        if (_managedWindow is not null) _managedWindow.PropertyChanged -= WindowPropertyChanged;
        if (_display != 0)
        {
            foreach (var (key, modifiers) in _grabs) XUngrabKey(_display, key, modifiers, _root);
            XSync(_display, 0);
            XCloseDisplay(_display);
            _display = 0;
        }
        ToggleVisibility = null; ToggleLock = null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct ModifierMap { public int Count; public nint Keys; }
    [StructLayout(LayoutKind.Sequential)] private struct XRectangle { public short X, Y; public ushort Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct WMHints
    {
        public nint Flags; public int Input; public int InitialState; public nint IconPixmap, IconWindow;
        public int IconX, IconY; public nint IconMask, WindowGroup;
    }
    // XEvent is a 24-long union on the supported Linux x64 ABI.
    [StructLayout(LayoutKind.Explicit, Size = 192)] private struct XEvent
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(16)] public int SendEvent;
        [FieldOffset(24)] public nint Display;
        [FieldOffset(32)] public nint Window;
        [FieldOffset(40)] public nint MessageType;
        [FieldOffset(48)] public int Format;
        [FieldOffset(56)] public nint Data0;
        [FieldOffset(64)] public nint Data1;
        [FieldOffset(80)] public nint Data3;
        [FieldOffset(84)] public uint Keycode;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int XErrorHandler(nint display, nint error);
    private const string Xlib = "libX11.so.6", Xext = "libXext.so.6";
    [DllImport(Xlib)] private static extern nint XOpenDisplay(nint name);
    [DllImport(Xlib)] private static extern int XCloseDisplay(nint display);
    [DllImport(Xlib)] private static extern nint XDefaultRootWindow(nint display);
    [DllImport(Xlib)] private static extern nint XInternAtom(nint display, string name, int onlyIfExists);
    [DllImport(Xlib)] private static extern int XSendEvent(nint display, nint window, int propagate, nint eventMask, ref XEvent ev);
    [DllImport(Xlib)] private static extern int XSetWMHints(nint display, nint window, ref WMHints hints);
    [DllImport(Xlib)] private static extern nint XGetWMHints(nint display, nint window);
    [DllImport(Xlib)] private static extern int XFree(nint data);
    [DllImport(Xlib)] private static extern int XGetWindowProperty(nint display, nint window, nint property, nint offset, nint length, int delete, nint requestedType, out nint actualType, out int actualFormat, out nuint count, out nuint remaining, out nint data);
    [DllImport(Xlib)] private static extern int XGetGeometry(nint display, nint drawable, out nint root, out int x, out int y, out uint width, out uint height, out uint borderWidth, out uint depth);
    [DllImport(Xlib)] private static extern int XFlush(nint display);
    [DllImport(Xlib)] private static extern int XSync(nint display, int discard);
    [DllImport(Xlib)] private static extern byte XKeysymToKeycode(nint display, nuint keysym);
    [DllImport(Xlib)] private static extern int XGrabKey(nint display, int key, uint modifiers, nint window, int ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(Xlib)] private static extern int XUngrabKey(nint display, int key, uint modifiers, nint window);
    [DllImport(Xlib)] private static extern int XPending(nint display);
    [DllImport(Xlib)] private static extern int XNextEvent(nint display, out XEvent ev);
    [DllImport(Xlib)] private static extern nint XGetModifierMapping(nint display);
    [DllImport(Xlib)] private static extern int XFreeModifiermap(nint map);
    [DllImport(Xlib)] private static extern int XkbSetDetectableAutoRepeat(nint display, int detectable, out int supported);
    [DllImport(Xlib)] private static extern nint XSetErrorHandler(XErrorHandler handler);
    [DllImport(Xlib, EntryPoint = "XSetErrorHandler")] private static extern nint XSetErrorHandlerPointer(nint handler);
    [DllImport(Xext)] private static extern int XShapeQueryExtension(nint display, out int eventBase, out int errorBase);
    [DllImport(Xext)] private static extern nint XShapeGetRectangles(nint display, nint window, int kind, out int count, out int ordering);
    [DllImport(Xext)] private static extern void XShapeCombineRectangles(nint display, nint window, int kind, int x, int y, nint rectangles, int count, int operation, int ordering);
    [DllImport(Xext)] private static extern void XShapeCombineMask(nint display, nint window, int kind, int x, int y, nint pixmap, int operation);
}
