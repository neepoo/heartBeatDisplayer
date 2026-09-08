using System.Text.Json;
using HeartBeat.Core;
using HeartBeat.Desktop;

internal static class SettingsStoreChecks
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(directory);
        var original = new UserSettings {
            Device = new HeartRateDevice("BLE:forerunner-255", "Garmin 手表"),
            Left = -240.5, Top = 72.25, Width = 420, Height = 280,
            AnimateHeart = false, BackgroundOpacity = .65, Locked = true
        };
        store.Save(original);
        Check("Settings serialize without reflection", store.LastError is null && File.Exists(file));
        var restored = store.Load();
        Check("Saved settings roundtrip every preference and real device", restored.Device == original.Device &&
            restored.Left == -240.5 && restored.Top == 72.25 && restored.Width == 420 && restored.Height == 280 &&
            !restored.AnimateHeart && restored.BackgroundOpacity == .65 && restored.Locked);
        using (var json = JsonDocument.Parse(File.ReadAllText(file))) {
            var root = json.RootElement;
            Check("Settings preserve legacy JSON names and value types", root.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(
                ["Device", "Left", "Top", "Width", "Height", "AnimateHeart", "BackgroundOpacity", "Locked"]) &&
                root.GetProperty("Device").GetProperty("Id").GetString() == "BLE:forerunner-255" &&
                root.GetProperty("Device").GetProperty("Name").GetString() == "Garmin 手表" &&
                root.GetProperty("BackgroundOpacity").ValueKind == JsonValueKind.Number &&
                root.GetProperty("Locked").ValueKind == JsonValueKind.True && File.ReadAllText(file).Contains('\n'));
        }
        File.WriteAllText(file, """{"Device":{"Id":"legacy-watch","Name":"Forerunner 255"},"Left":null,"Top":null,"Locked":true,"BackgroundOpacity":0.7,"UnknownFutureSetting":17}""");
        var legacy = store.Load();
        Check("Old settings with nested device retain defaults without reflection", store.LastError is null &&
            legacy.Device == new HeartRateDevice("legacy-watch", "Forerunner 255") && legacy.Left is null && legacy.Top is null &&
            legacy.Width == 320 && legacy.Height == 240 && legacy.AnimateHeart && legacy.Locked && legacy.BackgroundOpacity == .7);
    }
    private static void Check(string name, bool passed)
    {
        if (!passed) throw new InvalidOperationException("FAIL " + name);
        Console.WriteLine("PASS " + name);
    }
}
