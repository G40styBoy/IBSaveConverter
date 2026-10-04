using System.Text.Json;

namespace IBSaveConverter.Services;

/// <summary>
/// Settings remembered between runs, saved as settings.json next to the app.
/// Falls back to %AppData%\IBSaveConverter when the app folder can't be written to.
/// </summary>
public sealed class AppSettings
{
    private const string FILE_NAME = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string? LastPcFolder { get; set; }
    public string? LastZipFolder { get; set; }

    public static string AppFolderPath => Path.Combine(AppContext.BaseDirectory, FILE_NAME);
    public static string AppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IBSaveConverter", FILE_NAME);

    /// <summary>Where the settings were last loaded from or saved to.</summary>
    public static string? LastPath { get; private set; }

    public static AppSettings Load()
    {
        foreach (string path in new[] { AppFolderPath, AppDataPath })
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                if (settings is null)
                    continue;
                LastPath = path;
                return settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Unreadable settings: try the next location, then fall back to defaults.
            }
        }
        return new AppSettings();
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(this, JsonOptions);
        foreach (string path in new[] { AppFolderPath, AppDataPath })
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
                LastPath = path;
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not writable (for example the app sits in Program Files): try the next location.
            }
        }
    }
}
