namespace HeartBeat.Core;

public sealed class HeartRateHistory
{
    private readonly List<HeartRateSample> _points = [];
    private HeartRateSample? _latest;
    public static readonly TimeSpan Freshness = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    // Owned by the UI thread; immutable snapshots can be handed to the renderer.
    public void Add(HeartRateSample sample)
    {
        if (_latest is not null && sample.Timestamp < _latest.Timestamp) return;
        _latest = sample;
        if (_points.Count > 0 && _points[^1].Timestamp.ToUnixTimeSeconds() == sample.Timestamp.ToUnixTimeSeconds())
            _points[^1] = sample;
        else _points.Add(sample);
        Prune(sample.Timestamp);
    }
    public void Clear() { _points.Clear(); _latest = null; }
    public int? CurrentBpm(DateTimeOffset now) => _latest is not null && now >= _latest.Timestamp && now - _latest.Timestamp < Freshness ? _latest.Bpm : null;
    public IReadOnlyList<HeartRateSample> Snapshot(DateTimeOffset now)
    {
        Prune(now);
        return _points.ToArray();
    }
    private void Prune(DateTimeOffset now) => _points.RemoveAll(p => p.Timestamp <= now - Window);
}
