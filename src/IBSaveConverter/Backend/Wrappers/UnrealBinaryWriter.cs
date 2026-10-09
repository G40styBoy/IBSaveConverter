using System.Text;

namespace IBSaveEditor.Wrappers;

/// <summary>A BinaryWriter that also writes Unreal's length-prefixed strings (FString).</summary>
public class UnrealBinaryWriter : BinaryWriter
{
    public UnrealBinaryWriter(UnrealStream stream) : base(stream.BaseStream) { }

    public void WriteUnrealString(string str)
    {
        if (str == string.Empty)
        {
            Write(0);
            return;
        }

        byte[] strBytes = Encoding.UTF8.GetBytes(str);
        Write(strBytes.Length + sizeof(byte)); // string size + null terminator
        Write(strBytes);
        Write((byte)0); // null terminator
    }
}
