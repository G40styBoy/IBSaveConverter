namespace IBSaveEditor.Package.Conversion;

public sealed record ConversionResult(string CharacterName, int PawnLevel, int Slot, string OutputPath);

/// <summary>A character and the slot it goes into.</summary>
public sealed record SlotPlacement(SaveSlotData Character, int Slot);

/// <summary>
/// Converts characters between save sets. A save is identical on every platform of a game once decrypted (and
/// decompressed on IB3 mobile), so conversion only re-wraps it. The three moves (fill free slots, put in chosen
/// slots, replace everyone) work on any <see cref="ISaveSet"/>; the methods below them say which set is read and how
/// it is written. Every write is read back and compared byte for byte. Slots are 0-based.
/// </summary>
public static class SaveConverter
{
    // ---------- IB3 ----------

    /// <summary>
    /// Characters into one Android zip. The port's import replaces the phone's whole save, so the zip must hold
    /// every character the phone should end up with.
    /// </summary>
    /// <param name="keepFrom">A save whose characters are kept; the new ones fill its free slots.
    /// Null fills slots 1, 2, ... of a new save.</param>
    public static IReadOnlyList<ConversionResult> CharactersToAndroid(IReadOnlyList<SaveSlotData> characters, MobileSave? keepFrom,
                                                                      string outputZip) =>
        FillFreeSlots(characters, keepFrom ?? MobileSave.CreateNew(), outputZip,
            mobile => mobile.SaveTo(outputZip, MobileTemplates.AndroidDeviceId),
            () => MobileSave.LoadZip(outputZip));

    /// <summary>Characters into an iOS SAVE folder, encrypted with the fixed key or, if given, a device ID.</summary>
    /// <param name="keepFrom">As for <see cref="CharactersToAndroid"/>.</param>
    public static IReadOnlyList<ConversionResult> CharactersToIos(IReadOnlyList<SaveSlotData> characters, MobileSave? keepFrom,
                                                                  string outputFolder, string? deviceId) =>
        FillFreeSlots(characters, keepFrom ?? MobileSave.CreateNew(), outputFolder,
            mobile => mobile.SaveToFolder(outputFolder, deviceId),
            () =>
            {
                var written = MobileSave.LoadFolder(outputFolder, deviceId is null ? Array.Empty<string>() : new[] { deviceId });
                if (written.DeviceId != deviceId?.Trim())
                    throw new InvalidDataException("Verification failed: the iOS output isn't encrypted with the key that was picked.");
                return written;
            });

    /// <summary>
    /// Characters into PC slots. Other slots in <paramref name="existingPcFolder"/> are kept; null makes a new save set.
    /// The game opens the first placed slot next.
    /// </summary>
    /// <param name="outputFolder">Must not be <paramref name="existingPcFolder"/>.</param>
    public static IReadOnlyList<ConversionResult> AddToPc(IReadOnlyList<SlotPlacement> placements, string? existingPcFolder, string outputFolder) =>
        PutInSlots(placements, LoadPc(existingPcFolder), outputFolder, pc => pc.SaveTo(outputFolder), () => PcCloudFolder.Load(outputFolder));

    /// <summary>
    /// Replaces every character in <paramref name="existingPcFolder"/>: the PC ends up with exactly these characters.
    /// Its bookkeeping files are kept; null makes a new save set.
    /// </summary>
    public static IReadOnlyList<ConversionResult> ReplacePc(IReadOnlyList<SlotPlacement> placements, int currentSlot,
                                                            string? existingPcFolder, string outputFolder) =>
        ReplaceAll(placements, currentSlot, LoadPc(existingPcFolder), outputFolder, pc => pc.SaveTo(outputFolder), () => PcCloudFolder.Load(outputFolder));

    private static PcCloudFolder LoadPc(string? folder) => folder is null ? PcCloudFolder.CreateNew() : PcCloudFolder.Load(folder);

    // ---------- IB2 (PC folder and Android zip hold the same files) ----------

    /// <summary>As <see cref="CharactersToAndroid"/>, for Infinity Blade II.</summary>
    public static IReadOnlyList<ConversionResult> Ib2CharactersToAndroid(IReadOnlyList<SaveSlotData> characters, Ib2SaveSet? keepFrom,
                                                                         string outputZip) =>
        FillFreeSlots(characters, keepFrom ?? Ib2SaveSet.CreateNew(), outputZip,
            save => save.SaveToZip(outputZip), () => Ib2SaveSet.LoadZip(outputZip));

    /// <summary>As <see cref="AddToPc"/>, for Infinity Blade II.</summary>
    public static IReadOnlyList<ConversionResult> Ib2AddToPc(IReadOnlyList<SlotPlacement> placements, string? existingPcFolder, string outputFolder) =>
        PutInSlots(placements, LoadIb2Pc(existingPcFolder), outputFolder, save => save.SaveToFolder(outputFolder), () => Ib2SaveSet.LoadFolder(outputFolder));

