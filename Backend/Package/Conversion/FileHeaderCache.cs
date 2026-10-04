using System.Security.Cryptography;
using IBSaveEditor.Wrappers;

namespace IBSaveEditor.Package.Conversion;

/// <summary>
/// One file listed in LocalFileHeaderCache.
/// </summary>
internal sealed class CacheEntry
{
    /// <summary>Mobile only: first unknown int32 (always 0 so far, may be the slot).</summary>
    public int MobileValueA { get; set; }

    /// <summary>Mobile only: second unknown int32 (0 on _SwordSave*, 1 on everything else).</summary>
    public int MobileValueB { get; set; }

    public string Hash { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }

    /// <summary>The file name on disk. The cache stores names without the leading underscore.</summary>
    public string FileName => System.IO.Path.GetFileName(Path.Replace('\\', '/'));
}

/// <summary>
/// LocalFileHeaderCache: the index of every cloud file with its SHA-1 and size. Both games check it.
///
/// Layout (after decryption): int32 version (2), 8 reserved bytes, int32 count, then the entries.
///   PC entry:     FString sha1, FString path, FString name, int64 size
///   Mobile entry: int32 A, int32 B, then the same four fields
///
/// What the hash and size cover:
///   PC:     the decrypted payload
///   Mobile: the decrypted payload BEFORE dpiZ decompression (the compressed bytes)
///
/// All strings in the cache are plain ASCII (hex hashes and file paths), so the editor's
/// <see cref="UnrealBinaryReader"/>/<see cref="UnrealBinaryWriter"/> string handling fits as is.
/// </summary>
internal sealed class FileHeaderCache
{
    private const int VERSION = 2;
    private const string PC_PATH_PREFIX     = @"..\..\SwordGame\Cloud\";
    private const string MOBILE_PATH_PREFIX = @"SAVE\";

    public CloudPlatform Platform { get; }
    public List<CacheEntry> Entries { get; } = new();
    private byte[] _reserved = new byte[8];

    public FileHeaderCache(CloudPlatform platform) => Platform = platform;

    /// <summary>True if the decrypted bytes start like a cache. Used to test a key.</summary>
    public static bool LooksValid(byte[] plain) => plain.Length >= 16 && BitConverter.ToInt32(plain, 0) == VERSION;

    /// <summary>Lowercase hex SHA-1, the format the cache stores.</summary>
    public static string Sha1Hex(byte[] data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

    /// <summary>Decrypts and parses an encrypted LocalFileHeaderCache file.</summary>
    public static FileHeaderCache Load(byte[] encryptedFile, CloudPlatform platform, byte[]? keyOverride = null)
    {
        byte[] plain = PackageCrypto.DecryptIB3File(encryptedFile, keyOverride);
        if (!LooksValid(plain))
            throw new InvalidDataException("LocalFileHeaderCache has an unknown version (wrong key or damaged file).");

        var cache = new FileHeaderCache(platform);
        using var stream = new UnrealStream(plain, writable: false);
        using var reader = new UnrealBinaryReader(stream);

        reader.ReadInt32(); // version
        cache._reserved = reader.ReadBytes(8);
        int count = reader.ReadInt32();

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
            cache.Entries.Add(entry);
        }
        return cache;
    }

    /// <summary>Serializes and encrypts the cache.</summary>
    public byte[] ToEncryptedFile(byte[]? keyOverride = null)
    {
        using var stream = new UnrealStream(new MemoryStream());
        using var writer = new UnrealBinaryWriter(stream);

        writer.Write(VERSION);
        writer.Write(_reserved);
        writer.Write(Entries.Count);

        foreach (var entry in Entries)
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

        writer.Flush();
        return PackageCrypto.EncryptIB3File(stream.GetStreamBytes(), keyOverride);
    }

    /// <summary>
    /// Decrypts one listed file, cuts it to its cached size, and checks it against its cached hash.
    /// Returns the exact bytes the hash covers (still dpiZ compressed for mobile save files).
    /// </summary>
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

    public CacheEntry? Find(string fileName) => Entries.FirstOrDefault(e => e.FileName == fileName);

    public void Remove(string fileName) => Entries.RemoveAll(e => e.FileName == fileName);

    /// <summary>
    /// Updates the entry for a file (or appends a new one) from the exact bytes the cache must describe.
    /// </summary>
    public void Set(string fileName, byte[] hashedBytes)
    {
        var entry = Find(fileName);
        if (entry is null)
        {
            entry = new CacheEntry
            {
                Path = (Platform is CloudPlatform.PC ? PC_PATH_PREFIX : MOBILE_PATH_PREFIX) + fileName,
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
