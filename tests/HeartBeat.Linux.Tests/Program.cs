using HeartBeat.Core;
using HeartBeat.Linux;
using Tmds.DBus.Protocol;

var tests = new (string, Func<Task>)[] {
    ("Opaque Linux ID roundtrip and invalid ID rejection", () => {
        Equal(FakeBus.DevicePath, BlueZHeartRateTransport.DecodeId(BlueZHeartRateTransport.EncodeId(FakeBus.DevicePath)));
        foreach (var id in new[] { "ble:1234:0", "bluez:/org/bluez/../bad", "bluez:/org/bluez//dev", "bluez:/org/bluez/dev;cmd" })
            Throws<ArgumentException>(() => BlueZHeartRateTransport.DecodeId(id));
        return Task.CompletedTask;
    }),
    ("Adapter powered and no-adapter diagnostics", () => {
        Throws<IOException>(() => BlueZHeartRateTransport.FindAdapter([]));
        var bus = new FakeBus();
        bus.Objects[0].Interfaces[BlueZHeartRateTransport.Adapter]["Powered"] = false;
        Throws<IOException>(() => BlueZHeartRateTransport.FindAdapter(bus.Objects));
        return Task.CompletedTask;
    }),
    ("GATT characteristic belongs to HR service and selected device", () => {
        var bus = new FakeBus();
        Equal(FakeBus.CharacteristicPath, BlueZHeartRateTransport.FindMeasurement(bus.Objects, FakeBus.DevicePath));
        Equal<string?>(null, BlueZHeartRateTransport.FindMeasurement(bus.Objects, FakeBus.DevicePath + "_other"));
        bus.Objects[3].Interfaces[BlueZHeartRateTransport.Characteristic]["Flags"] = VariantValue.Array(new[] { "read" });
        Equal<string?>(null, BlueZHeartRateTransport.FindMeasurement(bus.Objects, FakeBus.DevicePath));
        return Task.CompletedTask;
    }),
    ("Connect defers Notify; first synchronous notification preserved", async () => {
        var bus = new FakeBus();
        var transport = new BlueZHeartRateTransport(() => bus);
        await using var connection = await transport.ConnectAsync(new(BlueZHeartRateTransport.EncodeId(FakeBus.DevicePath), "watch"), default);
        Equal(false, bus.Calls.Contains("StartNotify"));
        int? bpm = null;
        connection.MeasurementReceived += m => bpm = m.Bpm;
        bus.OnStart = () => bus.EmitValue([0, 88]);
        await connection.StartAsync(default);
        Equal<int?>(88, bpm);
        await ThrowsAsync<InvalidOperationException>(() => connection.StartAsync(default));
    }),
    ("Disconnect before Start is observed; notification is never enabled", async () => {
        var bus = new FakeBus();
        await using var connection = new BlueZHeartRateConnection(bus, FakeBus.DevicePath, FakeBus.CharacteristicPath, false);
        bus.Objects[1].Interfaces[BlueZHeartRateTransport.Device]["Connected"] = false;
        int disconnected = 0;
        connection.Disconnected += () => disconnected++;
        await ThrowsAsync<IOException>(() => connection.StartAsync(default));
        Equal(1, disconnected);
        Equal(false, bus.Calls.Contains("StartNotify"));
    }),
    ("Disconnect exactly once and no samples after loss or disposal", async () => {
        var bus = new FakeBus();
        var connection = new BlueZHeartRateConnection(bus, FakeBus.DevicePath, FakeBus.CharacteristicPath, true);
        int count = 0, lost = 0;
        connection.MeasurementReceived += _ => count++;
        connection.Disconnected += () => lost++;
        await connection.StartAsync(default);
        bus.EmitValue([1, 44, 1]); // UInt16 300 bpm
        Equal(1, count);
        bus.EmitDisconnect(); bus.EmitDisconnect(); bus.EmitValue([0, 90]);
        Equal(1, lost); Equal(1, count);
        await connection.DisposeAsync(); await connection.DisposeAsync();
        Equal(2, bus.DisposedWatches); Equal(true, bus.Disposed);
        Equal(1, bus.Calls.Count(c => c == "StopNotify"));
        Equal(1, bus.Calls.Count(c => c == "Disconnect"));
        bus.EmitValue([0, 90]); Equal(1, count);
    }),
    ("Malformed payload and subscriber exception do not stop delivery", async () => {
        var bus = new FakeBus();
        await using var connection = new BlueZHeartRateConnection(bus, FakeBus.DevicePath, FakeBus.CharacteristicPath, false);
        int count = 0;
        connection.MeasurementReceived += _ => throw new Exception("subscriber failure");
        connection.MeasurementReceived += _ => count++;
        await connection.StartAsync(default);
        bus.EmitValue([1]); bus.EmitValue([0, 95]);
        Equal(1, count);
    }),
    ("Cancelled connection cleans up owned BlueZ connection", async () => {
        var bus = new FakeBus();
        bus.Objects[1].Interfaces[BlueZHeartRateTransport.Device]["Connected"] = false;
        using var cancel = new CancellationTokenSource();
        bus.OnConnect = () => cancel.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => new BlueZHeartRateTransport(() => bus).ConnectAsync(new(BlueZHeartRateTransport.EncodeId(FakeBus.DevicePath), "watch"), cancel.Token));
        Equal(true, bus.Disposed); Equal(true, bus.Calls.Contains("Disconnect"));
    }),
    ("Shared preexisting connection is not disconnected during cleanup", async () => {
        var bus = new FakeBus();
        var connection = await new BlueZHeartRateTransport(() => bus).ConnectAsync(new(BlueZHeartRateTransport.EncodeId(FakeBus.DevicePath), "watch"), default);
        await connection.DisposeAsync();
        Equal(false, bus.Calls.Contains("Disconnect"));
    }),
    ("Permission and missing BlueZ diagnostics remain actionable", () => {
        Equal(true, BlueZHeartRateTransport.Explain(new Exception("org.bluez.Error.NotAuthorized"), default).Message.Contains("权限"));
        Equal(true, BlueZHeartRateTransport.Explain(new Exception("org.freedesktop.DBus.Error.ServiceUnknown"), default).Message.Contains("安装"));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Equal(true, BlueZHeartRateTransport.Explain(new Exception("failed"), cancel.Token) is OperationCanceledException);
        return Task.CompletedTask;
    })
};
int failed = 0;
foreach (var (name, test) in tests)
    try { await test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
Console.WriteLine($"Linux protocol/lifecycle: {tests.Length - failed}/{tests.Length} passed (hardware-free)");
return failed == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

sealed class FakeBus : IBlueZBus
{
    public const string DevicePath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF", CharacteristicPath = DevicePath + "/service001/char001";
    public List<BlueZObject> Objects { get; } = [
        new("/org/bluez/hci0", new() { [BlueZHeartRateTransport.Adapter] = new() { ["Powered"] = true } }),
        new(DevicePath, new() { [BlueZHeartRateTransport.Device] = new() { ["Connected"] = true, ["ServicesResolved"] = true } }),
        new(DevicePath + "/service001", new() { [BlueZHeartRateTransport.Service] = new() { ["UUID"] = BlueZHeartRateTransport.HeartRateUuid, ["Device"] = new ObjectPath(DevicePath) } }),
        new(CharacteristicPath, new() { [BlueZHeartRateTransport.Characteristic] = new() { ["UUID"] = BlueZHeartRateTransport.MeasurementUuid, ["Service"] = new ObjectPath(DevicePath + "/service001"), ["Flags"] = VariantValue.Array(new[] { "notify" }) } })
    ];
    public List<string> Calls { get; } = [];
    public bool Disposed; public int DisposedWatches;
    public Action? OnStart, OnConnect;
    private readonly Dictionary<string, Action<Exception?, BlueZChange?>> _watches = [];
    public Task ConnectAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task<IReadOnlyList<BlueZObject>> ObjectsAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<BlueZObject>>(Objects); }
    public Task CallAsync(string path, string iface, string method, CancellationToken token, Dictionary<string, VariantValue>? filter = null)
    {
        token.ThrowIfCancellationRequested(); Calls.Add(method);
        if (method == "StartNotify") OnStart?.Invoke();
        if (method == "Connect") { Objects[1].Interfaces[BlueZHeartRateTransport.Device]["Connected"] = true; OnConnect?.Invoke(); }
        return Task.CompletedTask;
    }
    public Task<IDisposable> WatchAsync(string path, string iface, Action<Exception?, BlueZChange?> callback, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); _watches[iface] = callback;
        return Task.FromResult<IDisposable>(new Subscription(() => { DisposedWatches++; _watches.Remove(iface); }));
    }
    public void EmitValue(byte[] bytes) { if (_watches.TryGetValue(BlueZHeartRateTransport.Characteristic, out var cb)) cb(null, new(BlueZHeartRateTransport.Characteristic, new() { ["Value"] = VariantValue.Array(bytes) }, [])); }
    public void EmitDisconnect() { if (_watches.TryGetValue(BlueZHeartRateTransport.Device, out var cb)) cb(null, new(BlueZHeartRateTransport.Device, new() { ["Connected"] = false }, [])); }
    public void Dispose() => Disposed = true;
    private sealed class Subscription(Action action) : IDisposable { public void Dispose() => action(); }
}
