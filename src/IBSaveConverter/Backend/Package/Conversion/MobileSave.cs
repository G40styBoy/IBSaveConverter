using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace IBSaveEditor.Package.Conversion;

/// <summary>No save key unlocks LocalFileHeaderCache: an iOS save locked to a device whose key isn't known yet.</summary>
public sealed class SaveKeyNotFoundException(string message) : IOException(message);

/// <summary>
/// A save key decrypted LocalFileHeaderCache, so it is the right key, but the save still couldn't be read.
/// Carries that key so it can be shown anyway. <see cref="DeviceId"/> is null for the fixed key.
/// </summary>
public sealed class MatchedKeyException(string? deviceId, string message, Exception inner) : IOException(message, inner)
{
    public string? DeviceId { get; } = deviceId;
}

/// <summary>
/// A mobile save set: the Android port's export zip, or an iOS SAVE folder. Both hold the same files, dpiZ
/// compressed and encrypted. Android uses its fixed port device ID as the key. iOS uses the device's own ID
/// (a legacy save), or the game's fixed IB3 key once the game has the fixed-key patch.
/// The zip has ib-port-saves.txt and userdata/ at the root:
///   userdata/Documents/SAVE/*                 the save files
///   userdata/Library/keychain.plist           device ID (the AES key)
///   userdata/Library/Preferences/...IB3.plist game preferences
/// Payloads are held decrypted and decompressed, by their plain file name.
/// </summary>
public sealed partial class MobileSave : ISaveSet
{
    private readonly Dictionary<string, byte[]> _payloads = new();     // plain SAVE file name -> plain payload
    private readonly Dictionary<string, byte[]> _otherFiles = new();   // zip path -> bytes (port info, plists)

    public IReadOnlyList<int> Slots =>
        _payloads.Keys.Select(CloudFiles.SlotOfSave).OfType<int>().Order().ToList();

    public IReadOnlyDictionary<string, byte[]> Files => _payloads;

    /// <summary>The device ID the save was read with, or null for the fixed IB3 key.</summary>
    public string? DeviceId { get; private set; }

    /// <summary>The player's online account ID, put in front of every file name. Usually empty.</summary>
    public string McpId { get; private set; } = string.Empty;

    private MobileSave() { }

    public static MobileSave CreateNew() => new();

    public static MobileSave LoadZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        if (zip.Entries.FirstOrDefault(e => ZipFiles.Normalize(e.FullName).EndsWith(MobileTemplates.PortInfoPath, StringComparison.OrdinalIgnoreCase)) is { } portInfo &&
            MobileTemplates.IsOtherGame(ZipFiles.Read(portInfo), "Infinity Blade III"))
            throw new InvalidDataException("This is an Infinity Blade II save. Go back and pick Infinity Blade II.");

