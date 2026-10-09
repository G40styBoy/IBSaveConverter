using IBSaveEditor.Package.Conversion;

namespace IBSaveConverter.Services;

/// <summary>One character as shown in the lists. <see cref="Slot"/> is -1 for a single save file.</summary>
public sealed record CharacterInfo(int Slot, SaveSlotData Data, string? SourceFile = null)
{
    public string Name => Data.Info.DisplayName;
    public int Level => Data.Info.PawnLevel;
    public string Map => Data.Info.CurrentMap;
    public bool IsDeleted => Data.Info.IsDeleted;
}

/// <summary>
/// The characters read from a PC folder, an Android zip or an iOS SAVE folder. <see cref="DeviceId"/> is the device ID
/// a mobile save is encrypted with; null for PC and for iOS saves on the fixed key.
/// </summary>
public sealed record LoadedSave(ICharacterSource Source, IReadOnlyList<CharacterInfo> Characters, string? DeviceId = null)
{
    public int CurrentSlot => Source.CurrentSlot;
}

/// <summary>The possible save keys from an iOS backup, and the save they unlocked if a SAVE folder was available.</summary>
public sealed record IosBackupResult(LoadedSave? Save, IReadOnlyList<string> DeviceIds, string DeviceName);

/// <summary>Copying into the save folder failed partway, so it may hold a mix of old and new files.</summary>
public sealed class InstallCopyException(string? backupFolder, Exception inner)
    : IOException(backupFolder is null
        ? $"Copying into the save folder failed partway: {inner.Message}"
        : $"Copying into the save folder failed partway: {inner.Message} Your saves from before are in \"{backupFolder}\".", inner)
{
    public string? BackupFolder { get; } = backupFolder;
}

public sealed record InstallResult(IReadOnlyList<ConversionResult> Characters, string Folder, string? BackupFolder);

/// <summary>
/// Workflows shared by both games (installing into a save folder with a backup first) and the IB3-only iOS ones.
/// Each game's PC and Android workflows are in <see cref="GameSaves"/>. Everything here is synchronous; the view
/// models run it off the UI thread.
/// </summary>
public static class ConversionService
{
    /// <summary>True for a device ID that can be used as a save key: 32 characters once the dashes are removed.</summary>
    public static bool IsValidDeviceId(string? deviceId)
    {
        try
        {
            MobileTemplates.KeyFromDeviceId(deviceId ?? string.Empty);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>True for an IB3 folder with a save in it: a PC Cloud folder or an iOS SAVE folder.</summary>
    public static bool IsSaveFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, CloudFiles.HeaderCache));

    /// <summary>
    /// An iOS SAVE folder. The fixed key is tried first, then <paramref name="deviceIds"/>.
    /// Throws <see cref="SaveKeyNotFoundException"/> for a legacy save whose device key isn't among them.
    /// </summary>
    public static LoadedSave ReadIosSaveFolder(string folder, IEnumerable<string> deviceIds)
    {
        var save = MobileSave.LoadFolder(folder, deviceIds);
        return Describe(save, save.DeviceId);
    }

    /// <summary>
    /// Reads the possible save keys out of an encrypted iOS backup and tries them on <paramref name="saveFolder"/>,
    /// or on the saves inside the backup when no folder is given.
    /// </summary>
    public static IosBackupResult ReadIosBackup(string backupFolder, string password, string? saveFolder)
    {
        string temp = Path.Combine(Path.GetTempPath(), "IBSaveConverter-ios-" + Guid.NewGuid().ToString("N"));
        try
        {
            var backup = IosBackupReader.Extract(backupFolder, password, temp);
            string? folder = saveFolder ?? backup.SaveFolder;
            var save = folder is null ? null : ReadIosSaveFolder(folder, backup.DeviceIds);
            return new IosBackupResult(save, backup.DeviceIds, backup.DeviceName);
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }

    /// <summary>
    /// Characters into an iOS SAVE folder, backed up first. <paramref name="keepExisting"/> keeps the folder's characters
    /// (read with the fixed key or one of <paramref name="knownKeys"/>); otherwise the folder ends up with only these.
    /// </summary>
    /// <param name="deviceId">The device ID to encrypt with, or null for the fixed key.</param>
    public static InstallResult ToIos(IReadOnlyList<SaveSlotData> characters, string saveFolder, bool keepExisting,
                                      IEnumerable<string> knownKeys, string? deviceId)
    {
        var keepFrom = keepExisting ? MobileSave.LoadFolder(saveFolder, knownKeys) : null;
        return Install(saveFolder, GameSaves.Ib3IsCharacterFile, removeOldCharacters: !keepExisting, temp =>
            SaveConverter.CharactersToIos(characters, keepFrom, temp, deviceId));
    }

    /// <summary>
    /// 1. Convert into a temp folder and verify. 2. Back up the save folder beside itself.
    /// 3. Copy the converted files over it, deleting old character files (by <paramref name="isCharacterFile"/>)
    /// if <paramref name="removeOldCharacters"/> is set.
    /// </summary>
    internal static InstallResult Install(string folder, Func<string, bool> isCharacterFile, bool removeOldCharacters,
                                          Func<string, IReadOnlyList<ConversionResult>> convert)
    {
        string temp = Path.Combine(Path.GetTempPath(), "IBSaveConverter-" + Guid.NewGuid().ToString("N"));
        try
        {
            var results = convert(temp);

            string? backup = null;
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
                backup = BackUpFolder(folder);

            try
            {
                Directory.CreateDirectory(folder);
                var newFiles = Directory.GetFiles(temp).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.GetFiles(temp))
                    File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);

                if (removeOldCharacters)
                    foreach (string file in Directory.GetFiles(folder))
                        if (isCharacterFile(Path.GetFileName(file)) && !newFiles.Contains(Path.GetFileName(file)))
                            File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InstallCopyException(backup, ex);
            }

            return new InstallResult(results, folder, backup);
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }

    /// <summary>Copies a folder to a timestamped sibling, such as "Cloud backup 2026-10-04 125012".</summary>
    private static string BackUpFolder(string folder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string parent = Path.GetDirectoryName(full) ?? throw new InvalidOperationException("Cannot back up a drive root.");
        string backup = Path.Combine(parent, $"{Path.GetFileName(full)} backup {DateTime.Now:yyyy-MM-dd HHmmss}");
        for (int n = 2; Directory.Exists(backup); n++)
            backup = Path.Combine(parent, $"{Path.GetFileName(full)} backup {DateTime.Now:yyyy-MM-dd HHmmss} ({n})");

        CopyDirectory(full, backup);
        return backup;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void DeleteQuietly(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static LoadedSave Describe(ICharacterSource source, string? deviceId = null) =>
        new(source, source.Slots.Select(slot => new CharacterInfo(slot, source.GetSlot(slot))).ToList(), deviceId);
}
