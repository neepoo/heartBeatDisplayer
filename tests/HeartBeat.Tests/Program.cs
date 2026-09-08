using HeartBeat.Core;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action body) => tests.Add((name, () => { body(); return Task.CompletedTask; }));

Test("8-bit heart rate is decoded", () => {
    Check.True(HeartRateParser.TryParse([0, 72], out var value)); Check.Equal<int?>(72, value.Bpm);
});
Test("16-bit heart rate uses little endian", () => {
    Check.True(HeartRateParser.TryParse([1, 44, 1], out var value)); Check.Equal<int?>(300, value.Bpm);
});
Test("Truncated flag-dependent payloads are rejected", () => {
    foreach (var packet in new byte[][] { [], [0], [1, 90], [8, 80], [16, 80, 1], [16, 80], [24, 80, 0, 0] })
        Check.True(!HeartRateParser.TryParse(packet, out _), Convert.ToHexString(packet));
});
Test("Optional energy and RR fields preserve BPM", () => {
    Check.True(HeartRateParser.TryParse([24, 81, 7, 0, 0, 4, 5, 4], out var value)); Check.Equal<int?>(81, value.Bpm);
});
Test("Unsupported contact flag is ignored, absent contact is unavailable", () => {
    Check.True(HeartRateParser.TryParse([2, 70], out var unsupported)); Check.Equal<int?>(70, unsupported.Bpm);
    Check.True(HeartRateParser.TryParse([4, 70], out var absent)); Check.Equal<int?>(null, absent.Bpm);
    Check.True(HeartRateParser.TryParse([6, 70], out var present)); Check.Equal<int?>(70, present.Bpm);
});
Test("Zero BPM means unavailable", () => {
    Check.True(HeartRateParser.TryParse([0, 0], out var value)); Check.Equal<int?>(null, value.Bpm);
});
var start = DateTimeOffset.Parse("2026-09-08T10:00:00Z");
Test("Current number uses newest sample, same second replaces curve point", () => {
    var h = new HeartRateHistory(); h.Add(new(start, 70)); h.Add(new(start.AddMilliseconds(800), 78));
    Check.Equal<int?>(78, h.CurrentBpm(start.AddSeconds(1)));
    Check.Equal(1, h.Snapshot(start.AddSeconds(1)).Count);
    Check.Equal<int?>(78, h.Snapshot(start.AddSeconds(1))[0].Bpm);
});
Test("Five-second stale deadline clears current number", () => {
    var h = new HeartRateHistory(); h.Add(new(start, 70));
    Check.Equal<int?>(70, h.CurrentBpm(start.AddSeconds(4)));
    Check.Equal<int?>(null, h.CurrentBpm(start.AddSeconds(5)));
});
Test("Unavailable measurements immediately clear number and preserve curve gap", () => {
    var h = new HeartRateHistory(); h.Add(new(start, 70)); h.Add(new(start.AddSeconds(1), null));
    Check.Equal<int?>(null, h.CurrentBpm(start.AddSeconds(1)));
    Check.Equal<int?>(null, h.Snapshot(start.AddSeconds(1))[1].Bpm);
});
Test("History evicts expired points even when no new data arrives", () => {
    var h = new HeartRateHistory(); h.Add(new(start, 70)); h.Add(new(start.AddSeconds(1), 80));
    var points = h.Snapshot(start.AddSeconds(300)); Check.Equal(1, points.Count); Check.Equal<int?>(80, points[0].Bpm);
    Check.Equal(0, h.Snapshot(start.AddSeconds(302)).Count);
});
Test("History doesn't fabricate points across pauses", () => {
    var h = new HeartRateHistory(); h.Add(new(start, 70)); h.Add(new(start.AddSeconds(15), 80));
    var points = h.Snapshot(start.AddSeconds(15)); Check.Equal(2, points.Count);
    Check.Equal(start.AddSeconds(15), points[1].Timestamp);
});
Test("Device switch clears prior session and out-of-order samples cannot replace latest", () => {
    var h = new HeartRateHistory(); h.Add(new(start.AddSeconds(2), 90)); h.Add(new(start, 70));
    Check.Equal<int?>(90, h.CurrentBpm(start.AddSeconds(3)));
    h.Clear(); Check.Equal(0, h.Snapshot(start.AddSeconds(3)).Count); Check.Equal<int?>(null, h.CurrentBpm(start.AddSeconds(3)));
});

Test("Statistics use only valid samples within the current five-minute window", () => {
    var now = start.AddMinutes(5);
    var stats = HeartRateStatistics.Calculate([
        new(start, 250), new(start.AddSeconds(1), 70), new(start.AddSeconds(2), null),
        new(start.AddSeconds(3), 90), new(now, 110), new(now.AddSeconds(1), 200), new(now, 0)], now);
    Check.Equal<int?>(90, stats.Average); Check.Equal<int?>(70, stats.Minimum); Check.Equal<int?>(110, stats.Maximum); Check.Equal(3, stats.Count);
});
Test("Statistics round half BPM upward and show unavailable for empty windows", () => {
    var stats = HeartRateStatistics.Calculate([new(start, 70), new(start.AddSeconds(1), 71)], start.AddSeconds(1));
    Check.Equal<int?>(71, stats.Average);
    var empty = HeartRateStatistics.Calculate([new(start, 90), new(start.AddMinutes(6), null)], start.AddMinutes(6));
    Check.Equal<int?>(null, empty.Average); Check.Equal<int?>(null, empty.Minimum); Check.Equal<int?>(null, empty.Maximum);
});
tests.AddRange(ControllerTests.All());
var failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {test.Name}: {e.Message}"); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;

static class Check
{
    public static void True(bool value, string message = "Expected true") { if (!value) throw new Exception(message); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
}
