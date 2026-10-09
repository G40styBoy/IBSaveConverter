using System.Text;

namespace IBSaveEditor.Package.Conversion;

/// <summary>
/// The PC port's Cloud folder. Every file listed in LocalFileHeaderCache is checked against its SHA-1
/// on load, held decrypted, and encrypted again on save.
/// </summary>
public sealed class PcCloudFolder : ISaveSet
{
    private const string DEFAULT_CLOUD_STORAGE_INI = "[CloudStorageEmulation]\r\nUpgradeKey=1\r\n\r\n";

    private readonly FileHeaderCache _cache;
    private readonly Dictionary<string, byte[]> _payloads = new();
    private readonly byte[] _cloudStorageIni;

    public string? SourcePath { get; }

    private PcCloudFolder(FileHeaderCache cache, byte[] cloudStorageIni, string? sourcePath)
    {
        _cache = cache;
        _cloudStorageIni = cloudStorageIni;
        SourcePath = sourcePath;
    }

    public IReadOnlyList<int> Slots =>
        _payloads.Keys.Select(CloudFiles.SlotOfSave).OfType<int>().Order().ToList();

    public IReadOnlyDictionary<string, byte[]> Files => _payloads;

    public int CurrentSlot =>
        _payloads.TryGetValue(CloudFiles.CurrentSlot, out var value) && value.Length >= 4 ? BitConverter.ToInt32(value) : 0;

    public static PcCloudFolder Load(string folder)
    {
        string cachePath = Path.Combine(folder, CloudFiles.HeaderCache);
        if (!File.Exists(cachePath))
            throw new FileNotFoundException("No LocalFileHeaderCache in this folder. Is it the SwordGame\\Cloud folder?", cachePath);

        var cache = FileHeaderCache.Load(File.ReadAllBytes(cachePath), CloudPlatform.PC);

        string iniPath = Path.Combine(folder, CloudFiles.CloudStorageIni);
        byte[] ini = File.Exists(iniPath) ? File.ReadAllBytes(iniPath) : Encoding.ASCII.GetBytes(DEFAULT_CLOUD_STORAGE_INI);

        var result = new PcCloudFolder(cache, ini, Path.GetFullPath(folder));
        foreach (var entry in cache.Entries)
        {
            string filePath = Path.Combine(folder, entry.FileName);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"{entry.FileName} is listed in LocalFileHeaderCache but missing.", filePath);

            result._payloads[entry.FileName] = FileHeaderCache.ReadListedFile(entry, File.ReadAllBytes(filePath));
        }
        return result;
    }

    public static PcCloudFolder CreateNew()
    {
        var folder = new PcCloudFolder(new FileHeaderCache(CloudPlatform.PC), Encoding.ASCII.GetBytes(DEFAULT_CLOUD_STORAGE_INI), null);
        folder.SetFile(CloudFiles.Timestamp, CloudFiles.TimestampPayload(DateTime.Now));
        folder.SetFile(CloudFiles.Ctrb, CloudFiles.Int32Payload(0));
        return folder;
    }

    public SaveSlotData GetSlot(int slot)
    {
        if (!_payloads.TryGetValue(CloudFiles.Save(slot), out var save) ||
            !_payloads.TryGetValue(CloudFiles.Summary(slot), out var summary))
            throw new ArgumentException($"PC folder has no save in slot {slot + 1}.", nameof(slot));

        return new SaveSlotData(save, summary);
    }

    public void PutSlot(SaveSlotData data, int slot, bool makeCurrent = true)
    {
        if (slot < 0 || slot >= CloudFiles.MaxSlots)
            throw new ArgumentOutOfRangeException(nameof(slot), $"A save folder has slots 1 to {CloudFiles.MaxSlots}.");
        SetFile(CloudFiles.Save(slot), data.Save);
        SetFile(CloudFiles.Backup(slot), data.Save);
        if (makeCurrent || !_payloads.ContainsKey(CloudFiles.CurrentSlot))
            SetFile(CloudFiles.CurrentSlot, CloudFiles.Int32Payload(makeCurrent ? slot : 0));
        SetFile(CloudFiles.Summary(slot), data.Summary);
        SetFile(CloudFiles.BackupSummary(slot), data.Summary);
        SetFile(CloudFiles.Timestamp, CloudFiles.TimestampPayload(DateTime.Now)); // PC uses local time
    }

    void ISaveSet.PutSlot(SaveSlotData data, int slot) => PutSlot(data, slot, makeCurrent: false);

    public void RemoveAllCharacters()
    {
        foreach (string fileName in _payloads.Keys.Where(CloudFiles.IsCharacterFile).ToList())
        {
            _payloads.Remove(fileName);
            _cache.Remove(fileName);
        }
    }

    public void SetCurrentSlot(int slot) => SetFile(CloudFiles.CurrentSlot, CloudFiles.Int32Payload(slot));

    /// <summary>Writes every file to a folder. Refuses to overwrite the folder it was loaded from.</summary>
    public void SaveTo(string outputFolder)
    {
        string fullOutput = Path.GetFullPath(outputFolder);
        if (SourcePath is not null && string.Equals(Path.TrimEndingDirectorySeparator(fullOutput), Path.TrimEndingDirectorySeparator(SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to overwrite the source Cloud folder. Write to a new folder instead.");

        Directory.CreateDirectory(fullOutput);

        foreach (var (fileName, payload) in _payloads)
            File.WriteAllBytes(Path.Combine(fullOutput, fileName), PackageCrypto.EncryptIB3File(payload));

        File.WriteAllBytes(Path.Combine(fullOutput, CloudFiles.HeaderCache), _cache.ToEncryptedFile());
        File.WriteAllBytes(Path.Combine(fullOutput, CloudFiles.CloudStorageIni), _cloudStorageIni);
    }

    private void SetFile(string fileName, byte[] payload)
    {
        _payloads[fileName] = payload;
        _cache.Set(fileName, payload); // PC hashes the plain payload
    }
}
