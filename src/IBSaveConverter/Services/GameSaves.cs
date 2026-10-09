using IBSaveEditor.Package;
using IBSaveEditor.Package.Conversion;

namespace IBSaveConverter.Services;

/// <summary>
/// One game's saves, as the shared screens see them: where they live, how to read them, and how characters are put
/// into them. The PC, Android and save file screens work the same for both games through this.
/// Everything here is synchronous; the view models run it off the UI thread.
/// </summary>
public abstract class GameSaves
{
    public static GameSaves Ib3 { get; } = new Ib3Saves();
    public static GameSaves Ib2 { get; } = new Ib2Saves();

    public abstract Game Game { get; }

    /// <summary>"Infinity Blade III".</summary>
    public abstract string Name { get; }

    /// <summary>"IB3", for file and folder names.</summary>
    public abstract string ShortName { get; }

    /// <summary>The kinds of loose .bin file the Save file source takes.</summary>
    public abstract string SaveFileHint { get; }

    public int MaxSlots => CloudFiles.MaxSlots;

    /// <summary>Documents\My Games\&lt;game&gt;\SwordGame\Cloud.</summary>
    public string DefaultPcFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", Name, "SwordGame", "Cloud");

    /// <summary>The PC folder as people know it, for hints and dialog titles.</summary>
    public string PcFolderHint => $"Documents\\My Games\\{Name}\\SwordGame\\Cloud";

    public string SuggestedZipName => $"{ShortName} Android Save Folder.zip";

    public abstract string? LastPcFolder(AppSettings settings);
    public abstract void RememberPcFolder(AppSettings settings, string folder);

    public abstract bool IsPcFolder(string? folder);
    public abstract LoadedSave ReadPcFolder(string folder);
    public abstract LoadedSave ReadAndroidZip(string zipPath);

    public CharacterInfo ReadSaveFile(string path) => new(-1, SingleSaveFile.Load(path, Game).Character, path);

    public void ExportDecrypted(LoadedSave save, string folder) => DecryptedExport.Write(save.Source, folder, save.DeviceId);

    /// <summary>Characters into one Android zip. <paramref name="keepFromZip"/>'s characters are kept; otherwise the zip starts at slot 1.</summary>
    public IReadOnlyList<ConversionResult> ToAndroid(IReadOnlyList<SaveSlotData> characters, string outputZip, string? keepFromZip)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputZip));
        if (directory is not null)
            Directory.CreateDirectory(directory);
        return WriteAndroid(characters, outputZip, keepFromZip);
    }

    /// <summary>Characters into PC slots, backed up first. Other PC characters are kept.</summary>
    public InstallResult AddToPc(IReadOnlyList<SlotPlacement> placements, string folder)
    {
        string? existing = IsPcFolder(folder) ? folder : null;
        return ConversionService.Install(folder, IsCharacterFile, removeOldCharacters: false, temp => WritePc(placements, existing, temp));
    }

    /// <summary>The PC ends up with exactly these characters. The old ones are removed after the backup.</summary>
    public InstallResult ReplacePc(IReadOnlyList<SlotPlacement> placements, int currentSlot, string folder)
    {
        string? existing = IsPcFolder(folder) ? folder : null;
        return ConversionService.Install(folder, IsCharacterFile, removeOldCharacters: true, temp => ReplaceOnPc(placements, currentSlot, existing, temp));
    }

    /// <summary>Previews which slots new characters would fill. Returns fewer than <paramref name="count"/> when the save is too full.</summary>
    public IReadOnlyList<int> PlanFreeSlots(IEnumerable<CharacterInfo> existing, int count)
    {
        var occupied = existing.Where(c => !c.IsDeleted).Select(c => c.Slot).ToHashSet();
        return Enumerable.Range(0, MaxSlots).Where(slot => !occupied.Contains(slot)).Take(count).ToList();
    }

    /// <summary>The character files in a PC folder, which a replace removes.</summary>
    protected abstract bool IsCharacterFile(string fileName);

    /// <summary>IB3 character files, in a PC Cloud folder or an iOS SAVE folder. Some have an account ID in front of their names.</summary>
    internal static bool Ib3IsCharacterFile(string fileName) => CloudFiles.IsCharacterFile(CloudFiles.PlainName(fileName));

    protected abstract IReadOnlyList<ConversionResult> WriteAndroid(IReadOnlyList<SaveSlotData> characters, string outputZip, string? keepFromZip);
    protected abstract IReadOnlyList<ConversionResult> WritePc(IReadOnlyList<SlotPlacement> placements, string? existingFolder, string outputFolder);
    protected abstract IReadOnlyList<ConversionResult> ReplaceOnPc(IReadOnlyList<SlotPlacement> placements, int currentSlot, string? existingFolder, string outputFolder);
}

