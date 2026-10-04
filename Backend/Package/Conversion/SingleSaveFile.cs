using System.Buffers.Binary;
using System.Text;
using IBSaveEditor.Wrappers;

namespace IBSaveEditor.Package.Conversion;

/// <summary>What a single save file was stored as before loading.</summary>
public enum SingleSaveFormat
{
    /// <summary>The editor's unencrypted IB3 package: int32 version 5, then FF FF FF FF, then the properties.</summary>
    Unencrypted,
    /// <summary>A PC save file (_SwordSaveX_N-0.bin): "yeK " + AES with the IB3 key.</summary>
    PcEncrypted,
    /// <summary>An Android save file: "yeK " + AES with the Android device ID, then dpiZ compressed.</summary>
    AndroidEncrypted,
}

/// <summary>
/// One character's save file on its own, outside a Cloud folder or zip. A loose save has no slot summary,
/// so one is built from the save (<see cref="SlotSummaryBuilder"/>); the result can then go into a PC slot or
/// an Android save like any other character.
/// </summary>
public static class SingleSaveFile
{
    public static (SaveSlotData Character, SingleSaveFormat Format) Load(string path) => Load(File.ReadAllBytes(path));

    public static (SaveSlotData Character, SingleSaveFormat Format) Load(byte[] file)
    {
        var (save, format) = Unwrap(file);

        // An unencrypted IB2 save starts with the same header; only IB3 saves have CurrentEngineVersion.
        if (!SlotSummaryBuilder.IsIB3Save(save))
            throw new InvalidDataException("This isn't an Infinity Blade III save (it may be from another Infinity Blade game).");

        return (new SaveSlotData(save, SlotSummaryBuilder.FromSave(save)), format);
    }

    /// <summary>Returns the plain save payload (NO_MAGIC + properties + "None").</summary>
    private static (byte[] Save, SingleSaveFormat Format) Unwrap(byte[] file)
    {
        if (file.Length < 16)
            throw new InvalidDataException("This file is too small to be a save.");

        uint first = BitConverter.ToUInt32(file, 0), second = BitConverter.ToUInt32(file, 4);

        // Unencrypted: the 4-byte save version comes before the payload. Drop it.
        if (first == PackageConstants.SAVE_FILE_VERSION_IB3 && second == PackageConstants.NO_MAGIC)
            return (TrimPadding(file.AsSpan(4).ToArray()), SingleSaveFormat.Unencrypted);

        if (first != PackageConstants.IB3_SAVE_MAGIC)
            throw new InvalidDataException("This isn't an Infinity Blade III save file.");

        // Encrypted with the PC key?
        byte[] pc = PackageCrypto.DecryptIB3File(file);
        if (BitConverter.ToUInt32(pc, 0) == PackageConstants.NO_MAGIC)
            return (TrimPadding(pc), SingleSaveFormat.PcEncrypted);

        // Encrypted with the Android device ID and dpiZ compressed?
        byte[] android = PackageCrypto.DecryptIB3File(file, MobileTemplates.KeyFromDeviceId(MobileTemplates.FixedDeviceId));
        if (Dpiz.IsPacked(android))
            return (Dpiz.Unpack(android), SingleSaveFormat.AndroidEncrypted);

        throw new InvalidDataException("This save file couldn't be decrypted.");
    }

    /// <summary>
    /// Encrypted files are zero padded to 16 bytes and don't store their real length. The property list
    /// always ends with the "None" FString, whose own last byte is 0, so trailing zeros are cut back to it.
    /// </summary>
    private static byte[] TrimPadding(byte[] data)
    {
        int end = data.Length;
        while (end > 0 && data[end - 1] == 0)
            end--;
        return data.AsSpan(0, Math.Min(data.Length, end + 1)).ToArray();
    }
}

