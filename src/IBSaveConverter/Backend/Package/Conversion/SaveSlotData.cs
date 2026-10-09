namespace IBSaveEditor.Package.Conversion;

/// <summary>One character as decrypted, decompressed bytes: the full save and its slot summary.</summary>
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

    // Checks the start and end markers only. A full deserialize would reject valid saves that hold
    // properties the array registry doesn't know, and conversion never changes the contents.
    private void Validate()
    {
        byte[] none = CloudFiles.TerminatorBytes;
        bool startsRight = Save.Length > sizeof(uint) + none.Length && BitConverter.ToUInt32(Save, 0) == PackageConstants.NO_MAGIC;
        if (!startsRight || !Save.AsSpan().EndsWith(none))
            throw new InvalidDataException("Save data does not look like an IB3 save (wrong key or damaged file).");
    }
}

/// <summary>The slot summary values shown when picking a slot. IB2 and IB3 summaries share these fields.</summary>
public sealed record SlotSummary(string CharacterName, string CurrentMap, int PawnLevel, bool IsDeleted, int? UpdateSaveCount)
{
    // CurrentMap, PawnLevel and bDeleted come from the first SaveFiles entry.
    private const string FIRST_SAVE_FILE = "SaveFiles[0].";

    /// <summary>The name to show. IB2 mobile saves have no name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(CharacterName) ? "Unnamed character" : CharacterName;

    public static SlotSummary Read(byte[] summary)
    {
        Dictionary<string, object> values;
        try
        {
            values = SaveProperties.ReadTopLevel(summary);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException($"Slot summary could not be read: {ex.Message}", ex);
        }

        return new SlotSummary(
            values.GetValueOrDefault("CharacterName") as string ?? string.Empty,
            values.GetValueOrDefault(FIRST_SAVE_FILE + "CurrentMap") as string ?? string.Empty,
            values.GetValueOrDefault(FIRST_SAVE_FILE + "PawnLevel") as int? ?? 0,
            values.GetValueOrDefault(FIRST_SAVE_FILE + "bDeleted") as bool? ?? false,
            values.GetValueOrDefault("UpdateSaveCount") as int?);
    }
}
