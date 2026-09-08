namespace HeartBeat.Core;

public sealed class SimulatedHeartRateTransport : IHeartRateTransport
{
    public static readonly HeartRateDevice Device = new("demo", "演示设备（模拟数据）");
    public Task<IReadOnlyList<HeartRateDevice>> ScanAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HeartRateDevice>>([Device]);
    public Task<IHeartRateConnection> ConnectAsync(HeartRateDevice device, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IHeartRateConnection>(new Connection());
    }
    private sealed class Connection : IHeartRateConnection
    {
        private CancellationTokenSource? _stop;
        private Task _loop = Task.CompletedTask;
        public event Action<HeartRateMeasurement>? MeasurementReceived;
        public event Action? Disconnected { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_stop is not null) throw new InvalidOperationException("Already started");
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _stop.Token;
            _loop = Task.Run(async () => {
                var t = 0;
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var bpm = (int)Math.Round(78 + 9 * Math.Sin(t / 9.0) + 3 * Math.Sin(t / 2.0));
                        MeasurementReceived?.Invoke(new(bpm)); t++;
                        await Task.Delay(1000, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            });
            return Task.CompletedTask;
        }
        public async ValueTask DisposeAsync()
        {
            _stop?.Cancel(); await _loop.ConfigureAwait(false); _stop?.Dispose(); _stop = null;
        }
    }
}
