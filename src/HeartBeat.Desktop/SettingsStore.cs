using System;
using System.IO;
using System.Text.Json;
using HeartBeat.Core;

namespace HeartBeat.Desktop;

public sealed class UserSettings
{
    public HeartRateDevice? Device { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = OverlayLayout.DefaultWidth;
    public double Height { get; set; } = OverlayLayout.DefaultHeight;
    public bool AnimateHeart { get; set; } = true;
    public double BackgroundOpacity { get; set; } = 0.8;
    public bool Locked { get; set; }
}

public sealed class SettingsStore(string directory)
{
    private readonly string _file = Path.Combine(directory, "settings.json");
    public string? LastError { get; private set; }
    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_file)) return new();
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_file)) ?? new();
            settings.BackgroundOpacity = double.IsFinite(settings.BackgroundOpacity) ? Math.Clamp(settings.BackgroundOpacity, 0.4, 0.95) : 0.8;
            settings.Width = double.IsFinite(settings.Width) ? Math.Clamp(settings.Width, OverlayLayout.MinimumWidth, OverlayLayout.MaximumWidth) : OverlayLayout.DefaultWidth;
            settings.Height = double.IsFinite(settings.Height) ? Math.Clamp(settings.Height, OverlayLayout.MinimumHeight, OverlayLayout.MaximumHeight) : OverlayLayout.DefaultHeight;
            if (settings.Left is double x && !double.IsFinite(x)) settings.Left = null;
            if (settings.Top is double y && !double.IsFinite(y)) settings.Top = null;
            if (string.IsNullOrWhiteSpace(settings.Device?.Id) || settings.Device.Id == "demo") settings.Device = null;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { LastError = "设置无法读取，已恢复默认值：" + ex.Message; return new(); }
    }
    public void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _file, true);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { LastError = "设置未能保存：" + ex.Message; }
    }
}
