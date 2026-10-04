using IBSaveEditor.UProperties;
using System.Text;

namespace IBSaveEditor.Wrappers;

// Wrapper for BinaryWriter with methods tailored to Unreal Packages.
public class UnrealBinaryWriter : BinaryWriter
{
    public UnrealBinaryWriter(UnrealStream stream) : base(stream.BaseStream) { }

    // Writes a string without BinaryWriter's default 7-bit-encoded length prefix.
    public void WriteUnrealString(string str)
    {
        if (str == string.Empty)
        {
            Write(0);
            return;
        }

        byte[] strBytes = Encoding.UTF8.GetBytes(str);
        Write(str.Length + sizeof(byte)); // string size + null terminator
        Write(strBytes);
        Write((byte)0); // null terminator
    }

    public void WritePropertyMetadata(UProperty property)
    {
        WriteUnrealString(property.name);
        WriteUnrealString(property.type);
        Write(property.size);
        Write(property.arrayIndex);
    }
}
