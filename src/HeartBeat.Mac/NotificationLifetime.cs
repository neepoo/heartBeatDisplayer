using HeartBeat.Core;

namespace HeartBeat.Mac;

internal sealed class NotificationLifetime : IDisposable
{
    private readonly object _gate = new();
    private bool _started, _lost, _disposed;
    public event Action<HeartRateMeasurement>? MeasurementReceived;
    public event Action? Disconnected;
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_lost) throw new IOException("心率设备已断开连接，请重新连接。");
            _started = true;
        }
    }
    public void Deliver(HeartRateMeasurement value)
    {
        lock (_gate)
            if (_started && !_lost && !_disposed) InvokeSafely(() => MeasurementReceived?.Invoke(value));
    }
    public void LoseConnection()
    {
        lock (_gate)
        {
            if (_disposed || _lost) return;
            _lost = true;
            if (_started) InvokeSafely(() => Disconnected?.Invoke());
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            MeasurementReceived = null;
            Disconnected = null;
        }
    }
    // Native callbacks must never propagate subscriber exceptions into AppKit's run loop.
    private static void InvokeSafely(Action callback)
    {
        try { callback(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("BLE callback: {0}", error); }
    }
}
