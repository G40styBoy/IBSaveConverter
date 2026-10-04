using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace IBSaveEditor.Package.Conversion;

/// <summary>
/// The Android port's save export: a zip with ib-port-saves.txt and userdata/ at the root.
///   userdata/Documents/SAVE/*                 the save files
///   userdata/Library/keychain.plist           device ID (the AES key)
///   userdata/Library/Preferences/...IB3.plist game preferences
///
/// Save and summary files are dpiZ compressed after decryption. Everything is kept in memory
/// as plain, decompressed payloads, and is always written back with the fixed port device ID.
/// </summary>
public sealed partial class MobileSaveZip
{
    private readonly Dictionary<string, byte[]> _payloads = new();     // SAVE file name -> plain payload
    private readonly Dictionary<string, byte[]> _otherFiles = new();   // zip path -> bytes (port info, plists)

    /// <summary>Slot numbers that have a main save file (0 = in-game slot 1).</summary>
    public IReadOnlyList<int> Slots =>
        _payloads.Keys.Select(CloudFiles.SlotOfSave).OfType<int>().Order().ToList();

    /// <summary>The device ID this zip was encrypted with (read from its keychain.plist, or the fixed port ID).</summary>
    public string DeviceId { get; private set; } = MobileTemplates.FixedDeviceId;

    private MobileSaveZip() { }

