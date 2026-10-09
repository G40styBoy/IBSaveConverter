namespace IBSaveEditor.Package.Conversion;

/// <summary>A save set that characters can be read from: a PC Cloud folder, an Android zip or an iOS SAVE folder.</summary>
public interface ICharacterSource
{
    IReadOnlyList<int> Slots { get; }
    int CurrentSlot { get; }
    SaveSlotData GetSlot(int slot);

    /// <summary>Every listed file, decrypted and decompressed, by file name.</summary>
    IReadOnlyDictionary<string, byte[]> Files { get; }
}

/// <summary>A save set that characters can also be written into. <see cref="SaveConverter"/> works on any of them.</summary>
public interface ISaveSet : ICharacterSource
{
    /// <summary>Puts a character and its Backup copies in a slot. Sets the current slot only if there is none yet.</summary>
    void PutSlot(SaveSlotData data, int slot);

    void RemoveAllCharacters();

    void SetCurrentSlot(int slot);

    bool HasLiveCharacter(int slot) => Slots.Contains(slot) && !GetSlot(slot).Info.IsDeleted;
}
