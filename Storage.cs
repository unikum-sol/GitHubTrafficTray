using System.Text.Json;

namespace GitHubTrafficTray;

internal static class Storage
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static TrafficStore LoadStore(out string? warning)
    {
        var store = LoadFile<TrafficStore>(AppConfig.DataFile, out warning) ?? new TrafficStore();
        store.Normalize();
        return store;
    }

    public static AppSettings LoadSettings(out string? warning)
    {
        var s = LoadFile<AppSettings>(AppConfig.SettingsFile, out warning) ?? new AppSettings();
        if (s.FetchHour < 1 || s.FetchHour > 23) s.FetchHour = AppConfig.DefaultFetchHour;
        return s;
    }

    public static void SaveStore(TrafficStore store) => WriteAtomic(AppConfig.DataFile, store);

    public static void SaveSettings(AppSettings settings) => WriteAtomic(AppConfig.SettingsFile, settings);

    private static T? LoadFile<T>(string path, out string? warning) where T : class
    {
        warning = null;
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            // Defekte Datei nicht überschreiben, sondern beiseitelegen
            string backup = path + ".defekt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Move(path, backup); } catch { backup = "(Umbenennen fehlgeschlagen)"; }
            warning = $"Die Datei\n{path}\nkonnte nicht gelesen werden ({ex.Message}).\n\n" +
                      $"Sie wurde gesichert als:\n{backup}\n\nEs wird mit einer leeren Datei weitergearbeitet.";
            return null;
        }
    }

    private static void WriteAtomic<T>(string path, T obj)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(obj, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
