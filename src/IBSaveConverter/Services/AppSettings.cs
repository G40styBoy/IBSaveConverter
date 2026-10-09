using System.Text.Json;
using System.Text.Json.Serialization;

namespace IBSaveConverter.Services;

/// <summary>Settings saved as settings.json next to the app, or in %AppData%\IBSaveConverter if that folder isn't writable.</summary>
public sealed class AppSettings
{
    private const string FILE_NAME = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The Infinity Blade III Cloud folder.</summary>
    public string? LastPcFolder { get; set; }
    public string? LastIb2PcFolder { get; set; }
    public string? LastZipFolder { get; set; }
    // The iOS settings keep their old "Iphone" names so settings.json files from earlier versions still load.
    [JsonPropertyName("LastIphoneBackup")] public string? LastIosBackup { get; set; }
    [JsonPropertyName("LastIphoneSaveFolder")] public string? LastIosSaveFolder { get; set; }
    /// <summary>The last iOS device key found or typed. Prefills the device key everywhere it's asked for.</summary>
    [JsonPropertyName("IphoneDeviceId")] public string? IosDeviceId { get; set; }
    public string? LastIosOutputFolder { get; set; }

    public static string AppFolderPath => Path.Combine(AppContext.BaseDirectory, FILE_NAME);
    public static string AppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IBSaveConverter", FILE_NAME);

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
                // Unreadable: try the next location, then use defaults.
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
                // Not writable (for example under Program Files): try the next location.
            }
        }
    }
}
