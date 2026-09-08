using Tmds.DBus.Protocol;

namespace HeartBeat.Linux;

internal record BlueZObject(string Path, Dictionary<string, Dictionary<string, VariantValue>> Interfaces);
internal record BlueZChange(string Interface, Dictionary<string, VariantValue> Values, string[] Invalidated);

internal interface IBlueZBus : IDisposable
{
    Task ConnectAsync(CancellationToken token);
    Task<IReadOnlyList<BlueZObject>> ObjectsAsync(CancellationToken token);
    Task CallAsync(string path, string iface, string method, CancellationToken token, Dictionary<string, VariantValue>? filter = null);
    Task<IDisposable> WatchAsync(string path, string iface, Action<Exception?, BlueZChange?> callback, CancellationToken token);
}

// One private system-bus connection per scan or sensor session. Closing it releases
// BlueZ discovery/notification ownership without changing other applications' state.
internal sealed class BlueZBus : IBlueZBus
{
    private readonly DBusConnection _connection = new(DBusAddress.System ?? "unix:path=/run/dbus/system_bus_socket");
    private NameOwnerWatcher? _ownerWatcher;
    private string _owner = "org.bluez";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    public async Task ConnectAsync(CancellationToken token)
    {
        await _connection.ConnectAsync().AsTask().WaitAsync(Timeout, token);
        _ownerWatcher = await _connection.WatchNameOwnerAsync("org.bluez").WaitAsync(Timeout, token);
        _owner = _ownerWatcher.GetCurrentOwner() ?? throw new IOException("无法访问 BlueZ 系统服务。请安装并启用 bluez / bluetooth 服务。");
    }
    public async Task<IReadOnlyList<BlueZObject>> ObjectsAsync(CancellationToken token)
        => await _connection.CallMethodAsync(Message("/", "org.freedesktop.DBus.ObjectManager", "GetManagedObjects"), ReadObjects).WaitAsync(Timeout, token);

    public async Task CallAsync(string path, string iface, string method, CancellationToken token, Dictionary<string, VariantValue>? filter = null)
        => await _connection.CallMethodAsync(Message(path, iface, method, filter)).WaitAsync(Timeout, token);

    private MessageBuffer Message(string path, string iface, string method, Dictionary<string, VariantValue>? filter = null)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: _owner, path: path, @interface: iface, member: method, signature: filter is null ? null : "a{sv}");
        if (filter is not null) writer.WriteDictionary(filter);
        return writer.CreateMessage();
    }

    internal static IReadOnlyList<BlueZObject> ReadObjects(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        var objects = new List<BlueZObject>();
        var outer = reader.ReadDictionaryStart();
        while (reader.HasNext(outer))
        {
            string path = reader.ReadObjectPathAsString();
            var interfaces = new Dictionary<string, Dictionary<string, VariantValue>>();
            var inner = reader.ReadDictionaryStart();
            while (reader.HasNext(inner))
                interfaces[reader.ReadString()] = reader.ReadDictionaryOfStringToVariantValue();
            objects.Add(new(path, interfaces));
        }
        return objects;
    }

    public async Task<IDisposable> WatchAsync(string path, string iface, Action<Exception?, BlueZChange?> callback, CancellationToken token)
    {
        var task = _connection.WatchPropertiesChangedAsync(_owner, path, iface,
            static (message, _) => {
                var reader = message.GetBodyReader();
                return new BlueZChange(reader.ReadString(), reader.ReadDictionaryOfStringToVariantValue(), reader.ReadArrayOfString());
            }, (Notification<BlueZChange> notification) => callback(notification.IsCompletion ? notification.Exception : null, notification.HasValue ? notification.Value : null),
            ObserverFlags.EmitOnConnectionClosed | ObserverFlags.EmitOnOwnerChanged | ObserverFlags.EmitOnReaderFailed, emitOnCapturedContext: false).AsTask();
        // Do not abandon a subscription that could materialize after cancellation.
        var subscription = await task.WaitAsync(Timeout);
        if (token.IsCancellationRequested) { subscription.Dispose(); token.ThrowIfCancellationRequested(); }
        return subscription;
    }
    public void Dispose() { _ownerWatcher?.Dispose(); _connection.Dispose(); }
}
