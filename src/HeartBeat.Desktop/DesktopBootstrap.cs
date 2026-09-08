using Avalonia;
using Avalonia.Controls;
using HeartBeat.Core;

namespace HeartBeat.Desktop;

public sealed record DesktopOptions(string SettingsDirectory, bool Demo, bool SmokeTest)
{
    public static DesktopOptions Parse(string[] args)
    {
        string? directory = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--settings-dir")
            {
                if (++i >= args.Length || args[i].StartsWith("--") || string.IsNullOrWhiteSpace(args[i]))
                    throw new ArgumentException("--settings-dir requires a directory.");
                directory = Path.GetFullPath(args[i]);
            }
            else if (args[i] is not ("--demo" or "--smoke-test"))
                throw new ArgumentException("Unknown argument: " + args[i]);
        }
        var smoke = args.Contains("--smoke-test");
        var demo = args.Contains("--demo");
        if (smoke && (!demo || directory is null))
            throw new ArgumentException("--smoke-test requires --demo --settings-dir <isolated-directory>.");
        return new(directory ?? DefaultSettingsDirectory(), demo, smoke);
    }

    public static string DefaultSettingsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeartBeatDisplayer");
        if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Application Support", "HeartBeatDisplayer");
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return Path.Combine(!string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine(home, ".config"), "HeartBeatDisplayer");
    }
}

public static class DesktopBootstrap
{
    public static AppBuilder CreateBuilder(string[] args, Func<IHeartRateTransport> transportFactory, Func<IDesktopIntegration> desktopFactory)
    {
        var options = DesktopOptions.Parse(args);
        return AppBuilder.Configure(() => new DesktopApplication(options, transportFactory, desktopFactory));
    }

    public static int Run(string[] args, Func<IHeartRateTransport> transportFactory, Func<IDesktopIntegration> desktopFactory)
    {
        try
        {
            return CreateBuilder(args, transportFactory, desktopFactory).UsePlatformDetect().LogToTrace()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

// A per-settings-directory lock also permits isolated smoke sessions beside a running app.
public sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;
    private SingleInstance(FileStream file) => _lock = file;
    public static SingleInstance? TryAcquire(string directory)
    {
        Directory.CreateDirectory(directory);
        try { return new(new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
        catch (IOException) { return null; }
    }
    public void Dispose() => _lock.Dispose();
}
