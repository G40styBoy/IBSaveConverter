using System.Globalization;
using System.Text.RegularExpressions;
using IBSaveEditor.Wrappers;

namespace IBSaveEditor.Package.Conversion;

public enum CloudPlatform
{
    PC,
    Mobile
}

/// <summary>
/// File names in the PC Cloud folder and the mobile SAVE folder, plus the payloads of the small
/// bookkeeping files. Slots are 0-based: slot 0 is in-game slot 1.
/// </summary>
public static partial class CloudFiles
{
    public const string HeaderCache     = "LocalFileHeaderCache";
    public const string Timestamp       = "_CTN";
    public const string Ctrb            = "_CTRB";
    public const string CurrentSlot     = "_CurrentSlot";
    public const string CloudStorageIni = "CloudStorage.ini"; // PC only, plain text

    public const int MaxSlots = 5;

    public static string Save(int slot)          => $"_SwordSaveX_{slot}-0.bin";
    public static string Backup(int slot)        => $"_BackupX_{slot}-0.bin";
    public static string Summary(int slot)       => $"_SwordSaveSlotX_{slot}.bin";
    public static string BackupSummary(int slot) => $"_BackupSlotX_{slot}.bin";

    /// <summary>True for a character's save and summary files and their Backup copies.</summary>
    public static bool IsCharacterFile(string fileName) =>
        fileName.StartsWith("_SwordSave", StringComparison.Ordinal) || fileName.StartsWith("_Backup", StringComparison.Ordinal);

    /// <summary>True for the save and summary files, which mobile stores dpiZ compressed.</summary>
    public static bool IsCompressedOnMobile(string fileName) => IsCharacterFile(fileName);

    /// <summary>
    /// A player signed in to the game's online service gets their account ID (McpId) in front of every file name,
    /// for example "ea223a4e09b44bf594806c2e3b9d8846_SwordSaveX_0-0.bin" for "_SwordSaveX_0-0.bin". Returns the plain name.
    /// </summary>
    public static string PlainName(string fileName)
    {
        Match match = IdPrefixRegex().Match(fileName);
        return match.Success ? match.Groups["name"].Value : fileName;
    }

    /// <summary>The account ID in front of a file name, or null.</summary>
    public static string? IdPrefixOf(string fileName)
    {
        Match match = IdPrefixRegex().Match(fileName);
        return match.Success ? match.Groups["id"].Value : null;
    }

    public static int? SlotOfSave(string fileName)
    {
        Match match = SaveRegex().Match(fileName);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The _CTN payload. PC writes local time, mobile writes UTC; the caller picks.</summary>
    public static byte[] TimestampPayload(DateTime time) =>
        FStringBytes(time.ToString("yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture));

    public static byte[] TerminatorBytes => FStringBytes("None");

    public static byte[] Int32Payload(int value) => BitConverter.GetBytes(value);

    private static byte[] FStringBytes(string value)
    {
        using var stream = new UnrealStream(new MemoryStream());
        using var writer = new UnrealBinaryWriter(stream);
        writer.WriteUnrealString(value);
        writer.Flush();
        return stream.GetStreamBytes();
    }

    [GeneratedRegex(@"^_SwordSaveX_(\d+)-0\.bin$")]
    private static partial Regex SaveRegex();

    [GeneratedRegex(@"^(?<id>[0-9A-Fa-f]{32})(?<name>_.+)$")]
    private static partial Regex IdPrefixRegex();
}
