using System.Text;
using IBSaveEditor.Wrappers;

namespace IBSaveEditor.Package.Conversion;

public enum SingleSaveFormat
{
    /// <summary>int32 version 5 (or 0), then FF FF FF FF, then the properties. Both games.</summary>
    Unencrypted,
    /// <summary>IB3: "yeK " header, AES with the IB3 key.</summary>
    PcEncrypted,
    /// <summary>IB3: "yeK " header, AES with the Android device ID, then dpiZ compressed.</summary>
    AndroidEncrypted,
    /// <summary>IB3: "yeK " header, AES with the IB3 key (the iOS fixed-key patch), then dpiZ compressed.</summary>
    IosFixedKey,
    /// <summary>IB2: IB2 header, AES with the IB2 key. PC, Android and iOS all write it the same way.</summary>
    Ib2Encrypted,
}

/// <summary>
/// Loads one character's save file from outside a save folder or zip. A loose save has no slot summary,
/// so <see cref="SlotSummaryBuilder"/> builds one.
/// </summary>
public static class SingleSaveFile
{
    private const string WRONG_GAME_IB2 = "This is an Infinity Blade II save. Go back and pick Infinity Blade II.";
    private const string WRONG_GAME_IB3 = "This is an Infinity Blade III save. Go back and pick Infinity Blade III.";

    public static (SaveSlotData Character, SingleSaveFormat Format) Load(string path, Game game) => Load(File.ReadAllBytes(path), game);

    public static (SaveSlotData Character, SingleSaveFormat Format) Load(byte[] file, Game game)
    {
        if (file.Length < 16)
            throw new InvalidDataException("This file is too small to be a save.");

        var (save, format) = game is Game.IB3 ? UnwrapIB3(file) : UnwrapIB2(file);

        // Unencrypted IB2 and IB3 saves share a header; only IB3 saves have CurrentEngineVersion.
        if (SaveProperties.IsIB3Save(save) != (game is Game.IB3))
            throw new InvalidDataException(game is Game.IB3 ? WRONG_GAME_IB2 : WRONG_GAME_IB3);

        return (new SaveSlotData(save, SlotSummaryBuilder.FromSave(save, game)), format);
    }

    /// <summary>The unencrypted file format: the save version, then the plain save.</summary>
    public static byte[] ToUnencrypted(byte[] save)
    {
        var file = new byte[sizeof(int) + save.Length];
        BitConverter.GetBytes(PackageConstants.UNENCRYPTED_SAVE_VERSION).CopyTo(file, 0);
        save.CopyTo(file, sizeof(int));
        return file;
    }

    private static (byte[] Save, SingleSaveFormat Format) UnwrapIB3(byte[] file)
    {
        if (IsUnencrypted(file))
            return (Unencrypted(file), SingleSaveFormat.Unencrypted);

        uint first = BitConverter.ToUInt32(file, 0);
        if (first == PackageConstants.IB2_SAVE_MAGIC)
            throw new InvalidDataException(WRONG_GAME_IB2);
        if (first != PackageConstants.IB3_SAVE_MAGIC)
            throw new InvalidDataException("This isn't an Infinity Blade III save file.");

        // PC and fixed-key iOS share the IB3 key; iOS compresses the save, PC doesn't.
        byte[] fixedKey = PackageCrypto.DecryptIB3File(file);
        if (BitConverter.ToUInt32(fixedKey, 0) == PackageConstants.NO_MAGIC)
            return (UpToEnd(fixedKey, 0), SingleSaveFormat.PcEncrypted);
        if (Dpiz.IsPacked(fixedKey))
            return (Dpiz.Unpack(fixedKey), SingleSaveFormat.IosFixedKey);

        byte[] android = PackageCrypto.DecryptIB3File(file, MobileTemplates.KeyFromDeviceId(MobileTemplates.AndroidDeviceId));
        if (Dpiz.IsPacked(android))
            return (Dpiz.Unpack(android), SingleSaveFormat.AndroidEncrypted);

        throw new InvalidDataException("This save file couldn't be decrypted.");
    }

    private static (byte[] Save, SingleSaveFormat Format) UnwrapIB2(byte[] file)
    {
        if (IsUnencrypted(file))
            return (Unencrypted(file), SingleSaveFormat.Unencrypted);

        uint first = BitConverter.ToUInt32(file, 0);
        if (first == PackageConstants.IB3_SAVE_MAGIC)
            throw new InvalidDataException(WRONG_GAME_IB3);
        if (first != PackageConstants.IB2_SAVE_MAGIC)
            throw new InvalidDataException("This isn't an Infinity Blade II save file.");

        return (Ib2SaveSet.Unwrap(file), SingleSaveFormat.Ib2Encrypted);
    }

    // Also takes a version of 0, which is what a decrypted IB2 file starts with.
    private static bool IsUnencrypted(byte[] file) =>
        BitConverter.ToInt32(file, 0) is PackageConstants.UNENCRYPTED_SAVE_VERSION or 0 &&
        BitConverter.ToUInt32(file, sizeof(int)) == PackageConstants.NO_MAGIC;

    // Drops the 4-byte save version.
    private static byte[] Unencrypted(byte[] file) => UpToEnd(file, sizeof(int));

    /// <summary>
    /// The save that starts at <paramref name="start"/> (with its NO_MAGIC placeholder), cut where its property list
    /// ends. Encrypted files are padded and don't store their length.
    /// </summary>
    internal static byte[] UpToEnd(byte[] data, int start) =>
        data[start..SaveProperties.EndOfList(data, start + sizeof(uint))];
}