        // Allow zips that wrap everything in one extra folder.
        var cacheEntry = zip.Entries.FirstOrDefault(e => ZipFiles.Normalize(e.FullName).EndsWith(MobileTemplates.SaveFolder + CloudFiles.HeaderCache, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Zip has no userdata/Documents/SAVE/LocalFileHeaderCache. Is it a mobile save export?");
        string cachePath = ZipFiles.Normalize(cacheEntry.FullName);
        string root = cachePath[..^(MobileTemplates.SaveFolder + CloudFiles.HeaderCache).Length];

        var deviceIds = new List<string?>();
        if (zip.GetEntry(root + MobileTemplates.KeychainPath) is { } keychain && ReadDeviceIdFromKeychain(ZipFiles.Read(keychain)) is { } fromZip)
            deviceIds.Add(fromZip);
        deviceIds.Add(MobileTemplates.AndroidDeviceId);

        string saveRoot = root + MobileTemplates.SaveFolder;
        var names = zip.Entries.Select(e => ZipFiles.Normalize(e.FullName))
                               .Where(p => p.StartsWith(saveRoot, StringComparison.Ordinal) && !p.EndsWith('/'))
                               .Select(p => p[saveRoot.Length..])
                               .Where(n => !n.Contains('/'))
                               .ToList();
        var result = Read(ZipFiles.Read(cacheEntry), deviceIds, names, name => ZipFiles.Read(zip.GetEntry(saveRoot + name)!));

        foreach (var entry in zip.Entries)
        {
            string path = ZipFiles.Normalize(entry.FullName);
            if (path.EndsWith('/') || !path.StartsWith(root, StringComparison.Ordinal))
                continue;
            string relative = path[root.Length..];
            if (!relative.StartsWith(MobileTemplates.SaveFolder, StringComparison.OrdinalIgnoreCase))
                result._otherFiles[relative] = ZipFiles.Read(entry);
        }
        return result;
    }

    /// <summary>
    /// Loads an iOS SAVE folder. The fixed key is tried first, then each device ID.
    /// Throws <see cref="SaveKeyNotFoundException"/> when none of them unlocks it.
    /// </summary>
    public static MobileSave LoadFolder(string folder, IEnumerable<string> deviceIds)
    {
        string cachePath = Path.Combine(folder, CloudFiles.HeaderCache);
        if (!File.Exists(cachePath))
            throw new FileNotFoundException("No LocalFileHeaderCache in this folder. Is it the game's SAVE folder?", cachePath);

        var names = Directory.EnumerateFiles(folder).Select(p => Path.GetFileName(p)).ToList();

        // The ID in front of the file names is the account ID, not the device ID. It costs nothing to try it as well.
        var candidates = new List<string?> { null };
        candidates.AddRange(deviceIds.Concat(names.Select(CloudFiles.IdPrefixOf).OfType<string>()));

        return Read(File.ReadAllBytes(cachePath), candidates, names, name => File.ReadAllBytes(Path.Combine(folder, name)));
    }

    // Android uses the mobile cache layout. iOS saves are expected to match it; the PC layout is the fallback.
    // fileNames are the names as stored, which may have the McpId in front.
    private static MobileSave Read(byte[] cacheFile, IReadOnlyList<string?> deviceIds, IReadOnlyList<string> fileNames,
                                   Func<string, byte[]> readStoredFile)
    {
        var byPlainName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in fileNames.OrderBy(n => n == CloudFiles.PlainName(n) ? 0 : 1))
            byPlainName.TryAdd(CloudFiles.PlainName(name), name);
        byte[]? ReadFile(string name) =>
            byPlainName.TryGetValue(CloudFiles.PlainName(name), out string? stored) ? readStoredFile(stored) : null;

        var (deviceId, key) = FindKey(cacheFile, deviceIds);
        try
        {
            var result = ReadWithKey(cacheFile, key, ReadFile, fileNames);
            result.DeviceId = deviceId;
            return result;
        }
        catch (Exception ex) when (ex is not MatchedKeyException)
        {
            // The key did decrypt LocalFileHeaderCache, so it is the right one. Hand it back with the failure.
            throw new MatchedKeyException(deviceId, ex.Message, ex);
        }
    }

    private static MobileSave ReadWithKey(byte[] cacheFile, byte[]? key, Func<string, byte[]?> readFile, IReadOnlyList<string> fileNames)
    {
        Exception? firstError = null;

        foreach (var layout in new[] { CloudPlatform.Mobile, CloudPlatform.PC })
        {
            try
            {
                var cache = FileHeaderCache.Load(cacheFile, layout, key);
                var result = new MobileSave { McpId = cache.McpId };
                foreach (var entry in cache.Entries)
                {
                    byte[] file = readFile(entry.FileName)
                        ?? throw new InvalidDataException($"{entry.FileName} is listed in LocalFileHeaderCache but missing.");

                    byte[] stored = FileHeaderCache.ReadListedFile(entry, file, key);
                    result._payloads[CloudFiles.PlainName(entry.FileName)] = Dpiz.IsPacked(stored) ? Dpiz.Unpack(stored) : stored;
                }
                return result;
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or EndOfStreamException or ArgumentException)
            {
                firstError ??= ex;
            }
        }

        // The game can leave LocalFileHeaderCache cut short when a save is interrupted, while the save
        // files themselves are fine. They carry their own sizes, so read them without the cache.
        try
        {
            return ReadWithoutCache(key, readFile, fileNames);
        }
        catch (InvalidDataException fallbackError)
        {
            throw new InvalidDataException(
                "LocalFileHeaderCache is damaged or cut short, and the save files could not be read without it: " +
                fallbackError.Message, firstError);
        }
    }

