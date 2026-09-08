using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HeartBeat.App;
using HeartBeat.App.Bluetooth;
using HeartBeat.Core;

internal static class Program
{
    private static readonly List<string> Results = new();
    private static int _failed;
    [STAThread]
    private static int Main(string[] args)
    {
        if (!args.Contains("--demo") || !args.Contains("--settings-dir"))
        { Console.Error.WriteLine("Use --demo --settings-dir <workspace-test-directory>"); return 2; }
        var directory = Path.GetFullPath(args[Array.IndexOf(args, "--settings-dir") + 1]);
        Directory.CreateDirectory(directory);
        var app = new App(); app.InitializeComponent();
        app.Startup += async (_, _) =>
        {
            // Let OnStartup finish before observing the actual application and dispatcher.
            await Task.Delay(1600);
            try
            {
                var overlay = app.Windows.OfType<OverlayWindow>().Single();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!int.TryParse(overlay.DisplayedBpm, out _) && DateTime.UtcNow < deadline) await Task.Delay(50);
                Verify("Actual app receives simulated heart rate", app.IsDemo && int.TryParse(overlay.DisplayedBpm, out var bpm) && bpm > 0);
                var handle = new WindowInteropHelper(overlay).Handle;
                app.SetLocked(true);
                Verify("Locked overlay passes mouse input to underlying windows", (GetWindowLongPtr(handle, -20).ToInt64() & 0x20) != 0);
                Verify("Overlay has nonactivating window style", (GetWindowLongPtr(handle, -20).ToInt64() & 0x08000000) != 0);
                app.SetLocked(false);
                Verify("Unlock restores drag interaction", (GetWindowLongPtr(handle, -20).ToInt64() & 0x20) == 0 && !overlay.IsLocked);
                Verify("Unlocked corner starts native window resizing", HitTestCorner(handle) == 17);
                app.SetLocked(true);
                var lockedHit = HitTestCorner(handle);
                Verify("Locked window has no resize hit target", lockedHit < 10 || lockedHit > 17);
                app.SetLocked(false);
                overlay.Width = 420; overlay.Height = 280;
                SendMessage(handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
                var persistedBoundsPath = Path.Combine(directory, "settings.json");
                using (var savedBounds = JsonDocument.Parse(File.ReadAllText(persistedBoundsPath)))
                    Verify("Resize completion persists both dimensions", savedBounds.RootElement.TryGetProperty("Width", out var width) && width.GetDouble() == 420 && savedBounds.RootElement.TryGetProperty("Height", out var height) && height.GetDouble() == 280);
                app.ToggleVisibility(); Verify("Overlay hides without ending process", !overlay.IsVisible);
                var foreground = GetForegroundWindow();
                app.ToggleVisibility();
                Verify("Showing overlay does not steal focus", overlay.IsVisible && GetForegroundWindow() == foreground);
                overlay.Left = -100000; overlay.Top = -100000; app.ResetPosition();
                Verify("Reset recovers an offscreen window", overlay.Left > -10000 && overlay.Top > -10000);
                app.ShowSettings();
                var controls = app.Windows.OfType<ControlWindow>().Single();
                controls.UpdateLayout(); Capture(controls, Path.Combine(directory, "settings-preview.png"));
                controls.Close(); Verify("Closing settings keeps app available", !controls.IsVisible && overlay.IsVisible);
                await app.DisconnectAsync(); await Task.Delay(300);
                Verify("Manual disconnect clears displayed number", overlay.DisplayedBpm == "--");
                await app.StartDemoAsync(); await Task.Delay(1200);
                Verify("Restarting source restores live display", int.TryParse(overlay.DisplayedBpm, out _));

                // Deterministic fixture for visual inspection of the entire five-minute chart.
                var now = DateTimeOffset.UtcNow;
                overlay.Update(90, new HeartRateSample[] { new(now.AddSeconds(-2), 70), new(now.AddSeconds(-1), 90), new(now, 110) }, now, "模拟数据");
                Verify("Overlay shows correct average/minimum/maximum", (overlay.FindName("AverageLabel") as TextBlock)?.Text == "90" && (overlay.FindName("MinimumLabel") as TextBlock)?.Text == "70" && (overlay.FindName("MaximumLabel") as TextBlock)?.Text == "110");
                Verify("Fresh BPM animates the heart indicator", (overlay.FindName("PulseScale") as ScaleTransform)?.HasAnimatedProperties == true);
                app.SetHeartAnimation(false);
                Verify("Animation preference stops motion without hiding BPM", (overlay.FindName("PulseScale") as ScaleTransform)?.HasAnimatedProperties == false && int.TryParse(overlay.DisplayedBpm, out _));
                app.SetHeartAnimation(true);
                overlay.Update(null, Array.Empty<HeartRateSample>(), now, "等待数据");
                Verify("Unavailable BPM stops the heart animation", (overlay.FindName("PulseScale") as ScaleTransform)?.HasAnimatedProperties == false);
                var points = Enumerable.Range(0, 300).Select(i => new HeartRateSample(now.AddSeconds(i - 299),
                    i is > 125 and < 145 ? null : (int)(84 + 12 * Math.Sin(i / 27d) + 4 * Math.Sin(i / 6d)))).ToArray();
                overlay.SetAppearance(false, 0.8, true); overlay.Update(85, points, now, "模拟数据"); overlay.UpdateLayout();
                Capture(overlay, Path.Combine(directory, "overlay-preview.png"));
                overlay.Width = 280; overlay.Height = 216; overlay.UpdateLayout();
                Capture(overlay, Path.Combine(directory, "overlay-compact-preview.png"));
                Verify("Compact layout retains readable statistics", (overlay.FindName("AverageLabel") as TextBlock)?.ActualHeight > 0 && (overlay.FindName("Chart") as HeartRateChart)?.ActualHeight >= 40);
                overlay.Width = 420; overlay.Height = 280;
                Verify("Preview includes a rendered chart", new FileInfo(Path.Combine(directory, "overlay-preview.png")).Length > 1000);
                app.SetOpacity(0.65); app.SetLocked(true);
                var saved = new SettingsStore(directory).Load();
                Verify("Settings persist lock and opacity without saving demo device", saved.Locked && Math.Abs(saved.BackgroundOpacity - 0.65) < 0.001 && saved.Device is null);
                VerifySettingsRecovery(Path.Combine(directory, "recovery"));

                if (args.Contains("--probe-bluetooth"))
                {
                    try
                    {
                        var devices = await new BleHeartRateTransport().ScanAsync(CancellationToken.None);
                        var report = JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(Path.Combine(directory, "bluetooth-scan.json"), report);
                        Results.Add($"INFO Bluetooth scan returned {devices.Count} heart-rate candidate(s).");
                    }
                    catch (Exception ex) { Results.Add("INFO Bluetooth hardware probe: " + ex.Message); }
                }
            }
            catch (Exception ex) { _failed++; Results.Add("FAIL Unhandled smoke test error: " + ex); }
            finally
            {
                try { await app.ExitAsync(); }
                catch (Exception ex) { _failed++; Results.Add("FAIL Exit: " + ex); app.Shutdown(1); }
                File.WriteAllLines(Path.Combine(directory, "smoke-test.txt"), Results);
                foreach (var result in Results) Console.WriteLine(result);
            }
        };
        app.Run();
        return _failed == 0 ? 0 : 1;
    }
    private static void VerifySettingsRecovery(string directory)
    {
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "settings.json"), "{bad-json");
        var store = new SettingsStore(directory); var recovered = store.Load();
        Verify("Corrupt settings recover without crashing", recovered.BackgroundOpacity == 0.8 && store.LastError is not null);
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"BackgroundOpacity\":8}");
        Verify("Out-of-range persisted opacity is clamped", store.Load().BackgroundOpacity == 0.95);
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"Width\":1,\"Height\":99999}");
        var bounds = store.Load();
        Verify("Invalid saved dimensions are limited to usable sizes", bounds.Width == 280 && bounds.Height == 720);
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"Locked\":true,\"BackgroundOpacity\":0.65}");
        var legacy = store.Load();
        Verify("Version 1 settings migrate to a readable default size", legacy.Width == 320 && legacy.Height == 240 && legacy.Locked && legacy.BackgroundOpacity == 0.65 && legacy.AnimateHeart);
    }
    private static void Verify(string name, bool condition) { Results.Add((condition ? "PASS " : "FAIL ") + name); if (!condition) _failed++; }
    private static void Capture(Window window, string path)
    {
        var target = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 2), (int)Math.Ceiling(window.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        target.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path); encoder.Save(file);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    private static int HitTestCorner(IntPtr handle)
    {
        GetWindowRect(handle, out var rect);
        var coordinates = (long)(ushort)(rect.Right - 5) | ((long)(ushort)(rect.Bottom - 5) << 16);
        return SendMessage(handle, 0x0084, IntPtr.Zero, new IntPtr(coordinates)).ToInt32();
    }
}
