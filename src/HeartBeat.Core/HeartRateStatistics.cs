namespace HeartBeat.Core;

public sealed record HeartRateStatistics(int? Average, int? Minimum, int? Maximum, int Count)
{
    public static HeartRateStatistics Calculate(IEnumerable<HeartRateSample> samples, DateTimeOffset now)
    {
        var values = samples.Where(s => s.Timestamp > now - HeartRateHistory.Window && s.Timestamp <= now && s.Bpm is > 0)
            .Select(s => s.Bpm!.Value).ToArray();
        return values.Length == 0 ? new(null, null, null, 0) : new(
            (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero), values.Min(), values.Max(), values.Length);
    }
}
