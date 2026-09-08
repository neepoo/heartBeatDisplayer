using System.Text.Json.Serialization;
using HeartBeat.Core;

namespace HeartBeat.Desktop;

// Keep the existing JSON contract and generate constructor metadata for the BLE
// device record: trimmed macOS builds do not retain its reflection parameter names.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UserSettings))]
[JsonSerializable(typeof(HeartRateDevice))]
internal partial class SettingsJsonContext : JsonSerializerContext;
