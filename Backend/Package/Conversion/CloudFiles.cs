using System.Globalization;
using System.Text.RegularExpressions;
using IBSaveEditor.UProperties;
using IBSaveEditor.Wrappers;

namespace IBSaveEditor.Package.Conversion;

public enum CloudPlatform
{
    PC,
    Mobile
}

/// <summary>
/// The files in the PC Cloud folder and the mobile SAVE folder: their names, and the tiny
/// payloads of the small bookkeeping files. Slot numbers are 0-based: slot 0 is in-game slot 1.
/// </summary>
public static partial class CloudFiles
{
    public const string HeaderCache     = "LocalFileHeaderCache";
    public const string Timestamp       = "_CTN";
    public const string Ctrb            = "_CTRB";
    public const string CurrentSlot     = "_CurrentSlot";
    public const string CloudStorageIni = "CloudStorage.ini"; // PC only, plain text

    /// <summary>A save folder holds at most this many characters, on PC and on Android.</summary>
    public const int MaxSlots = 5;

    public static string Save(int slot)          => $"_SwordSaveX_{slot}-0.bin";
    public static string Backup(int slot)        => $"_BackupX_{slot}-0.bin";
    public static string Summary(int slot)       => $"_SwordSaveSlotX_{slot}.bin";
    public static string BackupSummary(int slot) => $"_BackupSlotX_{slot}.bin";

    /// <summary>True for a character's files (save, summary and their Backup copies).</summary>
    public static bool IsCharacterFile(string fileName) =>
        fileName.StartsWith("_SwordSave", StringComparison.Ordinal) || fileName.StartsWith("_Backup", StringComparison.Ordinal);

    /// <summary>True for the save and summary files, which mobile stores dpiZ compressed.</summary>
    public static bool IsCompressedOnMobile(string fileName) => IsCharacterFile(fileName);

    /// <summary>Returns the slot number if the name is a main save file (_SwordSaveX_N-0.bin).</summary>
    public static int? SlotOfSave(string fileName)
    {
        Match match = SaveRegex().Match(fileName);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// The _CTN payload: one FString timestamp such as "2026.09.30-22.57.28".
    /// PC writes local time, mobile writes UTC; the caller picks.
    /// </summary>
    public static byte[] TimestampPayload(DateTime time) =>
        FStringBytes(time.ToString("yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture));

    /// <summary>The "None" FString that ends every UE3 property list, as raw bytes.</summary>
    public static byte[] TerminatorBytes => FStringBytes(UType.NONE);

    /// <summary>A single int32, as stored in _CurrentSlot and _CTRB.</summary>
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
}
