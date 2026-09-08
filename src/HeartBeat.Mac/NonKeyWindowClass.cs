using System.Runtime.InteropServices;
using ObjCRuntime;

namespace HeartBeat.Mac;

/// <summary>
/// Instance-scoped Objective-C subclassing of Avalonia's window, preserving its original
/// superclass and memory layout. No NSWindow/AvnWindow method is globally replaced.
/// This excludes key/main focus; it does not claim NSPanel's application-nonactivation behavior.
/// </summary>
internal sealed class NonKeyWindowClass : IDisposable
{
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";
    private static readonly BooleanQuery Never = ReturnFalse;
    private static readonly Dictionary<IntPtr, IntPtr> Subclasses = [];
    private readonly IntPtr _window, _originalClass, _subclass;
    private bool _disposed;

    public NonKeyWindowClass(IntPtr window)
    {
        if (window == IntPtr.Zero) throw new ArgumentException("NSWindow handle is required.", nameof(window));
        _window = window;
        _originalClass = object_getClass(window);
        lock (Subclasses)
        {
            if (!Subclasses.TryGetValue(_originalClass, out _subclass))
            {
                _subclass = objc_allocateClassPair(_originalClass, "HeartBeatNonKeyWindow_" + _originalClass.ToInt64().ToString("X"), 0);
                if (_subclass == IntPtr.Zero) throw new InvalidOperationException("Unable to allocate the overlay's Objective-C subclass.");
                try
                {
                    AddQuery("canBecomeKeyWindow");
                    AddQuery("canBecomeMainWindow");
                    if (class_getInstanceSize(_originalClass) != class_getInstanceSize(_subclass))
                        throw new InvalidOperationException("Overlay subclass must preserve the native window's memory layout.");
                }
                catch { objc_disposeClassPair(_subclass); throw; }
                objc_registerClassPair(_subclass);
                Subclasses.Add(_originalClass, _subclass);
            }
        }
        object_setClass(_window, _subclass);
    }

    private void AddQuery(string name)
    {
        var selector = sel_registerName(name);
        var method = class_getInstanceMethod(_originalClass, selector);
        if (method == IntPtr.Zero || !class_addMethod(_subclass, selector, Marshal.GetFunctionPointerForDelegate(Never), method_getTypeEncoding(method)))
            throw new InvalidOperationException("Unable to override " + name + " for the overlay.");
    }

    [MonoPInvokeCallback(typeof(BooleanQuery))]
    private static byte ReturnFalse(IntPtr self, IntPtr selector) => 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Never overwrite a later subclass installed by another native owner.
        if (object_getClass(_window) == _subclass) object_setClass(_window, _originalClass);
        // Registered classes and their static delegate live for the process lifetime:
        // ObjC can cache method pointers, so unregistering a used class is unsafe.
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte BooleanQuery(IntPtr self, IntPtr selector);
    [DllImport(ObjectiveC)] private static extern IntPtr object_getClass(IntPtr instance);
    [DllImport(ObjectiveC)] private static extern IntPtr object_setClass(IntPtr instance, IntPtr nativeClass);
    [DllImport(ObjectiveC)] private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);
    [DllImport(ObjectiveC)] private static extern void objc_registerClassPair(IntPtr nativeClass);
    [DllImport(ObjectiveC)] private static extern void objc_disposeClassPair(IntPtr nativeClass);
    [DllImport(ObjectiveC)] private static extern nuint class_getInstanceSize(IntPtr nativeClass);
    [DllImport(ObjectiveC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjectiveC)] private static extern IntPtr class_getInstanceMethod(IntPtr nativeClass, IntPtr selector);
    [DllImport(ObjectiveC)] private static extern IntPtr method_getTypeEncoding(IntPtr method);
    [DllImport(ObjectiveC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool class_addMethod(IntPtr nativeClass, IntPtr selector, IntPtr implementation, IntPtr types);
}
