using AppKit;
using HeartBeat.Desktop;

namespace HeartBeat.Mac;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        NSApplication.Init();
        return DesktopBootstrap.Run(args, () => new CoreBluetoothTransport(), () => new MacDesktopIntegration());
    }
}
