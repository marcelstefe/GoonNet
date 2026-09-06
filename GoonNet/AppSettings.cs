using System;
using System.IO;
using System.Text.Json;

namespace GoonNet;

public class AppSettings
{
    public bool Notifications { get; set; } = true;
    public string DateFormat { get; set; } = "D Month Y";
    public string TimeFormat { get; set; } = "24-Hour";
    public string Units { get; set; } = "Metric";
    public string DataRefreshRate { get; set; } = "1s";
}

public static class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GoonNet");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = new();

    public static event Action<AppSettings>? SettingsChanged;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) Current = loaded;
            }
        }
        catch
        {
            Current = new AppSettings();
        }
        return Current;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
            Current = settings;
            SettingsChanged?.Invoke(Current);
        }
        catch
        {
            // ignore write errors
        }
    }
}
