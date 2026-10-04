namespace IBSaveEditor.Package.Conversion;

public sealed record ConversionResult(string CharacterName, int PawnLevel, int SourceSlot, int TargetSlot, string OutputPath);

/// <summary>
/// Converts IB3 characters between the PC port and the Android port.
///
/// The save itself is identical on both platforms once decrypted (and decompressed on mobile),
/// so conversion never changes the save. It only unwraps from one platform and re-wraps for the other.
/// After writing, the output is loaded back and compared byte for byte with the input.
///
/// Slot numbers are 0-based: slot 0 is in-game slot 1.
/// </summary>
public static class SaveConverter
{
    /// <summary>
    /// Mobile zip -> PC Cloud folder.
    /// </summary>
    /// <param name="mobileZip">The Android port's export zip.</param>
    /// <param name="existingPcFolder">The user's current Cloud folder to merge into, or null to make a new one.
    /// Other slots in it are kept as they are.</param>
    /// <param name="outputFolder">Where to write the result. Must not be <paramref name="existingPcFolder"/>.</param>
    public static ConversionResult MobileToPc(string mobileZip, string? existingPcFolder, string outputFolder, int sourceSlot = 0, int targetSlot = 0)
    {
        SaveSlotData character = MobileSaveZip.Load(mobileZip).GetSlot(sourceSlot);
        EnsureNotDeleted(character, sourceSlot);

        PcCloudFolder pc = existingPcFolder is null ? PcCloudFolder.CreateNew() : PcCloudFolder.Load(existingPcFolder);
        pc.PutSlot(character, targetSlot, makeCurrent: true);
        pc.SaveTo(outputFolder);

        // Read the result back with the normal loader (checks every hash) and compare.
        SaveSlotData written = PcCloudFolder.Load(outputFolder).GetSlot(targetSlot);
        EnsureSame(character, written, "PC output");

        return new ConversionResult(character.Info.CharacterName, character.Info.PawnLevel, sourceSlot, targetSlot, outputFolder);
    }

    /// <summary>
    /// PC Cloud folder -> mobile zip. The character always lands in mobile slot 1 (index 0).
    /// The zip is encrypted with <paramref name="deviceId"/>; by default the fixed port ID every install uses.
    /// </summary>
    /// <param name="templateMobileZip">Optional: an existing mobile export to copy ib-port-saves.txt and the
    /// preferences plist from. Without it, built-in copies from a tested export are used.</param>
    /// <param name="deviceId">The Android device ID to encrypt with, or null for the fixed port ID.</param>
    public static ConversionResult PcToMobile(string pcFolder, string outputZip, int sourceSlot, string? templateMobileZip = null, string? deviceId = null)
    {
        SaveSlotData character = PcCloudFolder.Load(pcFolder).GetSlot(sourceSlot);
        EnsureNotDeleted(character, sourceSlot);

        MobileSaveZip mobile = templateMobileZip is null ? MobileSaveZip.CreateNew() : MobileSaveZip.Load(templateMobileZip);
        mobile.SetOnlyCharacter(character);
        mobile.SaveTo(outputZip, deviceId);

        SaveSlotData written = MobileSaveZip.Load(outputZip).GetSlot(0);
        EnsureSame(character, written, "mobile output");

        return new ConversionResult(character.Info.CharacterName, character.Info.PawnLevel, sourceSlot, 0, outputZip);
    }

    /// <summary>
    /// Several PC characters -> one mobile zip. The port's import replaces the phone's whole save,
    /// so everything the phone should end up with has to be in this zip.
    /// </summary>
    /// <param name="sourceSlots">PC slots to convert, in the order they should land.</param>
    /// <param name="keepFromMobileZip">Optional: a recent export from the phone. Its characters are kept and the
    /// PC characters go into its free slots (empty or deleted). Without it, the PC characters fill slots 1, 2, ...</param>
    /// <param name="deviceId">The Android device ID to encrypt with, or null for the fixed port ID.</param>
    /// <returns>One result per converted character; TargetSlot is the mobile slot it landed in.</returns>
    public static IReadOnlyList<ConversionResult> PcToMobile(string pcFolder, string outputZip, IReadOnlyList<int> sourceSlots,
                                                             string? keepFromMobileZip, string? deviceId)
    {
        if (sourceSlots.Count == 0)
            throw new ArgumentException("Pick at least one character.", nameof(sourceSlots));

        var pc = PcCloudFolder.Load(pcFolder);
        var characters = sourceSlots.Select(slot => (slot, data: pc.GetSlot(slot))).ToList();
        foreach (var (slot, data) in characters)
            EnsureNotDeleted(data, slot);

        var placed = CharactersToMobile(characters.Select(c => c.data).ToList(), outputZip, keepFromMobileZip, deviceId);
        return placed.Select((p, i) => p with { SourceSlot = characters[i].slot }).ToList();
    }

