using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace IBSaveEditor.Package.Conversion;

/// <summary>
/// An Infinity Blade II save set: a PC Cloud folder or the Android port's export zip. Both hold the same files, and
/// PC, Android and iOS all encrypt them the same way, with the IB2 key and no compression. There is no index file.
///   SwordSaveX_N-0.bin, BackupX_N-0.bin            the character (N = slot, 0-based)
///   SwordSaveSlotX_N.bin, BackupSlotX_N.bin        its slot summary
/// Each file is the IB2 magic, then AES(int32 0, NO_MAGIC, the properties), padded (zeros on PC, junk on Android).
/// The game also keeps the last played slot and a per-slot save counter equal to the summary's UpdateSaveCount:
///   PC       CloudStorage.ini        CurrentSlot0000=N, SwordSaveSlotX_N_dv=count
///   Android  the prefs plist         CLOUD::CurrentSlot{device ID, '_' for '-'}, CLOUD::SwordSaveSlotX_N_dv{device ID}
/// Both are rewritten to match the saves on every write. Payloads are held decrypted, by file name.
/// </summary>
public sealed partial class Ib2SaveSet : ISaveSet
{
    private const string CLOUD_STORAGE_INI = "CloudStorage.ini";
    private const string DEFAULT_INI = "[CloudStorageEmulation]\r\nUpgradeKey=1\r\n\r\n";
    private const string DEFAULT_INI_SLOT_SUFFIX = "0000";

    private readonly Dictionary<string, byte[]> _payloads = new();     // file name -> payload (NO_MAGIC + properties)
    private readonly Dictionary<string, byte[]> _otherZipFiles = new(); // zip path -> bytes (port info, plists)
    private string? _ini;                                              // PC CloudStorage.ini as loaded
    private string _deviceId = MobileTemplates.AndroidDeviceId;        // names the Android CLOUD:: keys
    private int? _currentSlot;

    private Ib2SaveSet() { }

    // IB2 names are IB3's without the leading underscore.
    public static string Save(int slot) => CloudFiles.Save(slot)[1..];
    public static string Backup(int slot) => CloudFiles.Backup(slot)[1..];
    public static string Summary(int slot) => CloudFiles.Summary(slot)[1..];
    public static string BackupSummary(int slot) => CloudFiles.BackupSummary(slot)[1..];

    /// <summary>True for a character's save or summary file, or their Backup copies (not the editor's .bak.bin copies).</summary>
    public static bool IsCharacterFile(string fileName) => CharacterFileRegex().IsMatch(fileName);