    public static MobileSaveZip Load(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        // Allow zips that wrap everything in one extra folder.
        var cacheEntry = zip.Entries.FirstOrDefault(e => Normalize(e.FullName).EndsWith(MobileTemplates.SaveFolder + CloudFiles.HeaderCache, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Zip has no userdata/Documents/SAVE/LocalFileHeaderCache. Is it a mobile save export?");
        string cachePath = Normalize(cacheEntry.FullName);
        string root = cachePath[..^(MobileTemplates.SaveFolder + CloudFiles.HeaderCache).Length];

        byte[] cacheFile = ReadEntry(cacheEntry);
        var (deviceId, key) = FindKey(zip, root, cacheFile);
        var cache = FileHeaderCache.Load(cacheFile, CloudPlatform.Mobile, key);

        var result = new MobileSaveZip { DeviceId = deviceId };
        foreach (var entry in cache.Entries)
        {
            var zipEntry = zip.GetEntry(root + MobileTemplates.SaveFolder + entry.FileName)
                ?? throw new InvalidDataException($"{entry.FileName} is listed in LocalFileHeaderCache but missing from the zip.");

            byte[] stored = FileHeaderCache.ReadListedFile(entry, ReadEntry(zipEntry), key);
            result._payloads[entry.FileName] = Dpiz.IsPacked(stored) ? Dpiz.Unpack(stored) : stored;
        }

        foreach (var entry in zip.Entries)
        {
            string path = Normalize(entry.FullName);
            if (path.EndsWith('/') || !path.StartsWith(root, StringComparison.Ordinal))
                continue;
            string relative = path[root.Length..];
            if (!relative.StartsWith(MobileTemplates.SaveFolder, StringComparison.OrdinalIgnoreCase))
                result._otherFiles[relative] = ReadEntry(entry);
        }
        return result;
    }

    /// <summary>A new export with no characters yet, using the template port files.</summary>
    public static MobileSaveZip CreateNew() => new();

    public SaveSlotData GetSlot(int slot)
    {
        if (!_payloads.TryGetValue(CloudFiles.Save(slot), out var save) ||
            !_payloads.TryGetValue(CloudFiles.Summary(slot), out var summary))
            throw new ArgumentException($"Mobile save has no character in slot {slot + 1}.", nameof(slot));

        return new SaveSlotData(save, summary);
    }

    /// <summary>True if the slot holds a character that isn't marked deleted.</summary>
    public bool HasLiveCharacter(int slot) =>
        _payloads.ContainsKey(CloudFiles.Save(slot)) && _payloads.ContainsKey(CloudFiles.Summary(slot)) && !GetSlot(slot).Info.IsDeleted;

    /// <summary>
    /// Puts a character into a slot (replacing whatever was there) and keeps every other slot.
    /// Writes the Backup copies too, and refreshes _CTN. Slot files use the same names as on PC.
    /// </summary>
    public void PutSlot(SaveSlotData data, int slot)
    {
        if (slot < 0 || slot >= CloudFiles.MaxSlots)
            throw new ArgumentOutOfRangeException(nameof(slot), $"A save holds at most {CloudFiles.MaxSlots} characters.");
        _payloads[CloudFiles.Timestamp] = CloudFiles.TimestampPayload(DateTime.UtcNow); // mobile uses UTC
        _payloads.TryAdd(CloudFiles.Ctrb, CloudFiles.Int32Payload(0));
        _payloads.TryAdd(CloudFiles.CurrentSlot, CloudFiles.Int32Payload(slot));
        _payloads[CloudFiles.Save(slot)]          = data.Save;
        _payloads[CloudFiles.Backup(slot)]        = data.Save;
        _payloads[CloudFiles.Summary(slot)]       = data.Summary;
        _payloads[CloudFiles.BackupSummary(slot)] = data.Summary;
    }

    /// <summary>The slot the game opens next (0 if not set).</summary>
    public int CurrentSlot =>
        _payloads.TryGetValue(CloudFiles.CurrentSlot, out var value) && value.Length >= 4 ? BitConverter.ToInt32(value) : 0;

    /// <summary>Which slot the game opens next.</summary>
    public void SetCurrentSlot(int slot) => _payloads[CloudFiles.CurrentSlot] = CloudFiles.Int32Payload(slot);

    /// <summary>Removes every character. The port's other files (prefs, port info) are kept.</summary>
    public void RemoveAllCharacters() => _payloads.Clear();

    /// <summary>Replaces the whole SAVE folder with one character in slot 1 (slot index 0).</summary>
    public void SetOnlyCharacter(SaveSlotData data)
    {
        RemoveAllCharacters();
        PutSlot(data, 0);
        SetCurrentSlot(0);
    }

    /// <summary>
    /// Writes the export zip, files at the zip root, encrypted with <paramref name="deviceId"/>
    /// (default: the fixed port ID every install uses). keychain.plist is written with the same ID.
    /// The cache is rebuilt in the same order and with the same values a real export uses.
    /// </summary>
    public void SaveTo(string zipPath, string? deviceId = null)
    {
        deviceId ??= MobileTemplates.FixedDeviceId;
        byte[] key = MobileTemplates.KeyFromDeviceId(deviceId);
        var cache = new FileHeaderCache(CloudPlatform.Mobile);
        var stored = new Dictionary<string, byte[]>();

        foreach (var (fileName, payload) in CacheOrder())
        {
            byte[] bytes = CloudFiles.IsCompressedOnMobile(fileName) ? Dpiz.Pack(payload) : payload;
            stored[fileName] = bytes;
            cache.Set(fileName, bytes); // mobile hashes the compressed bytes
        }

        if (File.Exists(zipPath))
            File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        WriteEntry(zip, MobileTemplates.PortInfoPath, _otherFiles.GetValueOrDefault(MobileTemplates.PortInfoPath) ?? MobileTemplates.PortInfo);
        WriteEntry(zip, MobileTemplates.SaveFolder + CloudFiles.HeaderCache, cache.ToEncryptedFile(key));
        foreach (var (fileName, bytes) in stored.OrderBy(p => p.Key, StringComparer.Ordinal))
            WriteEntry(zip, MobileTemplates.SaveFolder + fileName, PackageCrypto.EncryptIB3File(bytes, key));
        WriteEntry(zip, MobileTemplates.PrefsPath, _otherFiles.GetValueOrDefault(MobileTemplates.PrefsPath) ?? MobileTemplates.Prefs);
        // Always the ID the files above were encrypted with, never a template's.
        WriteEntry(zip, MobileTemplates.KeychainPath, MobileTemplates.Keychain(deviceId.Trim()));
    }

    /// <summary>
    /// Files in the order a real export lists them in its cache: _CTN, _CTRB, slot 1's save files,
    /// _CurrentSlot, slot 1's summary files, then for each later slot its summary files before its
    /// save files (seen in a two-character export). Anything else goes last.
    /// </summary>
    private IEnumerable<KeyValuePair<string, byte[]>> CacheOrder()
    {
        var order = new List<string> { CloudFiles.Timestamp, CloudFiles.Ctrb, CloudFiles.Save(0), CloudFiles.Backup(0),
                                       CloudFiles.CurrentSlot, CloudFiles.Summary(0), CloudFiles.BackupSummary(0) };
        foreach (int slot in Slots.Where(s => s > 0))
            order.AddRange(new[] { CloudFiles.Summary(slot), CloudFiles.BackupSummary(slot), CloudFiles.Save(slot), CloudFiles.Backup(slot) });

        foreach (string name in order.Where(_payloads.ContainsKey))
            yield return new(name, _payloads[name]);
        foreach (var pair in _payloads.Where(p => !order.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal))
            yield return pair;
    }

    /// <summary>
    /// Tries the ID in the zip's keychain.plist first, then the fixed port ID.
    /// A key is right when it decrypts the cache to a valid header.
    /// </summary>
    private static (string DeviceId, byte[] Key) FindKey(ZipArchive zip, string root, byte[] cacheFile)
    {
        var candidates = new List<string>();
        var keychain = zip.GetEntry(root + MobileTemplates.KeychainPath);
        if (keychain is not null && ReadDeviceIdFromKeychain(ReadEntry(keychain)) is { } fromZip)
            candidates.Add(fromZip);
        candidates.Add(MobileTemplates.FixedDeviceId);

        foreach (string id in candidates)
        {
            byte[] key;
            try { key = MobileTemplates.KeyFromDeviceId(id); }
            catch (ArgumentException) { continue; }

            if (FileHeaderCache.LooksValid(PackageCrypto.DecryptIB3File(cacheFile, key)))
                return (id.Trim(), key);
        }
        throw new InvalidDataException("Could not find the key for this mobile save. keychain.plist is missing or does not match.");
    }

    /// <summary>Reads the "||" value from an XML keychain.plist (stored as base64 data or as a string).</summary>
    public static string? ReadDeviceIdFromKeychain(byte[] plist)
    {
        Match match = KeychainRegex().Match(Encoding.UTF8.GetString(plist));
        if (!match.Success)
            return null;

        if (match.Groups["data"].Success)
        {
            try { return Encoding.ASCII.GetString(Convert.FromBase64String(match.Groups["data"].Value.Trim())); }
            catch (FormatException) { return null; }
        }
        return match.Groups["str"].Value.Trim();
    }

    [GeneratedRegex(@"<key>\|\|</key>\s*(?:<data>(?<data>[^<]*)</data>|<string>(?<str>[^<]*)</string>)")]
    private static partial Regex KeychainRegex();

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string path, byte[] data)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(data);
    }
}
