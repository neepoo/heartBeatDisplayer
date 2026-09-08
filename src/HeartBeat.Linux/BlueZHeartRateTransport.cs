using HeartBeat.Core;
using Tmds.DBus.Protocol;

namespace HeartBeat.Linux;

public sealed class BlueZHeartRateTransport : IHeartRateTransport
{
    internal const string Adapter = "org.bluez.Adapter1", Device = "org.bluez.Device1", Service = "org.bluez.GattService1", Characteristic = "org.bluez.GattCharacteristic1";
    internal const string HeartRateUuid = "0000180d-0000-1000-8000-00805f9b34fb", MeasurementUuid = "00002a37-0000-1000-8000-00805f9b34fb";
    private readonly Func<IBlueZBus> _factory;
    public BlueZHeartRateTransport() : this(() => new BlueZBus()) { }
    internal BlueZHeartRateTransport(Func<IBlueZBus> factory) => _factory = factory;

    public async Task<IReadOnlyList<HeartRateDevice>> ScanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bus = _factory();
        string? adapter = null;
        bool started = false;
        try
        {
            await bus.ConnectAsync(cancellationToken);
            adapter = FindAdapter(await bus.ObjectsAsync(cancellationToken));
            await bus.CallAsync(adapter, Adapter, "SetDiscoveryFilter", cancellationToken, new() {
                ["Transport"] = "le", ["UUIDs"] = VariantValue.Array(new[] { HeartRateUuid }), ["DuplicateData"] = true
            });
            await bus.CallAsync(adapter, Adapter, "StartDiscovery", cancellationToken);
            started = true;
            var found = new Dictionary<string, HeartRateDevice>();
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(400, cancellationToken);
                foreach (var item in await bus.ObjectsAsync(cancellationToken))
                    if (item.Path.StartsWith(adapter + "/", StringComparison.Ordinal) && item.Interfaces.TryGetValue(Device, out var props) &&
                        Strings(props, "UUIDs").Contains(HeartRateUuid, StringComparer.OrdinalIgnoreCase))
                        found[item.Path] = new(EncodeId(item.Path), Text(props, "Alias") ?? Text(props, "Name") ?? "心率设备");
            }
            return found.Values.OrderBy(d => d.Name).ToArray();
        }
        catch (Exception ex) { throw Explain(ex, cancellationToken); }
        finally
        {
            if (started && adapter is not null) await BestEffort(bus, adapter, Adapter, "StopDiscovery");
        }
    }

    public async Task<IHeartRateConnection> ConnectAsync(HeartRateDevice device, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = DecodeId(device.Id);
        var bus = _factory();
        bool ownsConnection = false;
        try
        {
            await bus.ConnectAsync(cancellationToken);
            var initial = await bus.ObjectsAsync(cancellationToken);
            FindAdapter(initial);
            var target = initial.FirstOrDefault(o => o.Path == path && o.Interfaces.ContainsKey(Device));
            if (target is null) throw new IOException("已保存的蓝牙设备不在 BlueZ 缓存中，请开启手表心率广播并重新扫描。");
            ownsConnection = !Bool(target.Interfaces[Device], "Connected");
            if (ownsConnection) await bus.CallAsync(path, Device, "Connect", cancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            while (true)
            {
                var objects = await bus.ObjectsAsync(deadline.Token);
                var current = objects.FirstOrDefault(o => o.Path == path);
                if (current is null || !current.Interfaces.TryGetValue(Device, out var props) || !Bool(props, "Connected"))
                    throw new IOException("设备已断开，请保持手表靠近电脑并开启心率广播。");
                if (Bool(props, "ServicesResolved"))
                {
                    string? characteristic = FindMeasurement(objects, path);
                    if (characteristic is null) throw new IOException("设备未提供支持 Notify 的标准心率特征 0x2A37，请开启 BLE 心率广播。");
                    return new BlueZHeartRateConnection(bus, path, characteristic, ownsConnection);
                }
                await Task.Delay(250, deadline.Token);
            }
        }
        catch (Exception ex)
        {
            if (ownsConnection) await BestEffort(bus, path, Device, "Disconnect");
            bus.Dispose();
            throw Explain(ex, cancellationToken);
        }
    }

    internal static string FindAdapter(IReadOnlyList<BlueZObject> objects)
    {
        var adapters = objects.Where(o => o.Interfaces.ContainsKey(Adapter)).ToArray();
        if (adapters.Length == 0) throw new IOException("未检测到蓝牙适配器。请连接支持 BLE 的适配器并检查 Linux 驱动。");
        return adapters.FirstOrDefault(o => Bool(o.Interfaces[Adapter], "Powered"))?.Path
            ?? throw new IOException("蓝牙已关闭或被 rfkill 禁用，请在系统蓝牙设置中开启蓝牙并关闭飞行模式。");
    }
    internal static string? FindMeasurement(IReadOnlyList<BlueZObject> objects, string device)
    {
        var services = objects.Where(o => o.Interfaces.TryGetValue(Service, out var p) &&
            Text(p, "UUID")?.Equals(HeartRateUuid, StringComparison.OrdinalIgnoreCase) == true && Path(p, "Device") == device).Select(o => o.Path).ToHashSet();
        return objects.FirstOrDefault(o => o.Interfaces.TryGetValue(Characteristic, out var p) &&
            Text(p, "UUID")?.Equals(MeasurementUuid, StringComparison.OrdinalIgnoreCase) == true &&
            services.Contains(Path(p, "Service") ?? "") && Strings(p, "Flags").Contains("notify"))?.Path;
    }
    internal static bool Bool(Dictionary<string, VariantValue> p, string key) => p.TryGetValue(key, out var v) && v.GetBool();
    internal static string? Text(Dictionary<string, VariantValue> p, string key) => p.TryGetValue(key, out var v) ? v.GetString() : null;
    internal static string? Path(Dictionary<string, VariantValue> p, string key) => p.TryGetValue(key, out var v) ? v.GetObjectPathAsString() : null;
    internal static string[] Strings(Dictionary<string, VariantValue> p, string key) => p.TryGetValue(key, out var v) ? v.GetArray<string>() : [];
    internal static string EncodeId(string path) => "bluez:" + path;
    internal static string DecodeId(string id)
    {
        if (!id.StartsWith("bluez:/org/bluez/", StringComparison.Ordinal) || id.Length > 256 ||
            id[6..].Split('/').Skip(1).Any(s => s.Length == 0 || s.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
            throw new ArgumentException("Linux 设备标识无效，请重新扫描。");
        return id[6..];
    }
    internal static Exception Explain(Exception ex, CancellationToken token)
    {
        if (token.IsCancellationRequested) return new OperationCanceledException(token);
        if (ex is OperationCanceledException or TimeoutException) return new IOException("BlueZ 操作超时，请确认手表靠近电脑、心率广播已开启，然后重试。", ex);
        string error = ex.Message;
        if (ex is UnauthorizedAccessException || error.Contains("AccessDenied") || error.Contains("NotAuthorized") || error.Contains("NotPermitted"))
            return new IOException("BlueZ 拒绝访问。请使用已登录的桌面用户运行，并检查系统 D-Bus / BlueZ 权限策略；无需以 root 运行应用。", ex);
        if (error.Contains("NotReady")) return new IOException("蓝牙适配器尚未就绪，请在系统设置中开启蓝牙并检查 rfkill / 飞行模式。", ex);
        if (error.Contains("ServiceUnknown") || error.Contains("NameHasNoOwner") || ex is System.Net.Sockets.SocketException || error.Contains("system_bus_socket") || ex.GetType().Name == "DBusConnectFailedException")
            return new IOException("无法访问 BlueZ 系统服务。请安装并启用 bluez / bluetooth 服务，确认系统 D-Bus 正常运行。", ex);
        return ex;
    }
    internal static async Task BestEffort(IBlueZBus bus, string path, string iface, string method)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await bus.CallAsync(path, iface, method, timeout.Token); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"BlueZ {method}: {ex.Message}"); }
    }
}
