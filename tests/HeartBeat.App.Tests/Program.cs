using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using HeartBeat.App;
using HeartBeat.App.Bluetooth;
using HeartBeat.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var smokeArgs = args.Where(a => a != "--probe-bluetooth").Append("--smoke-test").ToArray();
        DesktopOptions options;
        try { options = DesktopOptions.Parse(smokeArgs); }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
        var integration = new VerifiedWindowsIntegration();
        var result = DesktopBootstrap.Run(smokeArgs, () => throw new Exception("Demo initialized real Bluetooth"), () => integration);
        var native = integration.Results.ToList();
        native.Add((integration.Activated ? "FAIL " : "PASS ") + "Windows overlay never activated during startup or reshow");
        if (integration.Activated) result = 1;
        if (!integration.CheckedLocked || !integration.CheckedUnlocked) { native.Add("FAIL Native checks were not exercised"); result = 1; }
        if (args.Contains("--probe-bluetooth")) {
            try {
                var devices = new BleHeartRateTransport().ScanAsync(CancellationToken.None).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(options.SettingsDirectory, "bluetooth-scan.json"), JsonSerializer.Serialize(devices));
                native.Add($"INFO Bluetooth scan returned {devices.Count} candidates (connection not verified).");
            } catch (Exception ex) { native.Add("INFO Hardware probe: " + ex.Message); }
        }
        Directory.CreateDirectory(options.SettingsDirectory);
        File.WriteAllLines(Path.Combine(options.SettingsDirectory, "windows-native-test.txt"), native);
        foreach (var line in native) Console.WriteLine(line);
        return result;
    }
    private sealed class VerifiedWindowsIntegration : IDesktopIntegration, IDesktopSmokeChecks
    {
        private readonly WindowsDesktopIntegration _native = new();
        private Window? _window;
        public readonly HashSet<string> Results = [];
        public bool CheckedLocked { get; private set; }
        public bool CheckedUnlocked { get; private set; }
        public bool Activated { get; private set; }
        public string? Warning => _native.Warning;
        public bool SupportsClickThrough => _native.SupportsClickThrough;
        public event Action? ToggleVisibility { add => _native.ToggleVisibility += value; remove => _native.ToggleVisibility -= value; }
        public event Action? ToggleLock { add => _native.ToggleLock += value; remove => _native.ToggleLock -= value; }
        public void Attach(Window window) { _window = window; window.Activated += (_, _) => Activated = true; _native.Attach(window); }
        public void Apply(bool locked)
        {
            _native.Apply(locked);
            var handle = _window!.TryGetPlatformHandle()!.Handle;
            var style = GetWindowLongPtr(handle, -20).ToInt64();
            Verify("Windows overlay has nonactivating style", (style & 0x08000000) != 0);
            Verify("Windows overlay excludes Alt+Tab tool windows", (style & 0x80) != 0);
            Verify("Windows mouse click does not activate", SendMessage(handle, 0x21, 0, 0) == 3);
            GetWindowRect(handle, out var rect);
            var point = (long)(ushort)(rect.Right - 5) | ((long)(ushort)(rect.Bottom - 5) << 16);
            var hit = SendMessage(handle, 0x84, 0, new nint(point)).ToInt32();
            if (locked) {
                CheckedLocked = true;
                Verify("Windows locked overlay passes mouse input through", (style & 0x20) != 0);
                Verify("Windows locked overlay has no resize target", hit < 10 || hit > 17);
            } else {
                CheckedUnlocked = true;
                Verify("Windows unlocked overlay receives mouse input", (style & 0x20) == 0);
                Verify("Windows bottom-right corner requests native resize", hit == 17);
            }
        }
        private void Verify(string name, bool condition)
        { Results.Add((condition ? "PASS " : "FAIL ") + name); if (!condition) throw new Exception(name); }
        public void Dispose() => _native.Dispose();
        public IReadOnlyList<(string Name, bool Passed)> CheckNativeState(bool locked) => _native.CheckNativeState(locked);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out WindowRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
}
