namespace IBSaveConverter.Services;

public static class SaveLocations
{
    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>Where Finder, iTunes and the Apple Devices app keep local iOS backups, or null if none exist.</summary>
    public static string? IosBackupRoot => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Apple Computer", "MobileSync", "Backup"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apple", "MobileSync", "Backup"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "MobileSync", "Backup"),
    }.FirstOrDefault(Directory.Exists);
}
