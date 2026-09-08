namespace HeartBeat.Core;

public sealed record HeartRateDevice(string Id, string Name);
public sealed record HeartRateMeasurement(int? Bpm);
public sealed record HeartRateSample(DateTimeOffset Timestamp, int? Bpm);
public enum ConnectionState { Idle, Connecting, Connected, Reconnecting, Error }
public sealed record ConnectionStatus(ConnectionState State, string Message);

public interface IHeartRateTransport
{
    Task<IReadOnlyList<HeartRateDevice>> ScanAsync(CancellationToken cancellationToken);
    Task<IHeartRateConnection> ConnectAsync(HeartRateDevice device, CancellationToken cancellationToken);
}

// ConnectAsync returns an inactive connection: consumers subscribe before StartAsync
// enables sensor notifications, so even the first measurement cannot be lost.
public interface IHeartRateConnection : IAsyncDisposable
{
    event Action<HeartRateMeasurement>? MeasurementReceived;
    event Action? Disconnected;
    Task StartAsync(CancellationToken cancellationToken);
}
