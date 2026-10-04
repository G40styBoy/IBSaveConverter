using System.Collections.ObjectModel;
using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels;

/// <summary>
/// PC Cloud folder -> Android save zip.
/// The Android game loads one zip holding the whole save, so the zip must contain every character the
/// phone should have: either a new save with just the ticked PC characters, or an existing Android save
/// with the PC characters added to its free slots.
/// Steps unlock in order: PC folder -> characters -> Android save -> save as.
/// </summary>
public sealed class PcToAndroidViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;

    private string _pcFolder = string.Empty;
    private string _folderNote = string.Empty;
    private bool? _addToExisting; // null until the user picks
    private string _existingZip = string.Empty;
    private string _existingNote = string.Empty;
    private IReadOnlyList<SlotInfo> _existingSlots = Array.Empty<SlotInfo>();
    private string _outputZip = string.Empty;
    private bool _outputChosenByUser;
    private bool _isBusy;
    private string? _lastOutput;
    private int _loadVersion, _existingVersion;
    private readonly List<SlotItem> _fileItems = new(); // single save files, listed after the PC characters

    public PcToAndroidViewModel(AppSettings settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;

        BrowsePcFolderCommand   = new AsyncCommand(BrowsePcFolderAsync, () => !IsBusy);
        ReloadCommand           = new AsyncCommand(LoadSlotsAsync, () => !IsBusy);
        AddSaveFileCommand      = new AsyncCommand(AddSaveFileAsync, () => !IsBusy);
        BrowseExistingCommand   = new AsyncCommand(BrowseExistingAsync, () => !IsBusy);
        BrowseOutputCommand     = new AsyncCommand(BrowseOutputAsync, () => !IsBusy);
        ConvertCommand          = new AsyncCommand(ConvertAsync, () => CanConvert);
        RevealOutputCommand     = new AsyncCommand(() => { if (_lastOutput is not null) _dialogs.Reveal(_lastOutput); return Task.CompletedTask; }, () => _lastOutput is not null);
    }

    public ObservableCollection<SlotItem> Slots { get; } = new();
    public ObservableCollection<PlannedSlot> Plan { get; } = new();
    public StatusMessage Status { get; } = new();

    public AsyncCommand BrowsePcFolderCommand { get; }
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand AddSaveFileCommand { get; }
    public AsyncCommand BrowseExistingCommand { get; }
    public AsyncCommand BrowseOutputCommand { get; }
    public AsyncCommand ConvertCommand { get; }
    public AsyncCommand RevealOutputCommand { get; }

    // ---------- Step: PC save folder ----------

    public string PcFolder
    {
        get => _pcFolder;
        set
        {
            if (SetField(ref _pcFolder, value))
                _ = LoadSlotsAsync();
        }
    }

    /// <summary>Only shown when something is wrong with the folder.</summary>
    public string FolderNote
    {
        get => _folderNote;
        private set
        {
            if (SetField(ref _folderNote, value))
                OnPropertyChanged(nameof(HasFolderNote));
        }
    }
    public bool HasFolderNote => FolderNote.Length > 0;

    /// <summary>The PC folder loaded and has at least one character.</summary>
    public bool IsFolderReady => Slots.Any(s => s.IsSelectable && !s.IsFromFile);

    /// <summary>The characters step unlocks with a loaded PC folder or at least one single save file.</summary>
    public bool CanPickCharacters => IsFolderReady || _fileItems.Count > 0;

    // ---------- Step: characters ----------

    public IReadOnlyList<SlotItem> CheckedSlots => Slots.Where(s => s.IsChecked).ToList();
    public bool AreCharactersReady => CanPickCharacters && CheckedSlots.Count > 0;

    // ---------- Step: Android save ----------

    public bool IsNewSave
    {
        get => _addToExisting == false;
        set { if (value) SetMode(false); }
    }

    public bool IsAddToExisting
    {
        get => _addToExisting == true;
        set { if (value) SetMode(true); }
    }

    public string ExistingZip
    {
        get => _existingZip;
        set
        {
            if (SetField(ref _existingZip, value))
                _ = LoadExistingAsync();
        }
    }

    /// <summary>Only shown when the existing zip can't be used.</summary>
    public string ExistingNote
    {
        get => _existingNote;
        private set
        {
            if (SetField(ref _existingNote, value))
                OnPropertyChanged(nameof(HasExistingNote));
        }
    }
    public bool HasExistingNote => ExistingNote.Length > 0;

    private bool IsExistingLoaded => _existingSlots.Count > 0 && File.Exists(ExistingZip);

    /// <summary>True when every ticked character has a slot in the planned Android save.</summary>
    private bool AllCharactersFit => Plan.Count(p => p.IsNew) == CheckedSlots.Count;

    public bool IsAndroidSaveReady =>
        AreCharactersReady && _addToExisting is not null && (_addToExisting == false || IsExistingLoaded) && AllCharactersFit;

    public bool HasPlan => Plan.Count > 0 && _addToExisting is not null && (_addToExisting == false || IsExistingLoaded);

    public string PlanTitle => IsAddToExisting ? "The Android save will now have:" : "The new Android save will have:";

    public string PlanWarning
    {
        get
        {
            if (_addToExisting is null || !AreCharactersReady || (IsAddToExisting && !IsExistingLoaded) || AllCharactersFit)
                return string.Empty;
            int free = Plan.Count(p => p.IsNew);
            int extra = CheckedSlots.Count - free;
            return free == 0
                ? $"A save folder can only have {ConversionService.MaxSlots} saves at a time, and this one is full. Start a new save instead."
                : $"A save folder can only have {ConversionService.MaxSlots} saves at a time. Untick {extra}.";
        }
    }
    public bool HasPlanWarning => PlanWarning.Length > 0;

    // ---------- Step: save as ----------

    public string OutputZip
    {
        get => _outputZip;
        set
        {
            if (SetField(ref _outputZip, value))
                Refresh();
        }
    }

    public bool CanConvert => !IsBusy && IsAndroidSaveReady && !string.IsNullOrWhiteSpace(OutputZip);

    // ---------- Busy ----------

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                Refresh();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    private string OutputFolder =>
        Path.GetDirectoryName(_outputZip) is { Length: > 0 } dir ? dir : _settings.LastZipFolder ?? SaveLocations.Desktop;

    /// <summary>Called once at startup: last used folder, or the game's default Cloud folder.</summary>
    public void Initialize()
    {
        PcFolder = _settings.LastPcFolder is { } last && Directory.Exists(last) ? last : SaveLocations.DefaultPcCloudFolder;
    }

    public async Task LoadSlotsAsync()
    {
        int version = ++_loadVersion;
        string folder = PcFolder;
        Slots.Clear();
        RestoreFileItems();
        Status.Clear();
        Rebuild();

        if (!ConversionService.IsPcCloudFolder(folder))
        {
            IsBusy = false; // an older load may still be running; this newer one wins
            FolderNote = Directory.Exists(folder)
                ? "No PC saves in this folder. Pick the SwordGame\\Cloud folder."
                : "Folder not found. Pick your SwordGame\\Cloud folder.";
            return;
        }

        IsBusy = true;
        try
        {
            var (slots, current) = await Task.Run(() => (ConversionService.ReadPcSlots(folder), ConversionService.ReadPcCurrentSlot(folder)));
            if (version != _loadVersion)
                return; // a newer load started while this one ran

            Slots.Clear();
            foreach (var slot in slots)
                Slots.Add(new SlotItem(slot, slot.Index == current, Rebuild));
            RestoreFileItems();

            FolderNote = slots.Any(s => !s.IsDeleted) ? string.Empty : "Every character in this folder is deleted.";

            // Start with the last played character ticked (unless save files are already ticked).
            if (CheckedSlots.Count == 0)
            {
                var first = Slots.FirstOrDefault(s => s.IsCurrent && s.IsSelectable) ?? Slots.FirstOrDefault(s => s.IsSelectable);
                if (first is not null)
                    first.IsChecked = true;
            }

            _settings.LastPcFolder = folder;
            _settings.Save();
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
                FolderNote = $"Couldn't read the saves in this folder: {ex.Message}";
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsBusy = false;
                Rebuild();
            }
        }
    }

    public async Task LoadExistingAsync()
    {
        int version = ++_existingVersion;
        string zip = ExistingZip;
        _existingSlots = Array.Empty<SlotInfo>();

        if (!File.Exists(zip))
        {
            ExistingNote = string.IsNullOrWhiteSpace(zip) ? string.Empty : "File not found.";
            Rebuild();
            return;
        }

        try
        {
            var info = await Task.Run(() => ConversionService.ReadMobileZip(zip));
            if (version != _existingVersion)
                return;

            _existingSlots = info.Slots;
            ExistingNote = info.Slots.Count == 0 ? "This Android save has no characters. Start a new save instead." : string.Empty;
            _settings.LastZipFolder = Path.GetDirectoryName(zip);
            _settings.Save();
        }
        catch (Exception ex)
        {
            if (version != _existingVersion)
                return;
            ExistingNote = $"Couldn't read this zip: {ex.Message}";
        }
        Rebuild();
    }

    private void SetMode(bool addToExisting)
    {
        if (_addToExisting == addToExisting)
            return;
        _addToExisting = addToExisting;
        OnPropertyChanged(nameof(IsNewSave));
        OnPropertyChanged(nameof(IsAddToExisting));
        OnPropertyChanged(nameof(PlanTitle));
        Rebuild();
    }

    /// <summary>Recomputes the preview, the suggested file name, and which steps are unlocked.</summary>
    private void Rebuild()
    {
        Plan.Clear();
        var picked = CheckedSlots;
        var kept = IsAddToExisting ? _existingSlots.Where(s => !s.IsDeleted).ToList() : new List<SlotInfo>();
        var targets = ConversionService.PlanFreeSlots(kept, picked.Count);

        var rows = kept.Select(s => PlannedSlot.From(s, s.Index, "Already in this Android save", isNew: false))
            .Concat(picked.Take(targets.Count).Select((p, i) => PlannedSlot.From(p.Info, targets[i], p.IsFromFile ? "From save file" : "From PC", isNew: true)))
            .OrderBy(r => r.Index);
        foreach (var row in rows)
            Plan.Add(row);

        if (!_outputChosenByUser && picked.Count > 0)
            OutputZip = Path.Combine(OutputFolder, SaveLocations.SuggestedZipName);

        Refresh();
    }


    private async Task BrowsePcFolderAsync()
    {
        string start = Directory.Exists(PcFolder) ? PcFolder : SaveLocations.DefaultPcCloudFolder;
        if (await _dialogs.PickFolderAsync("Choose your Infinity Blade III SwordGame\\Cloud folder", start) is { } folder)
            PcFolder = folder;
    }

    /// <summary>Adds a single save file (unencrypted, PC or Android encrypted) to the characters list, ticked.</summary>
    private async Task AddSaveFileAsync()
    {
        if (await _dialogs.PickSaveFileAsync("Choose a save file", _settings.LastZipFolder ?? SaveLocations.Desktop) is not { } path)
            return;
        await AddSaveFileAsync(path);
    }

    public async Task AddSaveFileAsync(string path)
    {
        try
        {
            var (info, character) = await Task.Run(() => ConversionService.LoadSingleSave(path));
            if (info.IsDeleted)
            {
                Status.Set(StatusKind.Error, "That save file holds a deleted character.");
                return;
            }

            SlotItem? item = null;
            item = new SlotItem(info, checkedChanged: Rebuild)
            {
                Character = character,
                RemoveCommand = new AsyncCommand(() => { RemoveFileItem(item!); return Task.CompletedTask; }),
            };
            _fileItems.Add(item);
            Slots.Add(item);
            item.IsChecked = true;
            Status.Clear();
            Rebuild();
        }
        catch (Exception ex)
        {
            Status.Set(StatusKind.Error, $"Couldn't use that save file: {ex.Message}");
        }
    }

    private void RemoveFileItem(SlotItem item)
    {
        _fileItems.Remove(item);
        Slots.Remove(item);
        Rebuild();
    }

    private void RestoreFileItems()
    {
        foreach (var item in _fileItems)
            Slots.Add(item);
    }

    private async Task BrowseExistingAsync()
    {
        if (await _dialogs.PickZipToOpenAsync("Choose the Android save to add to", _settings.LastZipFolder ?? SaveLocations.Desktop) is { } zip)
            ExistingZip = zip;
    }

    private async Task BrowseOutputAsync()
    {
        if (await _dialogs.PickZipToSaveAsync("Save the Android save as", SaveLocations.SuggestedZipName, OutputFolder) is { } path)
        {
            _outputChosenByUser = true;
            OutputZip = path;
        }
    }

    private async Task ConvertAsync()
    {
        if (!CanConvert)
            return;

        string folder = PcFolder, output = OutputZip;
        string? existing = IsAddToExisting ? ExistingZip : null;
        var slots = CheckedSlots.Where(s => !s.IsFromFile).Select(s => s.Index).ToList();
        var files = CheckedSlots.Where(s => s.IsFromFile).Select(s => s.Character!).ToList();
        int total = Plan.Count;

        IsBusy = true;
        Status.Set(StatusKind.Info, "Converting...");
        try
        {
            var results = await Task.Run(() => ConversionService.ToAndroid(folder, slots, files, output, existing));
            _lastOutput = output;
            _settings.LastZipFolder = Path.GetDirectoryName(output);
            _settings.Save();

            string what = total == 1 ? "1 character" : $"{total} characters";
            Status.Set(StatusKind.Success,
                $"Done. {Path.GetFileName(output)} holds {what}. Load it in the Android game; it replaces the save that's on the phone now.");
        }
        catch (Exception ex)
        {
            Status.Set(StatusKind.Error, $"Conversion failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Refresh()
    {
        foreach (string name in new[] { nameof(IsFolderReady), nameof(CanPickCharacters), nameof(AreCharactersReady), nameof(IsAndroidSaveReady), nameof(CanConvert),
                                        nameof(HasPlan), nameof(PlanWarning), nameof(HasPlanWarning) })
            OnPropertyChanged(name);
        BrowsePcFolderCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        AddSaveFileCommand.RaiseCanExecuteChanged();
        BrowseExistingCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        ConvertCommand.RaiseCanExecuteChanged();
        RevealOutputCommand.RaiseCanExecuteChanged();
    }
}
