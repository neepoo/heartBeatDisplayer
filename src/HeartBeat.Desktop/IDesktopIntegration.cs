using Avalonia.Controls;

namespace HeartBeat.Desktop;

public interface IDesktopIntegration : IDisposable
{
    string? Warning { get; }
    bool SupportsClickThrough { get; }
    void Attach(Window window);
    void Apply(bool locked);
    event Action? ToggleVisibility;
    event Action? ToggleLock;
}

// Optional native diagnostics are invoked only by the explicit isolated smoke mode.
public interface IDesktopSmokeChecks
{
    IReadOnlyList<(string Name, bool Passed)> CheckNativeState(bool locked);
}
