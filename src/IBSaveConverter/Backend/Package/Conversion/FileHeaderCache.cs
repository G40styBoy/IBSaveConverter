using System.Security.Cryptography;
using IBSaveEditor.Wrappers;

namespace IBSaveEditor.Package.Conversion;

internal sealed class CacheEntry
{
    /// <summary>Mobile only. Meaning unknown; always 0.</summary>
    public int MobileValueA { get; set; }

    /// <summary>Mobile only. Meaning unknown; 0 on _SwordSave* files, 1 on the rest.</summary>
    public int MobileValueB { get; set; }

    public string Hash { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }

    public string FileName => System.IO.Path.GetFileName(Path.Replace('\\', '/'));
}

/// <summary>
/// LocalFileHeaderCache: the index of every cloud file with its SHA-1 and size. Both games check it.
/// Layout after decryption (FLocalFileCache::WriteLocalFileHeaderCache in the iOS binary):
///   int32 version (2), FString LastMcpId, the cached remote file list, the local file list.
///   Each list is an int32 count, then the entries.
///   PC entry:     FString sha1, FString path, FString name, int64 size
///   Mobile entry: int32 A, int32 B, then the same four fields
/// Hash and size cover the decrypted payload, which is still dpiZ compressed on mobile.
/// LastMcpId is the player's online account ID. It is empty unless they signed in, and then the game also puts it
/// in front of every file name. The remote list has always been empty in real files; it is read with the entry
/// layout and written back unchanged.
/// </summary>
internal sealed class FileHeaderCache
{
    private const int VERSION = 2;
    private const string PC_PATH_PREFIX     = @"..\..\SwordGame\Cloud\";
    private const string MOBILE_PATH_PREFIX = @"SAVE\";

    public CloudPlatform Platform { get; }
    public List<CacheEntry> Entries { get; } = new();

    /// <summary>The player's online account ID, or empty. Files are named McpId + their plain name.</summary>
    public string McpId { get; set; } = string.Empty;

    private List<CacheEntry> _remoteEntries = new();

    public FileHeaderCache(CloudPlatform platform) => Platform = platform;

    /// <summary>Used to test whether a key decrypted the file.</summary>
    public static bool LooksValid(byte[] plain) => plain.Length >= 16 && BitConverter.ToInt32(plain, 0) == VERSION;

    public static string Sha1Hex(byte[] data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

    public static FileHeaderCache Load(byte[] encryptedFile, CloudPlatform platform, byte[]? keyOverride = null)
    {
        byte[] plain = PackageCrypto.DecryptIB3File(encryptedFile, keyOverride);
        if (!LooksValid(plain))
            throw new InvalidDataException("LocalFileHeaderCache has an unknown version (wrong key or damaged file).");

        var cache = new FileHeaderCache(platform);
        using var stream = new UnrealStream(plain, writable: false);
        using var reader = new UnrealBinaryReader(stream);

        reader.ReadInt32(); // version
        cache.McpId = reader.DeserializeString();
        cache._remoteEntries = ReadEntries(reader, platform);
        cache.Entries.AddRange(ReadEntries(reader, platform));
        return cache;
    }

    private static List<CacheEntry> ReadEntries(UnrealBinaryReader reader, CloudPlatform platform)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > 10_000)
            throw new InvalidDataException($"LocalFileHeaderCache lists {count} files, which can't be right.");

        var entries = new List<CacheEntry>(count);
        for (int i = 0; i < count; i++)
        {
            var entry = new CacheEntry();
            if (platform is CloudPlatform.Mobile)
            {
                entry.MobileValueA = reader.ReadInt32();
                entry.MobileValueB = reader.ReadInt32();
            }
            entry.Hash = reader.DeserializeString();
            entry.Path = reader.DeserializeString();
            entry.Name = reader.DeserializeString();
            entry.Size = reader.ReadInt64();
            entries.Add(entry);
        }
        return entries;
    }

    private void WriteEntries(UnrealBinaryWriter writer, IReadOnlyList<CacheEntry> entries)
    {
        writer.Write(entries.Count);
        foreach (var entry in entries)
        {
            if (Platform is CloudPlatform.Mobile)
            {
                writer.Write(entry.MobileValueA);
                writer.Write(entry.MobileValueB);
            }
            writer.WriteUnrealString(entry.Hash);
            writer.WriteUnrealString(entry.Path);
            writer.WriteUnrealString(entry.Name);
            writer.Write(entry.Size);
        }
    }

    public byte[] ToEncryptedFile(byte[]? keyOverride = null)
    {
        using var stream = new UnrealStream(new MemoryStream());
        using var writer = new UnrealBinaryWriter(stream);

        writer.Write(VERSION);
        writer.WriteUnrealString(McpId);
        WriteEntries(writer, _remoteEntries);
        WriteEntries(writer, Entries);

        writer.Flush();
        return PackageCrypto.EncryptIB3File(stream.GetStreamBytes(), keyOverride);
    }

    /// <summary>Decrypts a file, cuts it to its cached size and checks its hash. Returns the bytes the hash covers.</summary>
    public static byte[] ReadListedFile(CacheEntry entry, byte[] encryptedFile, byte[]? keyOverride = null)
    {
        byte[] padded = PackageCrypto.DecryptIB3File(encryptedFile, keyOverride);
        if (entry.Size < 0 || entry.Size > padded.Length)
            throw new InvalidDataException($"{entry.FileName}: cached size {entry.Size} does not fit the {padded.Length}-byte file.");

        byte[] payload = padded.AsSpan(0, (int)entry.Size).ToArray();
        if (Sha1Hex(payload) != entry.Hash)
            throw new InvalidDataException($"{entry.FileName} does not match its LocalFileHeaderCache hash.");

        return payload;
    }

    /// <param name="fileName">The plain name, without the McpId in front.</param>
    public CacheEntry? Find(string fileName) => Entries.FirstOrDefault(e => CloudFiles.PlainName(e.FileName) == fileName);

    public void Remove(string fileName) => Entries.RemoveAll(e => CloudFiles.PlainName(e.FileName) == fileName);

    public void Set(string fileName, byte[] hashedBytes)
    {
        var entry = Find(fileName);
        if (entry is null)
        {
            entry = new CacheEntry
            {
                Path = (Platform is CloudPlatform.PC ? PC_PATH_PREFIX : MOBILE_PATH_PREFIX) + McpId + fileName,
                Name = fileName.TrimStart('_'),
                MobileValueA = 0,
                MobileValueB = fileName.StartsWith("_SwordSave", StringComparison.Ordinal) ? 0 : 1,
            };
            Entries.Add(entry);
        }

        entry.Hash = Sha1Hex(hashedBytes);
        entry.Size = hashedBytes.LongLength;
    }
}
