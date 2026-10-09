using IBSaveEditor.Wrappers;

namespace IBSaveEditor.Package.Conversion;

/// <summary>
/// A light reader for the property list in a save or slot summary (IB2 and IB3 use the same layout). It reads
/// top-level values and one level into structs, and skips everything else by its stored size, so properties no
/// registry knows about don't matter.
/// Keys: "Name", "Name[i]" for a static array element other than 0, and "Struct[i].Field" inside a struct.
/// Values: int, string, bool, or (enumType, value) for an enum.
/// </summary>
internal static class SaveProperties
{
    /// <summary>Reads a payload that starts with the 4-byte NO_MAGIC placeholder.</summary>
    public static Dictionary<string, object> ReadTopLevel(byte[] payload)
    {
        var values = new Dictionary<string, object>();
        using var stream = new UnrealStream(payload, writable: false);
        using var reader = new UnrealBinaryReader(stream);
        reader.ReadUInt32(); // NO_MAGIC
        ReadList(reader, payload.Length, values, prefix: null);
        return values;
    }

    /// <summary>
    /// Where the top-level property list that starts at <paramref name="start"/> ends: just past its "None".
    /// Anything after it is padding. Throws if the list runs off the end.
    /// </summary>
    public static int EndOfList(byte[] data, int start)
    {
        using var stream = new UnrealStream(data, writable: false);
        using var reader = new UnrealBinaryReader(stream);
        reader.BaseStream.Position = start;
        if (!ReadList(reader, data.Length, new Dictionary<string, object>(), prefix: null))
            throw new InvalidDataException("The save ends before its property list does.");
        return (int)reader.BaseStream.Position;
    }

    public static bool IsIB3Save(byte[] payload)
    {
        try { return ReadTopLevel(payload).ContainsKey("CurrentEngineVersion"); }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidOperationException or InvalidDataException or ArgumentOutOfRangeException) { return false; }
    }

    // Returns true when the list ended with "None".
    private static bool ReadList(UnrealBinaryReader reader, long end, Dictionary<string, object> values, string? prefix)
    {
        while (reader.BaseStream.Position < end)
        {
            string name = reader.DeserializeString();
            if (name == "None")
                return true;

            string type = reader.DeserializeString();
            int size = reader.ReadInt32();
            int index = reader.ReadInt32();
            string key = prefix is not null ? $"{prefix}.{name}" : index == 0 ? name : $"{name}[{index}]";

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
                    values.TryAdd(key, reader.ReadByte() != 0); // the value byte isn't counted in size
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
        return false;
    }
}
