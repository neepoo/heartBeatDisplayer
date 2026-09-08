using HeartBeat.Core;
using HeartBeat.Mac;

int passed = 0;
void Check(string name, Action test)
{
    test();
    Console.WriteLine($"PASS {name}");
    passed++;
}
static void Require(bool value) { if (!value) throw new Exception("Assertion failed"); }
Check("Notifications are deferred until Start", () =>
{
    using var lifetime = new NotificationLifetime();
    int received = 0;
    lifetime.MeasurementReceived += _ => received++;
    lifetime.Deliver(new(70));
    Require(received == 0);
    lifetime.Start();
    lifetime.Deliver(new(71));
    Require(received == 1);
});
Check("Disconnect before Start is latched", () =>
{
    using var lifetime = new NotificationLifetime();
    lifetime.LoseConnection();
    try { lifetime.Start(); throw new Exception("Missing disconnection failure"); }
    catch (IOException) { }
});
Check("Repeated disconnect and late notifications are suppressed", () =>
{
    using var lifetime = new NotificationLifetime();
    int lost = 0, received = 0;
    lifetime.Disconnected += () => lost++;
    lifetime.MeasurementReceived += _ => received++;
    lifetime.Start();
    lifetime.LoseConnection();
    lifetime.LoseConnection();
    lifetime.Deliver(new(72));
    Require(lost == 1 && received == 0);
});
Check("Dispose suppresses all late callbacks and Start", () =>
{
    var lifetime = new NotificationLifetime();
    int callbacks = 0;
    lifetime.Disconnected += () => callbacks++;
    lifetime.MeasurementReceived += _ => callbacks++;
    lifetime.Start();
    lifetime.Dispose();
    lifetime.Deliver(new(72));
    lifetime.LoseConnection();
    Require(callbacks == 0);
    try { lifetime.Start(); throw new Exception("Missing disposal failure"); }
    catch (ObjectDisposedException) { }
});
Console.WriteLine($"Mac lifetime checks: {passed} passed.");
