using System.Runtime.InteropServices;
using AppKit;
using Avalonia.Controls;
using Avalonia.Threading;
using HeartBeat.Desktop;
using ObjCRuntime;

namespace HeartBeat.Mac;

public sealed class MacDesktopIntegration : IDesktopIntegration, IDesktopSmokeChecks
{
    private Window? _window;
    private NSWindow? _native;
    private NSWindowLevel _originalLevel;
    private NSWindowStyle _originalStyle;
    private NSWindowCollectionBehavior _originalCollection;
    private bool _originalIgnoresMouse, _originalHides;
    private bool _locked, _disposed;
    private CarbonHotKeys? _hotKeys;
    private NonKeyWindowClass? _nonKeyWindow;
    public string? Warning { get; private set; }
    public bool SupportsClickThrough => true;
    public event Action? ToggleVisibility;
    public event Action? ToggleLock;

    public void Attach(Window window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window is not null) throw new InvalidOperationException("Native integration is already attached.");
        _window = window;
        window.ShowActivated = false;
        window.Opened += OnOpened;
        window.Closed += OnClosed;
        AttachNative();
        _hotKeys = new CarbonHotKeys(id =>
        {
            if (_disposed) return;
            if (id == 1) ToggleVisibility?.Invoke();
            else if (id == 2) ToggleLock?.Invoke();
        });
        if (_hotKeys.Warning is { } warning) Warning = warning;
    }

    private void OnOpened(object? sender, EventArgs args) => AttachNative();
    private void OnClosed(object? sender, EventArgs args) => Dispose();

    private void AttachNative()
    {
        if (_native is not null || _window is null || _disposed) return;
        var handle = _window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor != "NSWindow" || handle.Handle == IntPtr.Zero) return;
        _native = Runtime.GetNSObject<NSWindow>(handle.Handle);
        if (_native is null) { Warning = "无法获取 macOS 原生窗口。"; return; }
        _originalLevel = _native.Level;
        _originalStyle = _native.StyleMask;
        _originalCollection = _native.CollectionBehavior;
        _originalIgnoresMouse = _native.IgnoresMouseEvents;
        _originalHides = _native.HidesOnDeactivate;
        // Only this overlay changes Objective-C class. The derived class retains every
        // Avalonia implementation/ivar and overrides only two public AppKit queries.
        _nonKeyWindow = new NonKeyWindowClass(_native.Handle);
        Apply(_locked);
    }

    public void Apply(bool locked)
    {
        if (_disposed) return;
        _locked = locked;
        if (_native is null) return;
        _native.Level = NSWindowLevel.Floating;
        _native.HidesOnDeactivate = false;
        _native.CollectionBehavior = (_originalCollection
            & ~(NSWindowCollectionBehavior.FullScreenPrimary | NSWindowCollectionBehavior.FullScreenNone
                | NSWindowCollectionBehavior.MoveToActiveSpace | NSWindowCollectionBehavior.ParticipatesInCycle))
            | NSWindowCollectionBehavior.CanJoinAllSpaces
            | NSWindowCollectionBehavior.FullScreenAuxiliary
            | NSWindowCollectionBehavior.IgnoresCycle;
        _native.IgnoresMouseEvents = locked;
        // AvnWindow is an NSWindow, not an NSPanel. Do not pretend the panel-only mask
        // prevents application activation for that backend: the per-instance subclass
        // forbids key/main focus, and ShowActivated governs programmatic showing.
        // App-level activation on an unlocked click/drag still needs real Mac validation.
        if (_native is NSPanel) _native.StyleMask = _originalStyle | NSWindowStyle.NonactivatingPanel;
    }

    public IReadOnlyList<(string Name, bool Passed)> CheckNativeState(bool locked)
    {
        if (_native is null || _native.Handle == IntPtr.Zero)
            return [("macOS native NSWindow exists", false)];
        var keyWindow = NSApplication.SharedApplication.KeyWindow;
        // Exercise AppKit's real key-window request in addition to querying the flags.
        // This does not activate the application, and must leave any current key window alone.
        _native.MakeKeyWindow();
        return [
            ("macOS native floating window level", _native.Level == NSWindowLevel.Floating),
            ($"macOS native mouse passthrough matches locked={locked}", _native.IgnoresMouseEvents == locked),
            ("macOS native overlay cannot become key window", !_native.CanBecomeKeyWindow),
            ("macOS native overlay cannot become main window", !_native.CanBecomeMainWindow),
            ("macOS native key-window request preserves existing focus", !_native.IsKeyWindow && NSApplication.SharedApplication.KeyWindow?.Handle == keyWindow?.Handle),
            ("macOS showing overlay does not request activation", _window?.ShowActivated == false),
            ("macOS native overlay remains visible when app deactivates", !_native.HidesOnDeactivate),
            ("macOS native global shortcut registrations succeeded", _hotKeys?.IsRegistered == true)
        ];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hotKeys?.Dispose();
        _hotKeys = null;
        if (_window is not null)
        {
            _window.Opened -= OnOpened;
            _window.Closed -= OnClosed;
        }
        if (_native is not null && _native.Handle != IntPtr.Zero)
        {
            _native.IgnoresMouseEvents = _originalIgnoresMouse;
            _native.Level = _originalLevel;
            _native.StyleMask = _originalStyle;
            _native.CollectionBehavior = _originalCollection;
            _native.HidesOnDeactivate = _originalHides;
        }
        _nonKeyWindow?.Dispose();
        _nonKeyWindow = null;
        // Avalonia owns the NSWindow; do not close or dispose its shared native wrapper.
        _native = null;
        _window = null;
        ToggleVisibility = null;
        ToggleLock = null;
    }
}

