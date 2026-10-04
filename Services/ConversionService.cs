using IBSaveEditor.Package.Conversion;

namespace IBSaveConverter.Services;

/// <summary>One character, as shown in the lists. <paramref name="SourceFile"/> is set for a single save file.</summary>
public sealed record SlotInfo(int Index, string CharacterName, int Level, string Map, bool IsDeleted, string? SourceFile = null)
{
    /// <summary>In-game slot number (slot index 0 is in-game slot 1).</summary>
    public int Number => Index + 1;
}

/// <summary>A mobile zip's characters plus the device ID it was encrypted with.</summary>
public sealed record MobileZipInfo(IReadOnlyList<SlotInfo> Slots);

/// <summary>
/// Copying into the Cloud folder failed partway, so it may hold a mix of old and new files.
/// The backup (if any) holds the saves from before the install.
/// </summary>
public sealed class InstallCopyException(string? backupFolder, Exception inner)
    : IOException(backupFolder is null
        ? $"Copying into the Cloud folder failed partway: {inner.Message}"
        : $"Copying into the Cloud folder failed partway: {inner.Message} Your saves from before are in \"{backupFolder}\".", inner)
{
    public string? BackupFolder { get; } = backupFolder;
}

/// <summary>What happened during an Android -> PC install.</summary>
public sealed record InstallResult(IReadOnlyList<ConversionResult> Characters, string CloudFolder, string? BackupFolder);

