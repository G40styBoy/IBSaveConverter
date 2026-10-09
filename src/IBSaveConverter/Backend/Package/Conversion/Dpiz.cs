using System.Buffers.Binary;
using System.IO.Compression;

namespace IBSaveEditor.Package.Conversion;

/// <summary>
/// The mobile compression wrapper: "dpiZ", a uint32 uncompressed size, then a zlib stream.
/// The port always writes the last Adler-32 byte as 0, so <see cref="ZLibStream"/> rejects its files.
/// Reading inflates the raw deflate data and skips the checksum; writing zeroes that byte to match.
/// </summary>
internal static class Dpiz
{
    private static readonly byte[] Magic = "dpiZ"u8.ToArray();
    private const int SIZE_OFFSET = 4;
    private const int ZLIB_OFFSET = 8;
    private const int DEFLATE_OFFSET = 10; // after the 2-byte zlib header

    public static bool IsPacked(ReadOnlySpan<byte> data) => data.Length > DEFLATE_OFFSET && data[..Magic.Length].SequenceEqual(Magic);

    public static byte[] Unpack(byte[] packed)
    {
        if (!IsPacked(packed))
            throw new InvalidDataException("Data is not dpiZ compressed.");

        int size = BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(SIZE_OFFSET));
        if (size < 0)
            throw new InvalidDataException($"Invalid dpiZ size {size}.");

        using var input = new MemoryStream(packed, DEFLATE_OFFSET, packed.Length - DEFLATE_OFFSET);
        using var inflate = new DeflateStream(input, CompressionMode.Decompress);

        var result = new byte[size];
        try
        {
            inflate.ReadExactly(result);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("dpiZ data ended before the stored size was reached.");
        }
        return result;
    }

    public static byte[] Pack(byte[] data)
    {
        using var output = new MemoryStream();
        output.Write(Magic);
        output.Write(BitConverter.GetBytes(data.Length));

        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);

        byte[] packed = output.ToArray();
        if (packed.Length <= ZLIB_OFFSET)
            throw new InvalidOperationException("zlib produced no output.");

        packed[^1] = 0; // copy the port's broken checksum byte
        return packed;
    }
}
