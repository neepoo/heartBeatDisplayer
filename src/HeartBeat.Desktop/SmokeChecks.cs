using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using HeartBeat.Core;

namespace HeartBeat.Desktop;

// Explicit opt-in diagnostic mode used against the same application binaries that are packaged.
public static class SmokeChecks
{
    public static async Task RunAsync(DesktopApplication app, string directory)
    {
        var results = new List<string>(); var failed = 0;
        void Verify(string name, bool condition) { results.Add((condition ? "PASS " : "FAIL ") + name); if (!condition) failed++; }
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "smoke-test.txt"), "SMOKE RUNNING\n");
            var overlay = app.Overlay;
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!int.TryParse(overlay.DisplayedBpm, out _) && DateTime.UtcNow < deadline) await Task.Delay(100);
            Verify("Actual app receives simulated heart rate", app.IsDemo && int.TryParse(overlay.DisplayedBpm, out var bpm) && bpm > 0);
            Verify("Overlay is visible and topmost", overlay.IsVisible && overlay.Topmost);
            Verify("Overlay never requests activation or taskbar entry", !overlay.ShowActivated && !overlay.ShowInTaskbar);
            app.SetLocked(true); Verify("Lock disables resize interaction", overlay.IsLocked && !overlay.CanResize);
            await Task.Delay(200);
            foreach (var check in app.CheckNativeState(true)) Verify(check.Name, check.Passed);
            app.SetLocked(false); Verify("Unlock restores drag and resize", !overlay.IsLocked && overlay.CanResize);
            await Task.Delay(200);
            foreach (var check in app.CheckNativeState(false)) Verify(check.Name, check.Passed);
            overlay.Width = 420; overlay.Height = 280; await Task.Delay(350);
            var bounds = new SettingsStore(directory).Load();
            Verify("Resize persists both dimensions", bounds.Width == 420 && bounds.Height == 280);
            app.ToggleVisibility(); Verify("Overlay hides without ending process", !overlay.IsVisible);
            Verify("Hidden overlay stops heart animation", !overlay.IsHeartAnimating);
            app.ToggleVisibility(); Verify("Overlay can be shown again", overlay.IsVisible);
            overlay.Position = new(-100000, -100000); app.ResetPosition();
            Verify("Reset recovers an offscreen window", overlay.Position.X > -10000 && overlay.Position.Y > -10000);
            app.ShowSettings(); await Task.Delay(200);
            Verify("Settings remain reachable", app.Controls!.IsVisible);
            Capture(app.Controls!, Path.Combine(directory, "settings-preview.png"));
            app.Controls!.Close();
            Verify("Closing settings preserves application", overlay.IsVisible && (OperatingSystem.IsLinux() || !app.Controls.IsVisible));
            await app.DisconnectAsync(); await Task.Delay(300);
            Verify("Disconnect clears displayed number", overlay.DisplayedBpm == "--");
            await app.StartDemoAsync(); await Task.Delay(1200);
            Verify("Restarting demo restores live data", int.TryParse(overlay.DisplayedBpm, out _));
            var now = DateTimeOffset.UtcNow;
            overlay.Update(90, [new(now.AddSeconds(-2), 70), new(now.AddSeconds(-1), 90), new(now, 110)], now, "模拟数据");
            Verify("Five-minute statistics show average/minimum/maximum", overlay.AverageLabel.Text == "90" && overlay.MinimumLabel.Text == "70" && overlay.MaximumLabel.Text == "110");
            app.SetHeartAnimation(true); Verify("Fresh BPM animates heart", overlay.IsHeartAnimating);
            app.SetHeartAnimation(false); Verify("Animation preference stops motion but preserves BPM", !overlay.IsHeartAnimating && int.TryParse(overlay.DisplayedBpm, out _));
            app.SetHeartAnimation(true); overlay.Update(null, [], now, "等待数据");
            Verify("Unavailable BPM stops animation", !overlay.IsHeartAnimating && overlay.DisplayedBpm == "--");
            Verify("Unavailable statistics use placeholders", overlay.AverageLabel.Text == "--" && overlay.MinimumLabel.Text == "--" && overlay.MaximumLabel.Text == "--");
            var points = Enumerable.Range(0, 300).Select(i => new HeartRateSample(now.AddSeconds(i - 299), i is > 125 and < 145 ? null : (int)(84 + 12 * Math.Sin(i / 27d) + 4 * Math.Sin(i / 6d)))).ToArray();
            var largeReady = await ResizeForCaptureAsync(overlay, 420, 280);
            Verify("Large preview uses actual 420 x 280 window bounds", largeReady);
            if (!largeReady) throw new TimeoutException($"Large preview resize did not converge: {overlay.Bounds.Size}");
            overlay.SetAppearance(false, .8, true); overlay.Update(85, points, now, "模拟数据");
            Capture(overlay, Path.Combine(directory, "overlay-preview.png"));
            var compactReady = await ResizeForCaptureAsync(overlay, 280, 216);
            Verify("Compact preview uses actual 280 x 216 window bounds", compactReady);
            if (!compactReady) throw new TimeoutException($"Compact preview resize did not converge: {overlay.Bounds.Size}");
            overlay.Update(85, points, now, "模拟数据");
            Capture(overlay, Path.Combine(directory, "overlay-compact-preview.png"));
            Verify("Compact layout retains readable statistics and chart", overlay.AverageLabel.Bounds.Height > 0 && overlay.Chart.Bounds.Height >= 40);
            Verify("Preview contains rendered chart", new FileInfo(Path.Combine(directory, "overlay-preview.png")).Length > 1000);
            app.SetOpacity(.65); app.SetLocked(true);
            var saved = new SettingsStore(directory).Load();
            Verify("Preferences persist without saving demo device", saved.Locked && Math.Abs(saved.BackgroundOpacity - .65) < .001 && saved.Device is null);
            var recovery = Path.Combine(directory, "recovery"); Directory.CreateDirectory(recovery);
            var file = Path.Combine(recovery, "settings.json"); File.WriteAllText(file, "{bad-json");
            var store = new SettingsStore(recovery); var defaults = store.Load();
            Verify("Corrupt settings recover with warning", defaults.BackgroundOpacity == .8 && store.LastError is not null);
            File.WriteAllText(file, "{\"BackgroundOpacity\":8,\"Width\":1,\"Height\":99999}"); var clamped = store.Load();
            Verify("Invalid settings clamp to usable bounds", clamped.BackgroundOpacity == .95 && clamped.Width == 280 && clamped.Height == 720);
            File.WriteAllText(file, "{\"Locked\":true,\"BackgroundOpacity\":0.65}"); var legacy = store.Load();
            Verify("Legacy settings retain readable defaults", legacy.Width == 320 && legacy.Height == 240 && legacy.Locked && legacy.AnimateHeart);
            File.WriteAllText(file, "{\"Device\":{\"Id\":\"demo\",\"Name\":\"demo\"}}");
            Verify("Persisted demo device is ignored", store.Load().Device is null);
            var lockDir = Path.Combine(directory, "lock-check");
            using (var instance = SingleInstance.TryAcquire(lockDir)) {
                using var second = SingleInstance.TryAcquire(lockDir); Verify("Same settings directory prevents duplicate instance", instance is not null && second is null);
                using var isolated = SingleInstance.TryAcquire(Path.Combine(lockDir, "isolated")); Verify("Independent settings directories allow parallel sessions", isolated is not null);
            }
            using (var reopened = SingleInstance.TryAcquire(lockDir)) Verify("Instance lock is released on exit", reopened is not null);
            var rejected = false; try { DesktopOptions.Parse(["--smoke-test"]); } catch (ArgumentException) { rejected = true; }
            Verify("Smoke requires demo and explicit settings isolation", rejected);
            rejected = false; try { DesktopOptions.Parse(["--demo", "--settings-dir"]); } catch (ArgumentException) { rejected = true; }
            Verify("Missing settings-directory value is rejected", rejected);
        }
        catch (Exception ex) { failed++; results.Add("FAIL Unhandled smoke error: " + ex); }
        finally
        {
            try { app.SaveSettings(); }
            catch (Exception ex) { failed++; results.Add("FAIL Saving settings: " + ex); }
            try { await app.ExitAsync(failed == 0 ? 0 : 1); }
            catch (Exception ex) { failed++; results.Add("FAIL Exit: " + ex); }
            // The marker is written only after all assertions and controller cleanup passed.
            results.Add(failed == 0 ? "SMOKE COMPLETE" : $"SMOKE FAILED: {failed}");
            File.WriteAllLines(Path.Combine(directory, "smoke-test.txt"), results);
            foreach (var result in results) Console.WriteLine(result);
        }
    }
    private static async Task<bool> ResizeForCaptureAsync(Window window, double width, double height)
    {
        window.Width = width; window.Height = height;
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            // Native ConfigureNotify / resize callbacks run asynchronously. Layout alone
            // cannot turn a requested size into an observed platform window size.
            await Task.Delay(50);
            window.UpdateLayout();
            if (Math.Abs(window.Bounds.Width - width) < .01 && Math.Abs(window.Bounds.Height - height) < .01) return true;
        } while (timeout.Elapsed < TimeSpan.FromSeconds(2));
        return false;
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width * 2), (int)Math.Ceiling(window.Bounds.Height * 2)), new Vector(192, 192));
        bitmap.Render(window); bitmap.Save(path, new PngBitmapEncoderOptions());
    }
}