/// <summary>Registers only two shortcuts; no keyboard monitor or accessibility permission.</summary>
internal sealed class CarbonHotKeys : IDisposable
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint Signature = 0x48425444; // HBTD
    private const uint ControlOption = (1 << 12) | (1 << 11);
    private static readonly HotKeyHandler Callback = OnHotKey;
    private readonly Action<uint> _pressed;
    private GCHandle _self;
    private IntPtr _handler, _visibilityKey, _lockKey;
    private bool _disposed;
    public string? Warning { get; private set; }
    public bool IsRegistered => !_disposed && _handler != IntPtr.Zero && _visibilityKey != IntPtr.Zero && _lockKey != IntPtr.Zero;

    public CarbonHotKeys(Action<uint> pressed)
    {
        _pressed = pressed;
        _self = GCHandle.Alloc(this);
        var eventType = new EventType { Class = 0x6B657962, Kind = 6 }; // kEventClassKeyboard / hotkey pressed
        var target = GetApplicationEventTarget();
        int status = InstallEventHandler(target, Callback, 1, ref eventType, GCHandle.ToIntPtr(_self), out _handler);
        if (status == 0)
        {
            int visibilityStatus = RegisterEventHotKey(4, ControlOption, new HotKeyId { Signature = Signature, Id = 1 }, target, 0, out _visibilityKey);
            int lockStatus = RegisterEventHotKey(37, ControlOption, new HotKeyId { Signature = Signature, Id = 2 }, target, 0, out _lockKey);
            if (visibilityStatus != 0 || lockStatus != 0)
                Warning = $"部分全局快捷键注册失败（Ctrl+Alt+H: {visibilityStatus}，Ctrl+Alt+L: {lockStatus}），请使用托盘菜单。";
        }
        else
        {
            Warning = $"macOS 全局快捷键注册失败（{status}），请使用托盘菜单。";
            Dispose();
        }
    }

    [MonoPInvokeCallback(typeof(HotKeyHandler))]
    private static int OnHotKey(IntPtr next, IntPtr nativeEvent, IntPtr userData)
    {
        try
        {
            int status = GetEventParameter(nativeEvent, 0x2D2D2D2D, 0x686B6964, IntPtr.Zero,
                (uint)Marshal.SizeOf<HotKeyId>(), IntPtr.Zero, out var id);
            if (status != 0 || id.Signature != Signature || userData == IntPtr.Zero) return -9874;
            if (GCHandle.FromIntPtr(userData).Target is CarbonHotKeys owner && !owner._disposed)
                Dispatcher.UIThread.Post(() => { if (!owner._disposed) owner._pressed(id.Id); });
            return 0;
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("macOS hotkey callback: {0}", error);
            return -9874;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_visibilityKey != IntPtr.Zero) UnregisterEventHotKey(_visibilityKey);
        if (_lockKey != IntPtr.Zero) UnregisterEventHotKey(_lockKey);
        if (_handler != IntPtr.Zero) RemoveEventHandler(_handler);
        _visibilityKey = _lockKey = _handler = IntPtr.Zero;
        if (_self.IsAllocated) _self.Free();
    }

    [StructLayout(LayoutKind.Sequential)] private struct EventType { public uint Class, Kind; }
    [StructLayout(LayoutKind.Sequential)] private struct HotKeyId { public uint Signature, Id; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HotKeyHandler(IntPtr next, IntPtr nativeEvent, IntPtr userData);
    [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target, HotKeyHandler handler, uint count, ref EventType types, IntPtr userData, out IntPtr handlerRef);
    [DllImport(Carbon)] private static extern int RemoveEventHandler(IntPtr handler);
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, HotKeyId id, IntPtr target, uint options, out IntPtr hotKey);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr hotKey);
    [DllImport(Carbon)] private static extern int GetEventParameter(IntPtr nativeEvent, uint name, uint type, IntPtr actualType, uint size, IntPtr actualSize, out HotKeyId result);
}
