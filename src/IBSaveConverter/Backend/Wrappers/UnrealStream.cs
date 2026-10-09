namespace IBSaveEditor.Wrappers;

/// <summary>A stream that the Unreal reader and writer share.</summary>
public sealed class UnrealStream : IDisposable
{
    public Stream BaseStream { get; }

    public UnrealStream(Stream stream) => BaseStream = stream;

    public UnrealStream(byte[] data, bool writable = true) => BaseStream = new MemoryStream(data, writable);

    /// <summary>Everything written so far. The position is left where it was.</summary>
    public byte[] GetStreamBytes() => ((MemoryStream)BaseStream).ToArray();

    public void Dispose() => BaseStream.Dispose();
}
