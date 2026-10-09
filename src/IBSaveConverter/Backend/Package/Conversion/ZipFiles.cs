using System.IO.Compression;

namespace IBSaveEditor.Package.Conversion;

/// <summary>Small zip helpers shared by the mobile export readers and writers.</summary>
internal static class ZipFiles
{
    /// <summary>Zip paths with forward slashes, whatever wrote the zip.</summary>
    public static string Normalize(string path) => path.Replace('\\', '/');

    public static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static void Write(ZipArchive zip, string path, byte[] data)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(data);
    }
}