/// <summary>
/// Builds a slot summary (the SwordSaveSlotX file) from a save. Each field copies a save field, checked against real saves.
/// Both games:  CharacterName &lt;- CharacterName (IB2 Android saves have none)   SaveFiles[0].CurrentMap &lt;- MapSaveName
/// IB3:  SaveFiles[0].PawnLevel, GenerationCount, eCurrentPlayerType, MainGameFinishedCount, FightFinishedCount &lt;- same name,
///       CurrentGold &lt;- Currency[0].Current, CurrentChips &lt;- Currency[1].Current
/// IB2:  SaveFiles[0].PawnLevel, CurrentGold, GenerationCount, UnlockedNewGamePlus &lt;- same name; UpdateSaveCount = 1
/// A field is written only when the save has it. CloudDocIndex can't be derived and is left out; the PC game accepts that.
/// </summary>
public static class SlotSummaryBuilder
{
    public static byte[] FromSave(byte[] save, Game game)
    {
        var values = SaveProperties.ReadTopLevel(save);
        string? name = values.GetValueOrDefault("CharacterName") as string;
        if (name is null && game is Game.IB3)
            throw new InvalidDataException("The save has no character name.");

        using var stream = new UnrealStream(new MemoryStream());
        using var writer = new UnrealBinaryWriter(stream);

        writer.Write(PackageConstants.NO_MAGIC);
        if (name is not null)
            WriteStr(writer, "CharacterName", name);

        byte[] meta = game is Game.IB3 ? BuildIB3Metadata(values) : BuildIB2Metadata(values);
        writer.WriteUnrealString("SaveFiles");
        writer.WriteUnrealString("StructProperty");
        writer.Write(meta.Length);
        writer.Write(0); // array index
        writer.WriteUnrealString("SaveFileMetaData");
        writer.Write(meta);

        // IB2 counts saves here, and the game's cloud bookkeeping keeps the same number per slot.
        if (game is Game.IB2)
            WriteInt(writer, "UpdateSaveCount", 1);

        writer.WriteUnrealString("None");
        writer.Flush();
        return stream.GetStreamBytes();
    }

    private static byte[] BuildIB3Metadata(Dictionary<string, object> values) => BuildMetadata(values, writer =>
    {
        WriteIntIfPresent(writer, values, "PawnLevel", "PawnLevel");
        WriteIntIfPresent(writer, values, "Currency[0].Current", "CurrentGold");
        WriteIntIfPresent(writer, values, "Currency[1].Current", "CurrentChips");
        WriteIntIfPresent(writer, values, "GenerationCount", "GenerationCount");
        if (values.GetValueOrDefault("eCurrentPlayerType") is (string enumType, string enumValue))
            WriteEnum(writer, "eCurrentPlayerType", enumType, enumValue);
        WriteIntIfPresent(writer, values, "MainGameFinishedCount", "MainGameFinishedCount");
        WriteIntIfPresent(writer, values, "FightFinishedCount", "FightFinishedCount");
    });

    private static byte[] BuildIB2Metadata(Dictionary<string, object> values) => BuildMetadata(values, writer =>
    {
        WriteIntIfPresent(writer, values, "PawnLevel", "PawnLevel");
        WriteIntIfPresent(writer, values, "CurrentGold", "CurrentGold");
        WriteIntIfPresent(writer, values, "GenerationCount", "GenerationCount");
        WriteIntIfPresent(writer, values, "UnlockedNewGamePlus", "UnlockedNewGamePlus");
    });

    private static byte[] BuildMetadata(Dictionary<string, object> values, Action<UnrealBinaryWriter> writeFields)
    {
        using var stream = new UnrealStream(new MemoryStream());
        using var writer = new UnrealBinaryWriter(stream);

        if (values.GetValueOrDefault("MapSaveName") is string map)
            WriteStr(writer, "CurrentMap", map);
        writeFields(writer);

        writer.WriteUnrealString("None");
        writer.Flush();
        return stream.GetStreamBytes();
    }

    private static void WriteTag(UnrealBinaryWriter writer, string name, string type, int size)
    {
        writer.WriteUnrealString(name);
        writer.WriteUnrealString(type);
        writer.Write(size);
        writer.Write(0); // array index
    }

    private static int FStringSize(string value) => sizeof(int) + Encoding.UTF8.GetByteCount(value) + 1;

    private static void WriteStr(UnrealBinaryWriter writer, string name, string value)
    {
        WriteTag(writer, name, "StrProperty", FStringSize(value));
        writer.WriteUnrealString(value);
    }

    private static void WriteInt(UnrealBinaryWriter writer, string name, int value)
    {
        WriteTag(writer, name, "IntProperty", sizeof(int));
        writer.Write(value);
    }

    private static void WriteIntIfPresent(UnrealBinaryWriter writer, Dictionary<string, object> values, string from, string name)
    {
        if (values.GetValueOrDefault(from) is int value)
            WriteInt(writer, name, value);
    }

    private static void WriteEnum(UnrealBinaryWriter writer, string name, string enumType, string value)
    {
        WriteTag(writer, name, "ByteProperty", FStringSize(value)); // size covers the value only
        writer.WriteUnrealString(enumType);
        writer.WriteUnrealString(value);
    }
}