    /// <summary>
    /// Reads the SAVE files without LocalFileHeaderCache. Character files are dpiZ compressed and hold their own
    /// size; the small bookkeeping files are cut to their known sizes. A file that fails to read is skipped, a
    /// broken main file is replaced by its Backup twin, and a slot missing its save or summary is dropped.
    /// </summary>
    private static MobileSave ReadWithoutCache(byte[]? key, Func<string, byte[]?> readFile, IReadOnlyList<string> fileNames)
    {
        var result = new MobileSave { McpId = fileNames.Select(CloudFiles.IdPrefixOf).OfType<string>().FirstOrDefault() ?? string.Empty };
        var report = new List<string>(); // what happened to each file, shown if nothing can be read
        foreach (string name in fileNames)
        {
            if (readFile(name) is not { } file)
                continue;
            try
            {
                if (PayloadWithoutPadding(CloudFiles.PlainName(name), PackageCrypto.DecryptIB3File(file, key)) is { } payload)
                    result._payloads[CloudFiles.PlainName(name)] = payload;
                else
                    report.Add($"{name} ({file.Length} bytes): not a save file this app knows");
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or ArgumentException)
            {
                // Not a game file, or damaged. Its Backup twin may still be fine.
                report.Add($"{name} ({file.Length} bytes): {ex.Message}");
            }
        }

        for (int slot = 0; slot < CloudFiles.MaxSlots; slot++)
        {
            bool hasSave    = result.PreferGood(CloudFiles.Save(slot), CloudFiles.Backup(slot), LooksLikeSave);
            bool hasSummary = result.PreferGood(CloudFiles.Summary(slot), CloudFiles.BackupSummary(slot), LooksLikeSummary);
            if (hasSave && hasSummary)
                continue;
            if (hasSave || hasSummary)
                report.Add($"slot {slot}: dropped, the save is {(hasSave ? "fine" : "missing or unreadable")} and the summary is {(hasSummary ? "fine" : "missing or unreadable")}");
            foreach (string name in new[] { CloudFiles.Save(slot), CloudFiles.Backup(slot), CloudFiles.Summary(slot), CloudFiles.BackupSummary(slot) })
                result._payloads.Remove(name);
        }

        if (result.Slots.Count == 0)
        {
            string seen = report.Count == 0 ? "the folder holds no files this app could open" : string.Join("; ", report.Take(10));
            throw new InvalidDataException($"no character could be read from the save files. {seen}");
        }
        return result;
    }

    // Returns the real payload inside a decrypted, zero padded file, or null for files it doesn't know.
    private static byte[]? PayloadWithoutPadding(string name, byte[] plain)
    {
        if (CloudFiles.IsCharacterFile(name) && name.EndsWith(".bin", StringComparison.Ordinal))
        {
            if (Dpiz.IsPacked(plain))
                return Dpiz.Unpack(plain);
            int end = plain.AsSpan().LastIndexOf(CloudFiles.TerminatorBytes);
            return end < 0 ? null : plain[..(end + CloudFiles.TerminatorBytes.Length)];
        }
        if (name is CloudFiles.CurrentSlot or CloudFiles.Ctrb)
            return plain.Length >= sizeof(int) ? plain[..sizeof(int)] : null;
        if (name is CloudFiles.Timestamp && plain.Length >= sizeof(int))
        {
            int length = BitConverter.ToInt32(plain, 0);
            return length is > 0 and <= 64 && sizeof(int) + length <= plain.Length ? plain[..(sizeof(int) + length)] : null;
        }
        return null;
    }

    // Keeps a good copy under both names. Returns false when neither copy is good.
    private bool PreferGood(string main, string backup, Func<byte[], bool> isGood)
    {
        bool mainGood   = _payloads.TryGetValue(main, out var mainBytes) && isGood(mainBytes);
        bool backupGood = _payloads.TryGetValue(backup, out var backupBytes) && isGood(backupBytes);
        if (!mainGood && backupGood)
            _payloads[main] = backupBytes!;
        else if (mainGood && !backupGood)
            _payloads[backup] = mainBytes!;
        return mainGood || backupGood;
    }

    private static bool LooksLikeSave(byte[] save) =>
        save.Length > sizeof(uint) + CloudFiles.TerminatorBytes.Length &&
        BitConverter.ToUInt32(save, 0) == PackageConstants.NO_MAGIC &&
        save.AsSpan().EndsWith(CloudFiles.TerminatorBytes);

    private static bool LooksLikeSummary(byte[] summary)
    {
        try
        {
            SlotSummary.Read(summary);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or ArgumentException)
        {
            return false;
        }
    }

