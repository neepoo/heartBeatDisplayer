namespace HeartBeat.Core;

public sealed class HeartRateController : IAsyncDisposable
{
    private readonly IHeartRateTransport _transport;
    private readonly TimeProvider _clock;
    private readonly Func<int, TimeSpan> _retryDelay;
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task _run = Task.CompletedTask;
    private long _version;
    private bool _disposed;

    public HeartRateController(IHeartRateTransport transport, TimeProvider? clock = null, Func<int, TimeSpan>? retryDelay = null)
    {
        _transport = transport;
        _clock = clock ?? TimeProvider.System;
        _retryDelay = retryDelay ?? (attempt => TimeSpan.FromSeconds(Math.Min(15, Math.Pow(2, Math.Min(attempt + 1, 4)))));
    }
    public event Action<HeartRateSample>? SampleReceived;
    public event Action<ConnectionStatus>? StatusChanged;
    public long Version => Interlocked.Read(ref _version);

    public void Start(HeartRateDevice device)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cancellation?.Cancel();
            var version = ++_version;
            var previous = _run;
            var cancellation = _cancellation = new();
            // Chain sessions instead of abandoning WinRT operations on cancellation.
            // A new device cannot connect until the old operation has released its result.
            _run = Task.Run(async () =>
            {
                try
                {
                    await previous.ConfigureAwait(false);
                    await RunAsync(device, version, cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                finally
                {
                    lock (_sync) { if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null; }
                    cancellation.Dispose();
                }
            });
        }
    }

    public async Task StopAsync()
    {
        Task running;
        lock (_sync)
        {
            ++_version;
            _cancellation?.Cancel();
            running = _run;
            StatusChanged?.Invoke(new(ConnectionState.Idle, "已断开"));
        }
        await running.ConfigureAwait(false);
    }

    private async Task RunAsync(HeartRateDevice device, long version, CancellationToken token)
    {
        var attempt = 0;
        while (!token.IsCancellationRequested)
        {
            Publish(new(attempt == 0 ? ConnectionState.Connecting : ConnectionState.Reconnecting, "正在连接 " + device.Name), version, token);
            IHeartRateConnection? connection = null;
            Action<HeartRateMeasurement>? onMeasurement = null;
            Action? onDisconnected = null;
            var accepting = false;
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reason = "连接中断";
            try
            {
                connection = await _transport.ConnectAsync(device, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (_sync) accepting = true;
                onMeasurement = measurement =>
                {
                    lock (_sync)
                    {
                        if (!accepting || version != _version || token.IsCancellationRequested) return;
                        SampleReceived?.Invoke(new(_clock.GetUtcNow(), measurement.Bpm));
                    }
                };
                onDisconnected = () =>
                {
                    lock (_sync) accepting = false;
                    disconnected.TrySetResult();
                };
                connection.MeasurementReceived += onMeasurement;
                connection.Disconnected += onDisconnected;
                await connection.StartAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!disconnected.Task.IsCompleted)
                    Publish(new(ConnectionState.Connected, "已连接 · " + device.Name), version, token);
                attempt = 0;
                await disconnected.Task.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) { reason = ex.Message; }
            finally
            {
                lock (_sync) accepting = false;
                if (connection is not null)
                {
                    if (onMeasurement is not null) connection.MeasurementReceived -= onMeasurement;
                    if (onDisconnected is not null) connection.Disconnected -= onDisconnected;
                    try { await connection.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Connection cleanup: " + ex.Message); }
                }
            }
            var delay = _retryDelay(attempt++);
            Publish(new(ConnectionState.Reconnecting, $"{reason}，{delay.TotalSeconds:0.#} 秒后重试"), version, token);
            await Task.Delay(delay, _clock, token).ConfigureAwait(false);
        }
    }

    private void Publish(ConnectionStatus status, long version, CancellationToken token)
    {
        lock (_sync)
            if (version == _version && !token.IsCancellationRequested) StatusChanged?.Invoke(status);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        await StopAsync().ConfigureAwait(false);
    }
}
