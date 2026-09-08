using HeartBeat.Core;

namespace HeartBeat.Linux;

internal sealed class BlueZHeartRateConnection(IBlueZBus bus, string device, string characteristic, bool ownsConnection) : IHeartRateConnection
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private IDisposable? _deviceWatch, _valueWatch;
    private bool _started, _disposed, _lost, _notifying;
    public event Action<HeartRateMeasurement>? MeasurementReceived;
    public event Action? Disconnected;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_started) throw new InvalidOperationException("此心率连接已经启动。");
                _started = true;
            }
            _deviceWatch = await bus.WatchAsync(device, BlueZHeartRateTransport.Device, DeviceChanged, cancellationToken);
            _valueWatch = await bus.WatchAsync(characteristic, BlueZHeartRateTransport.Characteristic, ValueChanged, cancellationToken);
            // Snapshot after registration closes the Connect -> Start disconnect race.
            var objects = await bus.ObjectsAsync(cancellationToken);
            if (!objects.Any(o => o.Path == device && o.Interfaces.TryGetValue(BlueZHeartRateTransport.Device, out var p) && BlueZHeartRateTransport.Bool(p, "Connected")))
                LoseConnection();
            lock (_gate) if (_lost) throw new IOException("启用心率通知前设备已断开，请重新连接。");
            // Accept the first Value signal even when it precedes StartNotify's reply.
            await bus.CallAsync(characteristic, BlueZHeartRateTransport.Characteristic, "StartNotify", cancellationToken);
            _notifying = true;
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) if (_lost) throw new IOException("启用通知时设备已断开，请重试。");
        }
        catch (Exception ex) { throw BlueZHeartRateTransport.Explain(ex, cancellationToken); }
        finally { _lifecycle.Release(); }
    }

    private void DeviceChanged(Exception? error, BlueZChange? change)
    {
        if (error is not null || change is null || change.Invalidated.Contains("Connected") ||
            (change.Values.ContainsKey("Connected") && !BlueZHeartRateTransport.Bool(change.Values, "Connected"))) LoseConnection();
    }
    private void ValueChanged(Exception? error, BlueZChange? change)
    {
        if (error is not null) { LoseConnection(); return; }
        try
        {
            lock (_gate)
            {
                if (_disposed || _lost || !_started || change is null || !change.Values.TryGetValue("Value", out var value)) return;
                if (HeartRateParser.TryParse(value.GetArray<byte>(), out var measurement))
                    foreach (Action<HeartRateMeasurement> handler in MeasurementReceived?.GetInvocationList() ?? [])
                        try { handler(measurement); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("忽略无效 BlueZ 通知: " + ex.Message); }
    }
    private void LoseConnection()
    {
        lock (_gate)
        {
            if (_disposed || _lost) return;
            _lost = true;
            foreach (Action handler in Disconnected?.GetInvocationList() ?? [])
                try { handler(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            lock (_gate) { if (_disposed) return; _disposed = true; MeasurementReceived = null; Disconnected = null; }
            _valueWatch?.Dispose(); _deviceWatch?.Dispose();
            if (_notifying) await BlueZHeartRateTransport.BestEffort(bus, characteristic, BlueZHeartRateTransport.Characteristic, "StopNotify");
            if (ownsConnection) await BlueZHeartRateTransport.BestEffort(bus, device, BlueZHeartRateTransport.Device, "Disconnect");
            bus.Dispose();
        }
        finally { _lifecycle.Release(); }
    }
}
