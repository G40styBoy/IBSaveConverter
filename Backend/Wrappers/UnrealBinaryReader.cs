using IBSaveEditor.UProperties;
using System.Text;

namespace IBSaveEditor.Wrappers;

// Wrapper for BinaryReader with methods tailored to Unreal Packages.
public class UnrealBinaryReader : BinaryReader
{
    public UnrealBinaryReader(UnrealStream stream) : base(stream.BaseStream) { }

    public UnrealBinaryReader(UnrealStream stream, Encoding encoding, bool leaveOpen) : base(stream.BaseStream, encoding, leaveOpen) { }

    private T Deserialize<T>(Func<T> reader, string errorMessage = "Failed to deserialize value.")
    {
        try { return reader(); }
        catch (Exception ex) when (
            ex is EndOfStreamException
            or IOException
            or ObjectDisposedException)
        {
            throw new InvalidDataException(errorMessage, ex);
        }
    }

    public int DeserializeIntValue() => Deserialize(ReadInt32);

    public uint DeserializeUInt() => Deserialize(ReadUInt32);

    public bool DeserializeBool() => Deserialize(ReadBoolean);

    public byte DeserializeByte() => Deserialize(ReadByte);

    // Reads the next string without consuming it.
    public string PeekString()
    {
        long originalPosition = BaseStream.Position;

        try
        {
            return DeserializeString();
        }
        finally
        {
            BaseStream.Position = originalPosition;
        }
    }

    public string DeserializeString()
    {
        try
        {
            var strLength = ReadInt32();
            if (strLength <= 0)
                return string.Empty;

            var bytes = ReadBytes(strLength);
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failure deserializing string inside an Unreal Package. {exception}");
        }
    }

    public object DeserializeByteProperty() => Deserialize(() =>
    {
        string enumName = DeserializeString();
        if (enumName == UType.NONE)
            return (object)DeserializeByte();

        string enumValue = DeserializeString();
        return new KeyValuePair<string, string>(enumName, enumValue);
    }, "Failed to deserialize ByteProperty from package stream.");

    // For property METADATA only (tag size, array index, entry count): clamps any
    // negative value other than -1 to int.MaxValue against corrupted/malformed input.
    // Use DeserializeIntValue for real IntProperty game data, where negatives must round-trip exactly.
    public int DeserializeInt() => Deserialize(() =>
    {
        int value = ReadInt32();
        return value < 0 && value != -1 ? int.MaxValue : value;
    }, "Failed to read Int32 from package stream.");

    public float DeserializeFloat() => Deserialize(() =>
    {
        float value = ReadSingle();
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidDataException($"Invalid float value read from stream: {value}");

        return value;
    }, "Failed to read Single (float) from package stream.");
}
