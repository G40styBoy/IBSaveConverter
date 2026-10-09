using System.Security.Cryptography;

namespace IBSaveEditor.Package;

/// <summary>The AES layer every IB2 and IB3 save file is wrapped in.</summary>
internal static class PackageCrypto
{
    // Credits to Hox for the keys and the idea to initialize them in UTF8. Thanks!!
    private static readonly byte[] IB2AES = "|FK}S];v]!!cw@E4l-gMXa9yDPvRfF*B"u8.ToArray();
    private static readonly byte[] IB3AES = "6nHmjd:hbWNf=9|UO2:?;K0y+gZL-jP5"u8.ToArray();

    private const int BLOCK_SIZE = 16;

    /// <summary>
    /// Decrypts the IB3 file layout: the 4-byte IB3 magic, then AES-ECB data. Saves and the cloud
    /// bookkeeping files all use it, and mobile uses it with a different key.
    /// </summary>
    /// <param name="keyOverride">Replaces the IB3 key (mobile).</param>
    /// <returns>The decrypted bytes, including zero padding.</returns>
    public static byte[] DecryptIB3File(byte[] file, byte[]? keyOverride = null) =>
        DecryptCloudFile(file, PackageConstants.IB3_SAVE_MAGIC, keyOverride ?? IB3AES);

    /// <summary>The reverse of <see cref="DecryptIB3File"/>: zero pads, encrypts and prepends the magic.</summary>
    public static byte[] EncryptIB3File(byte[] payload, byte[]? keyOverride = null) =>
        EncryptCloudFile(payload, PackageConstants.IB3_SAVE_MAGIC, keyOverride ?? IB3AES);

    /// <summary>The IB2 file layout: the 4-byte IB2 magic, then AES-ECB with the IB2 key, on every platform.</summary>
    /// <returns>The decrypted bytes, including padding.</returns>
    public static byte[] DecryptIB2File(byte[] file) => DecryptCloudFile(file, PackageConstants.IB2_SAVE_MAGIC, IB2AES);

    /// <summary>The reverse of <see cref="DecryptIB2File"/>.</summary>
    public static byte[] EncryptIB2File(byte[] payload) => EncryptCloudFile(payload, PackageConstants.IB2_SAVE_MAGIC, IB2AES);

    private static byte[] DecryptCloudFile(byte[] file, uint magic, byte[] key)
    {
        if (file.Length < sizeof(uint) || BitConverter.ToUInt32(file, 0) != magic)
            throw new InvalidDataException("File does not start with the save magic.");
        if ((file.Length - sizeof(uint)) % BLOCK_SIZE != 0)
            throw new InvalidDataException("Encrypted data is not a multiple of 16 bytes.");

        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptEcb(file.AsSpan(sizeof(uint)), PaddingMode.None);
    }

    private static byte[] EncryptCloudFile(byte[] payload, uint magic, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;

        byte[] encrypted = aes.EncryptEcb(payload, PaddingMode.Zeros);
        var file = new byte[sizeof(uint) + encrypted.Length];
        BitConverter.GetBytes(magic).CopyTo(file, 0);
        encrypted.CopyTo(file, sizeof(uint));
        return file;
    }
}