/// <summary>
/// The app's conversion workflows, on top of the save editor's backend (<see cref="SaveConverter"/>,
/// <see cref="PcCloudFolder"/>, <see cref="MobileSaveZip"/>). Everything here is synchronous; the
/// view models run it off the UI thread.
/// </summary>
public static class ConversionService
{
    /// <summary>True if the folder holds a PC save set (it has a LocalFileHeaderCache).</summary>
    public static bool IsPcCloudFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, CloudFiles.HeaderCache));

    public static IReadOnlyList<SlotInfo> ReadPcSlots(string folder)
    {
        var pc = PcCloudFolder.Load(folder);
        return pc.Slots.Select(i => ToSlotInfo(i, pc.GetSlot(i))).ToList();
    }

    public static int ReadPcCurrentSlot(string folder) => PcCloudFolder.Load(folder).CurrentSlot;

    public static MobileZipInfo ReadMobileZip(string zipPath)
    {
        var mobile = MobileSaveZip.Load(zipPath);
        var slots = mobile.Slots.Select(i => ToSlotInfo(i, mobile.GetSlot(i))).ToList();
        return new MobileZipInfo(slots);
    }

    /// <summary>
    /// Loads a single save file (unencrypted, PC encrypted or Android encrypted) as a character.
    /// Its slot summary is built from the save.
    /// </summary>
    public static (SlotInfo Info, SaveSlotData Character) LoadSingleSave(string path)
    {
        var (character, _) = SingleSaveFile.Load(path);
        var info = character.Info;
        return (new SlotInfo(-1, info.CharacterName, info.PawnLevel, info.CurrentMap, info.IsDeleted, path), character);
    }

    /// <summary>
    /// Ticked PC characters plus single save files -> one Android zip. With <paramref name="keepFromPhoneZip"/>
    /// its characters are kept and these fill its free slots. Uses the Android port's fixed device ID.
    /// </summary>
    public static IReadOnlyList<ConversionResult> ToAndroid(string? pcFolder, IReadOnlyList<int> pcSlots,
                                                            IReadOnlyList<SaveSlotData> saveFiles, string outputZip,
                                                            string? keepFromPhoneZip = null)
    {
        var characters = new List<SaveSlotData>();
        if (pcSlots.Count > 0)
        {
            var pc = PcCloudFolder.Load(pcFolder ?? throw new ArgumentNullException(nameof(pcFolder)));
            characters.AddRange(pcSlots.Select(pc.GetSlot));
        }
        characters.AddRange(saveFiles);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputZip));
        if (directory is not null)
            Directory.CreateDirectory(directory);

        return SaveConverter.CharactersToMobile(characters, outputZip, keepFromPhoneZip, deviceId: null);
    }

    /// <summary>
    /// PC characters -> one Android zip. With <paramref name="keepFromPhoneZip"/> (a zip exported from the phone)
    /// its characters are kept and the PC ones fill its free slots; without it the zip holds only the PC
    /// characters, from slot 1. The zip uses the Android port's fixed device ID, the same on every phone.
    /// </summary>
    public static IReadOnlyList<ConversionResult> PcToAndroid(string pcFolder, IReadOnlyList<int> slots, string outputZip,
                                                              string? keepFromPhoneZip = null)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputZip));
        if (directory is not null)
            Directory.CreateDirectory(directory);

        return SaveConverter.PcToMobile(pcFolder, outputZip, slots, keepFromPhoneZip, deviceId: null);
    }

    /// <summary>
    /// The mobile slots <paramref name="count"/> new characters will land in: the first slots with no live
    /// character. Mirrors <see cref="SaveConverter.FreeMobileSlots"/>, for previewing before converting.
    /// Returns fewer than <paramref name="count"/> slots when the save is too full.
    /// </summary>
    public static IReadOnlyList<int> PlanFreeSlots(IEnumerable<SlotInfo> phoneSlots, int count)
    {
        var occupied = phoneSlots.Where(s => !s.IsDeleted).Select(s => s.Index).ToHashSet();
        return Enumerable.Range(0, CloudFiles.MaxSlots).Where(slot => !occupied.Contains(slot)).Take(count).ToList();
    }

    /// <summary>A save folder holds at most this many characters, on PC and on Android.</summary>
    public static int MaxSlots => CloudFiles.MaxSlots;

    /// <summary>
    /// One character from an Android zip -> one PC slot. Other PC characters are kept.
    /// See <see cref="Install"/> for how the Cloud folder is updated.
    /// </summary>
    public static InstallResult AndroidToPc(string zipPath, int sourceSlot, string cloudFolder, int targetSlot) =>
        Install(cloudFolder, removeOldCharacters: false, (existing, temp) =>
            new[] { SaveConverter.MobileToPc(zipPath, existing, temp, sourceSlot, targetSlot) });

    /// <summary>A single save file's character -> one PC slot. Other PC characters are kept.</summary>
    public static InstallResult SaveFileToPc(SaveSlotData character, string cloudFolder, int targetSlot) =>
        Install(cloudFolder, removeOldCharacters: false, (existing, temp) =>
            new[] { SaveConverter.CharacterToPc(character, existing, temp, targetSlot) });

    /// <summary>
    /// The whole Android save -> PC. The PC ends up with exactly the zip's characters, in the same slots;
    /// the characters that were on the PC are removed (after the backup).
    /// </summary>
    public static InstallResult AndroidToPcWhole(string zipPath, string cloudFolder) =>
        Install(cloudFolder, removeOldCharacters: true, (existing, temp) =>
            SaveConverter.MobileToPcWhole(zipPath, existing, temp));

    /// <summary>
    /// Installs into the Cloud folder in place:
    /// 1. converts into a temporary folder and verifies it (the backend reloads and compares bytes),
    /// 2. copies the current Cloud folder to "Cloud backup yyyy-MM-dd HHmmss" next to it,
    /// 3. copies the converted files over the Cloud folder, and with <paramref name="removeOldCharacters"/>
    ///    deletes character files that are no longer part of the save.
    /// If the Cloud folder has no saves yet, a new save set is created.
    /// </summary>
    private static InstallResult Install(string cloudFolder, bool removeOldCharacters,
                                         Func<string?, string, IReadOnlyList<ConversionResult>> convert)
    {
        string temp = Path.Combine(Path.GetTempPath(), "IBSaveConverter-" + Guid.NewGuid().ToString("N"));
        try
        {
            string? existing = IsPcCloudFolder(cloudFolder) ? cloudFolder : null;
            var results = convert(existing, temp);

            string? backup = null;
            if (Directory.Exists(cloudFolder) && Directory.EnumerateFileSystemEntries(cloudFolder).Any())
                backup = BackUpFolder(cloudFolder);

            try
            {
                Directory.CreateDirectory(cloudFolder);
                var newFiles = Directory.GetFiles(temp).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.GetFiles(temp))
                    File.Copy(file, Path.Combine(cloudFolder, Path.GetFileName(file)), overwrite: true);

                if (removeOldCharacters)
                    foreach (string file in Directory.GetFiles(cloudFolder))
                        if (CloudFiles.IsCharacterFile(Path.GetFileName(file)) && !newFiles.Contains(Path.GetFileName(file)))
                            File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InstallCopyException(backup, ex);
            }

            return new InstallResult(results, cloudFolder, backup);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp cleanup only */ }
        }
    }

    /// <summary>Copies a folder to a timestamped sibling, for example "Cloud backup 2026-10-04 125012".</summary>
    public static string BackUpFolder(string folder)
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

    private static SlotInfo ToSlotInfo(int index, SaveSlotData data) =>
        new(index, data.Info.CharacterName, data.Info.PawnLevel, data.Info.CurrentMap, data.Info.IsDeleted);
}
