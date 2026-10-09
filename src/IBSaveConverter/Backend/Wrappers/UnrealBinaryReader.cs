using System.Text;

namespace IBSaveEditor.Wrappers;

/// <summary>A BinaryReader that also reads Unreal's length-prefixed strings (FString).</summary>
public class UnrealBinaryReader : BinaryReader
{
    public UnrealBinaryReader(UnrealStream stream) : base(stream.BaseStream) { }

    /// <summary>An int32 length that counts the closing zero byte, then UTF-8 text. A length of 0 or less is an empty string.</summary>
    public string DeserializeString()
    {
        try
        {
            int length = ReadInt32();
            if (length <= 0)
                return string.Empty;

            return Encoding.UTF8.GetString(ReadBytes(length)).TrimEnd('\0');
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("A text value in the save runs past the end of the file.", ex);
        }
    }
}
