using HeartBeat.Desktop;
using HeartBeat.Linux;

return DesktopBootstrap.Run(args, () => new BlueZHeartRateTransport(), () => new X11DesktopIntegration());
