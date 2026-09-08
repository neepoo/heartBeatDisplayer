using HeartBeat.App.Bluetooth;
using HeartBeat.Desktop;

namespace HeartBeat.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) => DesktopBootstrap.Run(args, () => new BleHeartRateTransport(), () => new WindowsDesktopIntegration());
}