    /// <summary>True for a folder with IB2 saves in it.</summary>
    public static bool IsSaveFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) &&
        Directory.EnumerateFiles(folder).Any(f => IsCharacterFile(Path.GetFileName(f)));

    public IReadOnlyList<int> Slots =>
        _payloads.Keys.Where(n => _payloads.ContainsKey(SummaryOf(n))).Select(n => CloudFiles.SlotOfSave("_" + n)).OfType<int>().Order().ToList();

    public int CurrentSlot => _currentSlot ?? 0;

    public IReadOnlyDictionary<string, byte[]> Files => _payloads;

    public static Ib2SaveSet CreateNew() => new();

    // ---------- One file ----------

    /// <summary>Decrypts one IB2 save file and cuts off its padding.</summary>
    public static byte[] Unwrap(byte[] file)
    {
        byte[] plain = PackageCrypto.DecryptIB2File(file);
        if (plain.Length < 2 * sizeof(uint) || BitConverter.ToUInt32(plain, sizeof(int)) != PackageConstants.NO_MAGIC)
            throw new InvalidDataException("This isn't an Infinity Blade II save, or it is damaged.");
        return SingleSaveFile.UpToEnd(plain, sizeof(int));
    }

    /// <summary>The reverse of <see cref="Unwrap"/>, with the leading 0 the game writes.</summary>
    public static byte[] Wrap(byte[] payload)
    {
        var data = new byte[sizeof(int) + payload.Length];
        payload.CopyTo(data, sizeof(int));
        return PackageCrypto.EncryptIB2File(data);
    }

    // ---------- PC folder ----------

    public static Ib2SaveSet LoadFolder(string folder)
    {
        var names = Directory.EnumerateFiles(folder).Select(p => Path.GetFileName(p)).ToHashSet();
        var set = Load(names, name => File.ReadAllBytes(Path.Combine(folder, name)));

        string iniPath = Path.Combine(folder, CLOUD_STORAGE_INI);
        if (File.Exists(iniPath))
        {
            set._ini = File.ReadAllText(iniPath);
            if (IniCurrentSlotRegex().Match(set._ini) is { Success: true } match)
                set._currentSlot = int.Parse(match.Groups["slot"].Value);
        }
        return set;
    }

    public void SaveToFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, payload) in _payloads)
            File.WriteAllBytes(Path.Combine(folder, name), Wrap(payload));
        File.WriteAllText(Path.Combine(folder, CLOUD_STORAGE_INI), BookkeepingIni());
    }

    // ---------- Android zip ----------

    public static Ib2SaveSet LoadZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        // Allow zips that wrap everything in one extra folder.
        string? anySave = zip.Entries.Select(e => ZipFiles.Normalize(e.FullName))
                                     .FirstOrDefault(p => p.Contains(MobileTemplates.SaveFolder, StringComparison.OrdinalIgnoreCase));
        if (anySave is null)
            throw new InvalidDataException("Zip has no userdata/Documents/SAVE folder. Is it an Android save export?");
        string root = anySave[..anySave.IndexOf(MobileTemplates.SaveFolder, StringComparison.OrdinalIgnoreCase)];

        var others = zip.Entries.Select(e => (Path: ZipFiles.Normalize(e.FullName), Entry: e))
                                .Where(e => e.Path.StartsWith(root, StringComparison.Ordinal) && !e.Path.EndsWith('/'))
                                .Select(e => (Relative: e.Path[root.Length..], e.Entry))
                                .Where(e => !e.Relative.StartsWith(MobileTemplates.SaveFolder, StringComparison.OrdinalIgnoreCase))
                                .ToDictionary(e => e.Relative, e => ZipFiles.Read(e.Entry));
        if (others.TryGetValue(MobileTemplates.PortInfoPath, out var portInfo) && MobileTemplates.IsOtherGame(portInfo, "Infinity Blade II"))
            throw new InvalidDataException("This is an Infinity Blade III save. Go back and pick Infinity Blade III.");

        string saveRoot = root + MobileTemplates.SaveFolder;
        var names = zip.Entries.Select(e => ZipFiles.Normalize(e.FullName))
                               .Where(p => p.StartsWith(saveRoot, StringComparison.Ordinal) && !p.EndsWith('/'))
                               .Select(p => p[saveRoot.Length..])
                               .ToHashSet();
        var set = Load(names, name => ZipFiles.Read(zip.GetEntry(saveRoot + name)!));

        foreach (var (path, bytes) in others)
            set._otherZipFiles[path] = bytes;
        if (others.TryGetValue(MobileTemplates.KeychainPath, out var keychain) && MobileSave.ReadDeviceIdFromKeychain(keychain) is { } id)
            set._deviceId = id;
        if (others.TryGetValue(MobileTemplates.PrefsPath, out var prefs) &&
            PlistCurrentSlotRegex().Match(Encoding.UTF8.GetString(prefs)) is { Success: true } match)
            set._currentSlot = int.Parse(match.Groups["slot"].Value);
        return set;
    }

    public void SaveToZip(string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        ZipFiles.Write(zip, MobileTemplates.PortInfoPath, _otherZipFiles.GetValueOrDefault(MobileTemplates.PortInfoPath) ?? MobileTemplates.Ib2PortInfo);
        foreach (var (name, payload) in _payloads.OrderBy(p => p.Key, StringComparer.Ordinal))
            ZipFiles.Write(zip, MobileTemplates.SaveFolder + name, Wrap(payload));
        ZipFiles.Write(zip, MobileTemplates.PrefsPath, BookkeepingPlist());
        ZipFiles.Write(zip, MobileTemplates.KeychainPath, _otherZipFiles.GetValueOrDefault(MobileTemplates.KeychainPath) ?? MobileTemplates.Keychain(_deviceId));
    }

    // ---------- Characters ----------

    public SaveSlotData GetSlot(int slot)
    {
        if (!_payloads.TryGetValue(Save(slot), out var save) || !_payloads.TryGetValue(Summary(slot), out var summary))
            throw new ArgumentException($"This save has no character in slot {slot + 1}.", nameof(slot));
        return new SaveSlotData(save, summary);
    }

    public void PutSlot(SaveSlotData data, int slot)
    {
        if (slot < 0 || slot >= CloudFiles.MaxSlots)
            throw new ArgumentOutOfRangeException(nameof(slot), $"A save holds at most {CloudFiles.MaxSlots} characters.");
        _payloads[Save(slot)] = data.Save;
        _payloads[Backup(slot)] = data.Save;
        _payloads[Summary(slot)] = data.Summary;
        _payloads[BackupSummary(slot)] = data.Summary;
        _currentSlot ??= slot;
    }

    public void RemoveAllCharacters() => _payloads.Clear();

    public void SetCurrentSlot(int slot) => _currentSlot = slot;

    // Each file, or its Backup twin when the main one is missing or unreadable. A slot needs a save and a summary.
    private static Ib2SaveSet Load(IReadOnlySet<string> names, Func<string, byte[]> read)
    {
        var set = new Ib2SaveSet();
        for (int slot = 0; slot < CloudFiles.MaxSlots; slot++)
        {
            byte[]? save = ReadEither(Save(slot), Backup(slot));
            byte[]? summary = ReadEither(Summary(slot), BackupSummary(slot));
            if (save is null && summary is null)
                continue;
            if (save is null || summary is null)
                throw new InvalidDataException($"Slot {slot + 1} has a {(save is null ? "summary but no save" : "save but no summary")} that can be read.");

            set._payloads[Save(slot)] = set._payloads[Backup(slot)] = save;
            set._payloads[Summary(slot)] = set._payloads[BackupSummary(slot)] = summary;
        }
        return set;

        byte[]? ReadEither(string main, string backup)
        {
            foreach (string name in new[] { main, backup }.Where(names.Contains))
            {
                try { return Unwrap(read(name)); }
                catch (InvalidDataException) { }
            }
            return null;
        }
    }

    private static string SummaryOf(string saveName) =>
        CloudFiles.SlotOfSave("_" + saveName) is int slot ? Summary(slot) : string.Empty;

    // ---------- Cloud bookkeeping ----------

    private IEnumerable<(int Slot, int Count)> SaveCounts()
    {
        foreach (int slot in Slots)
            if (GetSlot(slot).Info.UpdateSaveCount is int count)
                yield return (slot, count);
    }

    // The loaded ini with its CurrentSlot and _dv lines replaced. Everything else is kept.
    private string BookkeepingIni()
    {
        string ini = _ini ?? DEFAULT_INI;
        string suffix = IniCurrentSlotRegex().Match(ini) is { Success: true } match ? match.Groups["suffix"].Value : DEFAULT_INI_SLOT_SUFFIX;

        var lines = ini.Split('\n').Select(l => l.TrimEnd('\r'))
                       .Where(l => !IniCurrentSlotRegex().IsMatch(l) && !IniSaveCountRegex().IsMatch(l))
                       .ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        lines.Add($"CurrentSlot{suffix}={CurrentSlot}");
        lines.AddRange(SaveCounts().Select(p => $"SwordSaveSlotX_{p.Slot}_dv={p.Count}"));
        return string.Join("\r\n", lines) + "\r\n\r\n";
    }

    // The loaded prefs plist (or the template) with its CLOUD::CurrentSlot and _dv entries replaced.
    private byte[] BookkeepingPlist()
    {
        string plist = Encoding.UTF8.GetString(_otherZipFiles.GetValueOrDefault(MobileTemplates.PrefsPath) ?? MobileTemplates.Ib2Prefs);
        plist = PlistBookkeepingRegex().Replace(plist, string.Empty);

        var entries = new StringBuilder();
        entries.Append($"\t<key>CLOUD::CurrentSlot{_deviceId.Replace('-', '_')}</key>\n\t<string>{CurrentSlot}</string>\n");
        foreach (var (slot, count) in SaveCounts())
            entries.Append($"\t<key>CLOUD::SwordSaveSlotX_{slot}_dv{_deviceId}</key>\n\t<string>{count}</string>\n");

        int end = plist.LastIndexOf("</dict>", StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidDataException("The Android preferences file is damaged.");
        return Encoding.UTF8.GetBytes(plist.Insert(end, entries.ToString()));
    }

    [GeneratedRegex(@"^(SwordSaveX_\d+-0|BackupX_\d+-0|SwordSaveSlotX_\d+|BackupSlotX_\d+)\.bin$")]
    private static partial Regex CharacterFileRegex();

    [GeneratedRegex(@"^CurrentSlot(?<suffix>\w*)=(?<slot>\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex IniCurrentSlotRegex();

    [GeneratedRegex(@"^SwordSaveSlotX_\d+_dv=", RegexOptions.Multiline)]
    private static partial Regex IniSaveCountRegex();

    [GeneratedRegex(@"<key>CLOUD::CurrentSlot[^<]*</key>\s*<string>(?<slot>\d+)</string>")]
    private static partial Regex PlistCurrentSlotRegex();

    [GeneratedRegex(@"[ \t]*<key>CLOUD::(?:CurrentSlot|SwordSaveSlotX_\d+_dv)[^<]*</key>\s*<string>[^<]*</string>\r?\n?")]
    private static partial Regex PlistBookkeepingRegex();
}
