using IBSaveEditor.UProperties;

namespace IBSaveEditor.Package.Conversion;

/// <summary>
/// One character as plain bytes: the full save and its small slot summary.
/// Both are decrypted and decompressed, so they are identical on PC and mobile, and are the
/// same bytes <see cref="UnrealPackage.FromDecryptedIB3"/> accepts.
/// </summary>
public sealed class SaveSlotData
{
    public byte[] Save { get; }
    public byte[] Summary { get; }
    public SlotSummary Info { get; }

    public SaveSlotData(byte[] save, byte[] summary)
    {
        Save    = save;
        Summary = summary;
        Validate();
        Info    = SlotSummary.Read(summary);
    }

    /// <summary>
    /// A quick shape check: a decrypted IB3 save starts with the NO_MAGIC placeholder and ends with
    /// the "None" terminator. Deliberately not a full deserialize: a valid game-written save can hold
    /// properties the editor's array registry doesn't know yet, and that must not block a conversion
    /// that never changes the save's contents.
    /// </summary>
    private void Validate()
    {
        byte[] none = CloudFiles.TerminatorBytes;
        bool startsRight = Save.Length > sizeof(uint) + none.Length && BitConverter.ToUInt32(Save, 0) == PackageConstants.NO_MAGIC;
        if (!startsRight || !Save.AsSpan().EndsWith(none))
            throw new InvalidDataException("Save data does not look like an IB3 save (wrong key or damaged file).");
    }
}

/// <summary>
/// The few values from a slot summary that are useful for showing or picking a slot.
/// </summary>
public sealed record SlotSummary(string CharacterName, string CurrentMap, int PawnLevel, bool IsDeleted)
{
    private const string CHARACTER_NAME = "CharacterName";
    private const string SAVE_FILES     = "SaveFiles";
    private const string CURRENT_MAP    = "CurrentMap";
    private const string PAWN_LEVEL     = "PawnLevel";
    private const string DELETED        = "bDeleted";

    /// <summary>
    /// Reads the summary with the editor's own <see cref="Serialize.Deserializer"/>. The values come
    /// from the first SaveFiles entry (SaveFiles is a registered IB3 static array of SaveFileMetaData).
    /// </summary>
    public static SlotSummary Read(byte[] summary)
    {
        List<UProperty> properties;
        try
        {
            using var package = UnrealPackage.FromDecryptedIB3(summary, "SaveSlot");
            properties = package.ReadProperties();
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            throw new InvalidDataException($"Slot summary could not be read: {ex.Message}", ex);
        }

        var firstSaveFile = properties
            .OfType<UArrayProperty>()
            .FirstOrDefault(p => p.name == SAVE_FILES)?
            .elements.OfType<UStructProperty>()
            .FirstOrDefault(s => s.arrayIndex == 0)?
            .elements ?? new List<UProperty>();

        return new SlotSummary(
            Find<UStringProperty>(properties, CHARACTER_NAME)?.value ?? string.Empty,
            Find<UStringProperty>(firstSaveFile, CURRENT_MAP)?.value ?? string.Empty,
            Find<UIntProperty>(firstSaveFile, PAWN_LEVEL)?.value ?? 0,
            Find<UBoolProperty>(firstSaveFile, DELETED)?.value ?? false);
    }

    private static T? Find<T>(IEnumerable<UProperty> properties, string name) where T : UProperty =>
        properties.OfType<T>().FirstOrDefault(p => p.name == name);
}