    public SaveSlotData GetSlot(int slot)
    {
        if (!_payloads.TryGetValue(CloudFiles.Save(slot), out var save) ||
            !_payloads.TryGetValue(CloudFiles.Summary(slot), out var summary))
            throw new ArgumentException($"Mobile save has no character in slot {slot + 1}.", nameof(slot));

        return new SaveSlotData(save, summary);
    }

    public bool HasLiveCharacter(int slot) =>
        _payloads.ContainsKey(CloudFiles.Save(slot)) && _payloads.ContainsKey(CloudFiles.Summary(slot)) && !GetSlot(slot).Info.IsDeleted;

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

    public void RemoveAllCharacters()
    {
        foreach (string name in _payloads.Keys.Where(CloudFiles.IsCharacterFile).ToList())
            _payloads.Remove(name);
    }

    public int CurrentSlot =>
        _payloads.TryGetValue(CloudFiles.CurrentSlot, out var value) && value.Length >= 4 ? BitConverter.ToInt32(value) : 0;

    public void SetCurrentSlot(int slot) => _payloads[CloudFiles.CurrentSlot] = CloudFiles.Int32Payload(slot);

    /// <summary>Writes the Android export zip, encrypted with <paramref name="deviceId"/>.</summary>
    public void SaveTo(string zipPath, string deviceId)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        ZipFiles.Write(zip, MobileTemplates.PortInfoPath, _otherFiles.GetValueOrDefault(MobileTemplates.PortInfoPath) ?? MobileTemplates.PortInfo);
        foreach (var (name, bytes) in EncryptedFiles(deviceId))
            ZipFiles.Write(zip, MobileTemplates.SaveFolder + name, bytes);
        ZipFiles.Write(zip, MobileTemplates.PrefsPath, _otherFiles.GetValueOrDefault(MobileTemplates.PrefsPath) ?? MobileTemplates.Prefs);
        // Always the ID the files above were encrypted with, never a template's.
        ZipFiles.Write(zip, MobileTemplates.KeychainPath, MobileTemplates.Keychain(deviceId.Trim()));
    }

    /// <summary>Writes an iOS SAVE folder, encrypted with <paramref name="deviceId"/> or, when null, the fixed key.</summary>
    public void SaveToFolder(string folder, string? deviceId)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in EncryptedFiles(deviceId))
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
    }

    // LocalFileHeaderCache, then every file under its stored name (McpId + plain name).
    private IEnumerable<(string Name, byte[] Bytes)> EncryptedFiles(string? deviceId)
    {
        byte[]? key = MobileTemplates.KeyFor(deviceId);
        var cache = new FileHeaderCache(CloudPlatform.Mobile) { McpId = McpId };
        var stored = new List<(string Name, byte[] Bytes)>();

        foreach (var (fileName, payload) in CacheOrder())
        {
            byte[] bytes = CloudFiles.IsCompressedOnMobile(fileName) ? Dpiz.Pack(payload) : payload;
            stored.Add((fileName, bytes));
            cache.Set(fileName, bytes); // mobile hashes the compressed bytes
        }

        yield return (CloudFiles.HeaderCache, cache.ToEncryptedFile(key));
        foreach (var (fileName, bytes) in stored.OrderBy(p => p.Name, StringComparer.Ordinal))
            yield return (McpId + fileName, PackageCrypto.EncryptIB3File(bytes, key));
    }

    /// <summary>
    /// Cache order of a real export: _CTN, _CTRB, slot 1's save files, _CurrentSlot, slot 1's summary
    /// files, then summary before save files for each later slot. Anything else goes last.
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

    // Candidates are device IDs, or null for the fixed key. The first one that decrypts the cache wins.
    private static (string? DeviceId, byte[]? Key) FindKey(byte[] cacheFile, IEnumerable<string?> deviceIds)
    {
        foreach (string? id in deviceIds.Select(i => i?.Trim()).Distinct())
        {
            byte[]? key;
            try { key = MobileTemplates.KeyFor(id); }
            catch (ArgumentException) { continue; }

            if (FileHeaderCache.LooksValid(PackageCrypto.DecryptIB3File(cacheFile, key)))
                return (id, key);
        }
        throw new SaveKeyNotFoundException("None of the save keys unlocks this save. Make sure the save and the key come from the same device.");
    }

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
}