/// <summary>
/// Builds a slot summary (_SwordSaveSlotX file) from a save. Every summary field is a copy of a save field,
/// checked against real PC and Android saves:
///   CharacterName <- CharacterName            SaveFiles[0].CurrentMap   <- MapSaveName
///   SaveFiles[0].PawnLevel, GenerationCount, eCurrentPlayerType, MainGameFinishedCount, FightFinishedCount <- same name
///   SaveFiles[0].CurrentGold <- Currency[0].Current      SaveFiles[0].CurrentChips <- Currency[1].Current
/// A field is written only when the save has it, the same as the game does. CloudDocIndex can't be derived
/// and is left out (the original mobile-to-PC converter left it out too, and the PC game accepts that).
/// </summary>
public static class SlotSummaryBuilder
{
    /// <summary>True if the save has the CurrentEngineVersion property, which only IB3 saves have.</summary>
    public static bool IsIB3Save(byte[] save)
    {
        try { return ReadTopLevel(save).ContainsKey("CurrentEngineVersion"); }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidOperationException or InvalidDataException or ArgumentOutOfRangeException) { return false; }
    }

    public static byte[] FromSave(byte[] save)
    {
        var values = ReadTopLevel(save);
        if (values.GetValueOrDefault("CharacterName") is not string name)
            throw new InvalidDataException("The save has no character name.");

        using var stream = new UnrealStream(new MemoryStream());
        using var writer = new UnrealBinaryWriter(stream);

        writer.Write(PackageConstants.NO_MAGIC);
        WriteStr(writer, "CharacterName", name);

        byte[] meta = BuildMetadata(values);
        writer.WriteUnrealString("SaveFiles");
        writer.WriteUnrealString("StructProperty");
        writer.Write(meta.Length);
        writer.Write(0); // array index
        writer.WriteUnrealString("SaveFileMetaData");
        writer.Write(meta);

        writer.WriteUnrealString("None");
        writer.Flush();
        return stream.GetStreamBytes();
    }

    private static byte[] BuildMetadata(Dictionary<string, object> values)
    {
        using var stream = new UnrealStream(new MemoryStream());
        using var writer = new UnrealBinaryWriter(stream);

        if (values.GetValueOrDefault("MapSaveName") is string map)
            WriteStr(writer, "CurrentMap", map);
        WriteIntIfPresent(writer, values, "PawnLevel", "PawnLevel");
        WriteIntIfPresent(writer, values, "Currency[0].Current", "CurrentGold");
        WriteIntIfPresent(writer, values, "Currency[1].Current", "CurrentChips");
        WriteIntIfPresent(writer, values, "GenerationCount", "GenerationCount");
        if (values.GetValueOrDefault("eCurrentPlayerType") is (string enumType, string enumValue))
            WriteEnum(writer, "eCurrentPlayerType", enumType, enumValue);
        WriteIntIfPresent(writer, values, "MainGameFinishedCount", "MainGameFinishedCount");
        WriteIntIfPresent(writer, values, "FightFinishedCount", "FightFinishedCount");

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

    private static void WriteIntIfPresent(UnrealBinaryWriter writer, Dictionary<string, object> values, string from, string name)
    {
        if (values.GetValueOrDefault(from) is not int value)
            return;
        WriteTag(writer, name, "IntProperty", sizeof(int));
        writer.Write(value);
    }

    private static void WriteEnum(UnrealBinaryWriter writer, string name, string enumType, string value)
    {
        WriteTag(writer, name, "ByteProperty", FStringSize(value)); // size covers the value only
        writer.WriteUnrealString(enumType);
        writer.WriteUnrealString(value);
    }

    /// <summary>
    /// Reads the save's top-level values (and the first level inside structs, for Currency[n].Current).
    /// Skips every value by its stored size, so it works on any save, including properties the
    /// editor's array registry doesn't know.
    /// </summary>
    private static Dictionary<string, object> ReadTopLevel(byte[] save)
    {
        var values = new Dictionary<string, object>();
        using var stream = new UnrealStream(save, writable: false);
        using var reader = new UnrealBinaryReader(stream);
        reader.ReadUInt32(); // NO_MAGIC
        ReadList(reader, save.Length, values, prefix: null);
        return values;
    }

    private static void ReadList(UnrealBinaryReader reader, long end, Dictionary<string, object> values, string? prefix)
    {
        while (reader.BaseStream.Position < end)
        {
            string name = reader.DeserializeString();
            if (name == "None")
                return;

            string type = reader.DeserializeString();
            int size = reader.ReadInt32();
            int index = reader.ReadInt32();
            string key = prefix is null ? name : $"{prefix}.{name}";

            switch (type)
            {
                case "IntProperty":
                    values.TryAdd(key, reader.ReadInt32());
                    break;
                case "StrProperty":
                case "NameProperty":
                    long valueEnd = reader.BaseStream.Position + size;
                    values.TryAdd(key, reader.DeserializeString());
                    reader.BaseStream.Position = valueEnd;
                    break;
                case "BoolProperty":
                    reader.ReadByte(); // the value byte isn't counted in size
                    break;
                case "ByteProperty":
                    string enumType = reader.DeserializeString();
                    long byteEnd = reader.BaseStream.Position + size;
                    if (size > 1)
                        values.TryAdd(key, (enumType, reader.DeserializeString()));
                    reader.BaseStream.Position = byteEnd;
                    break;
                case "StructProperty":
                    reader.DeserializeString(); // struct type name
                    long structEnd = reader.BaseStream.Position + size;
                    if (prefix is null)
                        ReadList(reader, structEnd, values, $"{name}[{index}]");
                    reader.BaseStream.Position = structEnd;
                    break;
                default:
                    reader.BaseStream.Position += size;
                    break;
            }
        }
    }
}