    /// <summary>As <see cref="ReplacePc"/>, for Infinity Blade II.</summary>
    public static IReadOnlyList<ConversionResult> Ib2ReplacePc(IReadOnlyList<SlotPlacement> placements, int currentSlot,
                                                               string? existingPcFolder, string outputFolder) =>
        ReplaceAll(placements, currentSlot, LoadIb2Pc(existingPcFolder), outputFolder, save => save.SaveToFolder(outputFolder), () => Ib2SaveSet.LoadFolder(outputFolder));

    private static Ib2SaveSet LoadIb2Pc(string? folder) => folder is null ? Ib2SaveSet.CreateNew() : Ib2SaveSet.LoadFolder(folder);

    // ---------- The three moves ----------

    /// <summary>The first <paramref name="count"/> slots with no live character. Throws if there aren't enough.</summary>
    public static IReadOnlyList<int> FreeSlots(ISaveSet save, int count)
    {
        var free = Enumerable.Range(0, CloudFiles.MaxSlots).Where(slot => !save.HasLiveCharacter(slot)).Take(count).ToList();
        if (free.Count < count)
            throw new InvalidOperationException(
                $"A save holds {CloudFiles.MaxSlots} characters and this one only has {free.Count} free slot(s).");
        return free;
    }

    // Characters into the first free slots of the target. The first one becomes the current slot.
    private static IReadOnlyList<ConversionResult> FillFreeSlots<T>(IReadOnlyList<SaveSlotData> characters, T target, string output,
                                                                    Action<T> write, Func<T> readBack) where T : ISaveSet
    {
        if (characters.Count == 0)
            throw new ArgumentException("Pick at least one character.", nameof(characters));
        var targets = FreeSlots(target, characters.Count);
        return PutInSlots(characters.Select((c, i) => new SlotPlacement(c, targets[i])).ToList(), target, output, write, readBack);
    }

    // Characters into the given slots; other slots are kept. The first placed slot becomes the current slot.
    private static IReadOnlyList<ConversionResult> PutInSlots<T>(IReadOnlyList<SlotPlacement> placements, T target, string output,
                                                                 Action<T> write, Func<T> readBack) where T : ISaveSet
    {
        EnsureValid(placements);
        foreach (var placement in placements)
            target.PutSlot(placement.Character, placement.Slot);
        target.SetCurrentSlot(placements[0].Slot);
        write(target);
        return Verify(placements, readBack(), output);
    }

    // The target ends up with exactly these characters. Its other files are kept.
    private static IReadOnlyList<ConversionResult> ReplaceAll<T>(IReadOnlyList<SlotPlacement> placements, int currentSlot, T target, string output,
                                                                 Action<T> write, Func<T> readBack) where T : ISaveSet
    {
        EnsureValid(placements);
        target.RemoveAllCharacters();
        foreach (var placement in placements)
            target.PutSlot(placement.Character, placement.Slot);
        target.SetCurrentSlot(placements.Any(p => p.Slot == currentSlot) ? currentSlot : placements[0].Slot);
        write(target);

        var written = readBack();
        if (!written.Slots.SequenceEqual(placements.Select(p => p.Slot).Order()))
            throw new InvalidDataException("Verification failed: the output has different slots than planned.");
        return Verify(placements, written, output);
    }

    private static IReadOnlyList<ConversionResult> Verify(IReadOnlyList<SlotPlacement> placements, ICharacterSource written, string output)
    {
        foreach (var placement in placements)
            EnsureSame(placement.Character, written.GetSlot(placement.Slot));
        return placements.Select(p => new ConversionResult(p.Character.Info.DisplayName, p.Character.Info.PawnLevel, p.Slot, output)).ToList();
    }

    private static void EnsureValid(IReadOnlyList<SlotPlacement> placements)
    {
        if (placements.Count == 0)
            throw new ArgumentException("Pick at least one character.", nameof(placements));
        if (placements.Select(p => p.Slot).Distinct().Count() != placements.Count)
            throw new ArgumentException("Two characters can't go into the same slot.", nameof(placements));
        if (placements.Any(p => p.Character.Info.IsDeleted))
            throw new InvalidOperationException("One of the characters is deleted.");
    }

    private static void EnsureSame(SaveSlotData expected, SaveSlotData actual)
    {
        if (!expected.Save.AsSpan().SequenceEqual(actual.Save) || !expected.Summary.AsSpan().SequenceEqual(actual.Summary))
            throw new InvalidDataException("Verification failed: the output does not match the original character.");
    }
}
