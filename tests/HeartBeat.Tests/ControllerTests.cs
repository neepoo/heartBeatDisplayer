using HeartBeat.Core;
using System.Collections.Concurrent;

static class ControllerTests
{
    private static readonly HeartRateDevice Watch = new("watch", "255");
    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("Controller forwards live samples and rejects callbacks after stop", async () => {
            var transport = new FakeTransport();
            await using var controller = New(transport);
            var samples = new ConcurrentQueue<HeartRateSample>(); controller.SampleReceived += samples.Enqueue;
            controller.Start(Watch);
            await Until(() => transport.Connections.TryPeek(out var c) && c.Started);
            var connection = transport.Connections.Single(); var late = connection.CaptureCallback();
            connection.Emit(81); await Until(() => samples.Count == 1);
            Check.Equal<int?>(81, samples.Single().Bpm);
            await controller.StopAsync(); late(new(120));
            await Task.Delay(40); Check.Equal(1, samples.Count); Check.Equal(1, transport.Connections.Count);
            Check.True(connection.Disposed);
        });
        yield return ("Disconnect reconnects once and ignores old connection events", async () => {
            var transport = new FakeTransport(); await using var controller = New(transport);
            var samples = new ConcurrentQueue<HeartRateSample>(); controller.SampleReceived += samples.Enqueue;
            controller.Start(Watch); await Until(() => transport.Connections.TryPeek(out var c) && c.Started);
            var first = transport.Connections.Single(); var late = first.CaptureCallback(); first.Drop();
            await Until(() => transport.Connections.Count == 2 && transport.Connections.Last().Started);
            late(new(199)); transport.Connections.Last().Emit(75); await Until(() => samples.Count > 0);
            Check.Equal(1, samples.Count); Check.Equal<int?>(75, samples.Single().Bpm); Check.True(first.Disposed);
        });
        yield return ("Switch waits for old uncancellable connect and disposes its result", async () => {
            var transport = new FakeTransport { BlockFirst = true }; await using var controller = New(transport);
            var samples = new ConcurrentQueue<HeartRateSample>(); controller.SampleReceived += samples.Enqueue;
            controller.Start(Watch); await Until(() => transport.Attempts == 1);
            controller.Start(new("new", "New watch"));
            await Task.Delay(30); Check.Equal(1, transport.Attempts);
            transport.ReleaseFirst.TrySetResult();
            await Until(() => transport.Connections.Count == 2 && transport.Connections.Last().Started);
            Check.True(transport.Connections.First().Disposed);
            Check.True(!transport.Connections.First().Started);
            transport.Connections.Last().Emit(88); await Until(() => samples.Count == 1);
            Check.Equal<int?>(88, samples.Single().Bpm);
        });
        yield return ("Transient connection failure retries and recovers", async () => {
            var transport = new FakeTransport { Failures = 1 }; await using var controller = New(transport);
            controller.Start(Watch);
            await Until(() => transport.Connections.TryPeek(out var c) && c.Started);
            Check.Equal(2, transport.Attempts);
        });
        yield return ("Stop during retry prevents further connection attempts", async () => {
            var transport = new FakeTransport { Failures = 100 };
            await using var controller = new HeartRateController(transport, retryDelay: _ => TimeSpan.FromSeconds(10));
            controller.Start(Watch); await Until(() => transport.Attempts == 1);
            await controller.StopAsync(); await Task.Delay(40); Check.Equal(1, transport.Attempts);
        });
        yield return ("Shutdown waits for pending native connect and disposes returned resource", async () => {
            var transport = new FakeTransport { BlockFirst = true }; var controller = New(transport);
            controller.Start(Watch); await Until(() => transport.Attempts == 1);
            var shutdown = controller.DisposeAsync().AsTask();
            await Task.Delay(20); Check.True(!shutdown.IsCompleted);
            transport.ReleaseFirst.TrySetResult(); await shutdown;
            Check.True(transport.Connections.Single().Disposed);
            Check.True(!transport.Connections.Single().Started);
        });
        yield return ("Disconnect during notification startup never reports connected", async () => {
            var transport = new FakeTransport { DropFirstOnStart = true }; await using var controller = New(transport);
            var states = new ConcurrentQueue<ConnectionState>(); controller.StatusChanged += s => states.Enqueue(s.State);
            controller.Start(Watch);
            await Until(() => transport.Connections.Count == 2 && transport.Connections.Last().Started && states.Contains(ConnectionState.Connected));
            Check.Equal(1, states.Count(s => s == ConnectionState.Connected));
            Check.True(transport.Connections.First().Disposed);
        });
    }
    private static HeartRateController New(FakeTransport transport) => new(transport, retryDelay: _ => TimeSpan.FromMilliseconds(5));
    private static async Task Until(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!condition()) { if (DateTime.UtcNow > timeout) throw new Exception("Timed out waiting for observable behavior"); await Task.Delay(5); }
    }
    private sealed class FakeTransport : IHeartRateTransport
    {
        public ConcurrentQueue<FakeConnection> Connections { get; } = new();
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockFirst;
        public bool DropFirstOnStart;
        public int Failures;
        public int Attempts;
        public Task<IReadOnlyList<HeartRateDevice>> ScanAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HeartRateDevice>>([Watch]);
        public async Task<IHeartRateConnection> ConnectAsync(HeartRateDevice device, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref Attempts);
            if (BlockFirst && attempt == 1) await ReleaseFirst.Task;
            if (attempt <= Failures) throw new IOException("Temporary radio failure");
            var c = new FakeConnection { DropOnStart = DropFirstOnStart && attempt == 1 }; Connections.Enqueue(c); return c;
        }
    }
    private sealed class FakeConnection : IHeartRateConnection
    {
        public event Action<HeartRateMeasurement>? MeasurementReceived;
        public event Action? Disconnected;
        public volatile bool Started;
        public volatile bool Disposed;
        public bool DropOnStart;
        public void Emit(int bpm) => MeasurementReceived?.Invoke(new(bpm));
        public Action<HeartRateMeasurement> CaptureCallback() => MeasurementReceived ?? (_ => { });
        public void Drop() => Disconnected?.Invoke();
        public Task StartAsync(CancellationToken cancellationToken) { Started = true; if (DropOnStart) Drop(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
