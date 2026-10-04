namespace IBSaveEditor.Wrappers;

// Wrapper for Stream with methods tailored to Unreal package traversal.
public sealed class UnrealStream : IDisposable
{
    private readonly Stream _stream;
    public Stream BaseStream => _stream;

    public UnrealStream(Stream stream) => _stream = stream;

    public UnrealStream(string path, FileMode mode, FileAccess access)
        => _stream = new FileStream(path, mode, access);

    public UnrealStream(string path, FileMode mode, FileAccess access, FileShare share)
        => _stream = new FileStream(path, mode, access, share);

    public UnrealStream(byte[] data, bool writable = true)
        => _stream = new MemoryStream(data, writable);

    public long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public long Length => _stream.Length;

    public bool IsEndOfFile => _stream.Position >= _stream.Length;

    // Copy of the underlying stream's bytes; leaves the stream's position unchanged.
    public byte[] GetStreamBytes()
    {
        if (_stream is null)
            throw new InvalidOperationException("Stream is not initialized.");

        if (_stream is MemoryStream ms)
            return ms.ToArray();

        long originalPosition = 0;

        if (_stream.CanSeek)
        {
            originalPosition = _stream.Position;
            _stream.Position = 0;
        }

        using var copyStream = new MemoryStream();
        _stream.CopyTo(copyStream);

        if (_stream.CanSeek)
            _stream.Position = originalPosition;

        return copyStream.ToArray();
    }

    public void SetPosition(long position)
    {
        if (!_stream.CanSeek)
            throw new NotSupportedException("Stream is not seekable.");
        if (position < 0 || position > _stream.Length)
            throw new ArgumentOutOfRangeException(nameof(position));
        _stream.Position = position;
    }

    public void ResetPosition() => _stream.Position = 0;

    // Reverts the position after reading a string property (size prefix + null terminator + content).
    public void RevertStringPosition(string value)
    {
        _stream.Position -= sizeof(int) + sizeof(byte);
        _stream.Position -= value.Length;
    }

    public void Dispose() => _stream.Dispose();
}
