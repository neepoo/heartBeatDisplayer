using Avalonia;
using Avalonia.Headless;
using HeartBeat.Desktop;
using HeartBeat.Core;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool corruptSettings = args.Contains("--corrupt-settings");
        var directory = Path.GetFullPath(args.FirstOrDefault(arg => arg != "--corrupt-settings") ?? "artifacts/headless-session");
        bool? startupWarningVisible = null;
        if (corruptSettings)
        {
            directory = Path.Combine(directory, "corrupt-startup");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{bad-json");
        }
        var smokeArgs = new[] { "--demo", "--smoke-test", "--settings-dir", directory };
        int exitCode = DesktopBootstrap.CreateBuilder(smokeArgs,
            () => throw new Exception("Demo initialized real Bluetooth"), () => new HeadlessIntegration(() => {
                if (!corruptSettings) return;
                // Attach queues this before the normal smoke suite. It executes after
                // full application startup, including ResetPosition's successful save.
                var app = (DesktopApplication)Application.Current!;
                bool recoveredFile;
                try { using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "settings.json"))); recoveredFile = true; }
                catch (JsonException) { recoveredFile = false; }
                startupWarningVisible = recoveredFile && app.Controls?.IsVisible == true &&
                    app.Controls.GetLogicalDescendants().OfType<TextBlock>().Any(text =>
                        text.Text?.StartsWith("设置无法读取，已恢复默认值：", StringComparison.Ordinal) == true);
            }))
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .StartWithClassicDesktopLifetime(smokeArgs, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        if (corruptSettings)
        {
            string result = (startupWarningVisible == true ? "PASS " : "FAIL ") + "Actual startup shows corrupt-settings warning after saving recovered defaults";
            Console.WriteLine(result);
            File.WriteAllText(Path.Combine(directory, "startup-warning-test.txt"), result + Environment.NewLine);
            if (startupWarningVisible != true) return 1;
        }
        return exitCode;
    }
    private sealed class HeadlessIntegration(Action checkStartup) : IDesktopIntegration
    {
        public string? Warning => null;
        public bool SupportsClickThrough => false;
        public event Action? ToggleVisibility { add { } remove { } }
        public event Action? ToggleLock { add { } remove { } }
        public void Attach(Avalonia.Controls.Window window) => Dispatcher.UIThread.Post(checkStartup);
        public void Apply(bool locked) { }
        public void Dispose() { }
    }
}
