namespace IBSaveConverter.Services;

/// <summary>Default places to look for and write saves.</summary>
public static class SaveLocations
{
    /// <summary>Documents\My Games\Infinity Blade III\SwordGame\Cloud</summary>
    public static string DefaultPcCloudFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games", "Infinity Blade III", "SwordGame", "Cloud");

    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>The default file name for a PC -> Android zip.</summary>
    public const string SuggestedZipName = "IB3 Android Save Folder.zip";
}