/// <summary>Infinity Blade III: an indexed Cloud folder on PC, a dpiZ zip on Android. iOS has its own screens.</summary>
public sealed class Ib3Saves : GameSaves
{
    public override Game Game => Game.IB3;
    public override string Name => "Infinity Blade III";
    public override string ShortName => "IB3";
    public override string SaveFileHint => "Unencrypted, PC, Android or fixed-key iOS .bin files.";

    public override string? LastPcFolder(AppSettings settings) => settings.LastPcFolder;
    public override void RememberPcFolder(AppSettings settings, string folder) => settings.LastPcFolder = folder;

    public override bool IsPcFolder(string? folder) => ConversionService.IsSaveFolder(folder);
    public override LoadedSave ReadPcFolder(string folder) => ConversionService.Describe(PcCloudFolder.Load(folder));

    public override LoadedSave ReadAndroidZip(string zipPath)
    {
        var save = MobileSave.LoadZip(zipPath);
        return ConversionService.Describe(save, save.DeviceId);
    }

    protected override bool IsCharacterFile(string fileName) => Ib3IsCharacterFile(fileName);

    protected override IReadOnlyList<ConversionResult> WriteAndroid(IReadOnlyList<SaveSlotData> characters, string outputZip, string? keepFromZip) =>
        SaveConverter.CharactersToAndroid(characters, keepFromZip is null ? null : MobileSave.LoadZip(keepFromZip), outputZip);

    protected override IReadOnlyList<ConversionResult> WritePc(IReadOnlyList<SlotPlacement> placements, string? existingFolder, string outputFolder) =>
        SaveConverter.AddToPc(placements, existingFolder, outputFolder);

    protected override IReadOnlyList<ConversionResult> ReplaceOnPc(IReadOnlyList<SlotPlacement> placements, int currentSlot, string? existingFolder, string outputFolder) =>
        SaveConverter.ReplacePc(placements, currentSlot, existingFolder, outputFolder);
}

/// <summary>Infinity Blade II: the same files in a PC folder and an Android zip, no index, no compression.</summary>
public sealed class Ib2Saves : GameSaves
{
    public override Game Game => Game.IB2;
    public override string Name => "Infinity Blade II";
    public override string ShortName => "IB2";
    public override string SaveFileHint => "Unencrypted, PC, Android or iOS .bin files.";

    public override string? LastPcFolder(AppSettings settings) => settings.LastIb2PcFolder;
    public override void RememberPcFolder(AppSettings settings, string folder) => settings.LastIb2PcFolder = folder;

    public override bool IsPcFolder(string? folder) => Ib2SaveSet.IsSaveFolder(folder);
    public override LoadedSave ReadPcFolder(string folder) => ConversionService.Describe(Ib2SaveSet.LoadFolder(folder));
    public override LoadedSave ReadAndroidZip(string zipPath) => ConversionService.Describe(Ib2SaveSet.LoadZip(zipPath));

    protected override bool IsCharacterFile(string fileName) => Ib2SaveSet.IsCharacterFile(fileName);

    protected override IReadOnlyList<ConversionResult> WriteAndroid(IReadOnlyList<SaveSlotData> characters, string outputZip, string? keepFromZip) =>
        SaveConverter.Ib2CharactersToAndroid(characters, keepFromZip is null ? null : Ib2SaveSet.LoadZip(keepFromZip), outputZip);

    protected override IReadOnlyList<ConversionResult> WritePc(IReadOnlyList<SlotPlacement> placements, string? existingFolder, string outputFolder) =>
        SaveConverter.Ib2AddToPc(placements, existingFolder, outputFolder);

    protected override IReadOnlyList<ConversionResult> ReplaceOnPc(IReadOnlyList<SlotPlacement> placements, int currentSlot, string? existingFolder, string outputFolder) =>
        SaveConverter.Ib2ReplacePc(placements, currentSlot, existingFolder, outputFolder);
}