    /// <summary>
    /// Any characters (from a PC folder, an Android save or single save files) -> one mobile zip.
    /// With <paramref name="keepFromMobileZip"/> its characters are kept and these fill its free slots;
    /// otherwise they fill slots 1, 2, ... The zip is reloaded and every character compared byte for byte.
    /// </summary>
    /// <returns>One result per character; TargetSlot is the mobile slot it landed in, SourceSlot is -1.</returns>
    public static IReadOnlyList<ConversionResult> CharactersToMobile(IReadOnlyList<SaveSlotData> characters, string outputZip,
                                                                     string? keepFromMobileZip, string? deviceId)
    {
        if (characters.Count == 0)
            throw new ArgumentException("Pick at least one character.", nameof(characters));
        if (characters.Any(c => c.Info.IsDeleted))
            throw new InvalidOperationException("One of the characters is deleted.");

        MobileSaveZip mobile = keepFromMobileZip is null ? MobileSaveZip.CreateNew() : MobileSaveZip.Load(keepFromMobileZip);
        var targets = FreeMobileSlots(mobile, characters.Count);
        for (int i = 0; i < characters.Count; i++)
            mobile.PutSlot(characters[i], targets[i]);
        mobile.SetCurrentSlot(targets[0]);
        mobile.SaveTo(outputZip, deviceId);

        var written = MobileSaveZip.Load(outputZip);
        var results = new List<ConversionResult>();
        for (int i = 0; i < characters.Count; i++)
        {
            EnsureSame(characters[i], written.GetSlot(targets[i]), "mobile output");
            results.Add(new ConversionResult(characters[i].Info.CharacterName, characters[i].Info.PawnLevel, -1, targets[i], outputZip));
        }
        return results;
    }

    /// <summary>
    /// One character (from anywhere) -> a PC slot. Other slots in <paramref name="existingPcFolder"/> are kept;
    /// null makes a new save set. The output is reloaded and compared byte for byte.
    /// </summary>
    public static ConversionResult CharacterToPc(SaveSlotData character, string? existingPcFolder, string outputFolder, int targetSlot)
    {
        if (character.Info.IsDeleted)
            throw new InvalidOperationException("This character is deleted.");

        PcCloudFolder pc = existingPcFolder is null ? PcCloudFolder.CreateNew() : PcCloudFolder.Load(existingPcFolder);
        pc.PutSlot(character, targetSlot, makeCurrent: true);
        pc.SaveTo(outputFolder);

        EnsureSame(character, PcCloudFolder.Load(outputFolder).GetSlot(targetSlot), "PC output");
        return new ConversionResult(character.Info.CharacterName, character.Info.PawnLevel, -1, targetSlot, outputFolder);
    }

    /// <summary>
    /// The first <paramref name="count"/> slots that hold no live character.
    /// Throws if the save doesn't have that many free slots (a save holds <see cref="CloudFiles.MaxSlots"/>).
    /// </summary>
    public static IReadOnlyList<int> FreeMobileSlots(MobileSaveZip mobile, int count)
    {
        var free = Enumerable.Range(0, CloudFiles.MaxSlots).Where(slot => !mobile.HasLiveCharacter(slot)).Take(count).ToList();
        if (free.Count < count)
            throw new InvalidOperationException(
                $"An Android save holds {CloudFiles.MaxSlots} characters and this one only has {free.Count} free slot(s).");
        return free;
    }

    /// <summary>
    /// The whole mobile save -> PC: the PC ends up with exactly the zip's characters, in the same slots,
    /// with the same last-played slot. Characters already in <paramref name="existingPcFolder"/> are removed
    /// (its bookkeeping files are kept). Null makes a new save set.
    /// </summary>
    /// <returns>One result per character; SourceSlot and TargetSlot are the same slot.</returns>
    public static IReadOnlyList<ConversionResult> MobileToPcWhole(string mobileZip, string? existingPcFolder, string outputFolder)
    {
        var mobile = MobileSaveZip.Load(mobileZip);
        var characters = mobile.Slots.Select(slot => (slot, data: mobile.GetSlot(slot))).ToList();
        if (characters.Count == 0)
            throw new InvalidOperationException("The Android save has no characters.");

        PcCloudFolder pc = existingPcFolder is null ? PcCloudFolder.CreateNew() : PcCloudFolder.Load(existingPcFolder);
        pc.RemoveAllCharacters();
        foreach (var (slot, data) in characters)
            pc.PutSlot(data, slot, makeCurrent: false);
        pc.SetCurrentSlot(characters.Any(c => c.slot == mobile.CurrentSlot) ? mobile.CurrentSlot : characters[0].slot);
        pc.SaveTo(outputFolder);

        var written = PcCloudFolder.Load(outputFolder);
        if (!written.Slots.SequenceEqual(characters.Select(c => c.slot)))
            throw new InvalidDataException("Verification failed: the PC output has different slots than the Android save.");
        foreach (var (slot, data) in characters)
            EnsureSame(data, written.GetSlot(slot), "PC output");

        return characters.Select(c => new ConversionResult(c.data.Info.CharacterName, c.data.Info.PawnLevel, c.slot, c.slot, outputFolder)).ToList();
    }

    private static void EnsureNotDeleted(SaveSlotData character, int slot)
    {
        if (character.Info.IsDeleted)
            throw new InvalidOperationException($"Slot {slot + 1} holds a deleted character. Pick another slot.");
    }

    private static void EnsureSame(SaveSlotData expected, SaveSlotData actual, string what)
    {
        if (!expected.Save.AsSpan().SequenceEqual(actual.Save) || !expected.Summary.AsSpan().SequenceEqual(actual.Summary))
            throw new InvalidDataException($"Verification failed: the {what} does not match the original character.");
    }
}
