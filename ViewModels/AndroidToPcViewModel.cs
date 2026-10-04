using System.Collections.ObjectModel;
using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels;

/// <summary>
/// Android save zip -> the PC Cloud folder, installed in place with a backup first. Either the whole
/// Android save replaces the PC's characters, or one character goes into one PC slot.
/// Steps unlock in order: Android save -> PC folder -> what to bring over.
/// </summary>
public sealed class AndroidToPcViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;

    private string _zipPath = string.Empty;
    private string _zipNote = string.Empty;
    private string _pcFolder = string.Empty;
    private string _pcNote = string.Empty;
    private bool _pcReadable;
    private bool? _wholeSave; // null until the user picks
    private SlotItem? _selectedSlot;
    private TargetSlotItem? _selectedTarget;
    private bool _isBusy;
    private string? _lastBackup;
    private IReadOnlyList<SlotInfo> _zipSlots = Array.Empty<SlotInfo>();
    private IReadOnlyList<SlotInfo> _pcSlots = Array.Empty<SlotInfo>();
    private int _zipVersion, _pcVersion;
    private SlotItem? _fileItem; // set when the source is a single save file instead of a zip

    public AndroidToPcViewModel(AppSettings settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;

        BrowseZipCommand      = new AsyncCommand(BrowseZipAsync, () => !IsBusy);
        ChooseSaveFileCommand = new AsyncCommand(ChooseSaveFileAsync, () => !IsBusy);
        BrowsePcFolderCommand = new AsyncCommand(BrowsePcFolderAsync, () => !IsBusy);
        InstallCommand        = new AsyncCommand(InstallAsync, () => CanInstall);
        RevealBackupCommand   = new AsyncCommand(() => { if (_lastBackup is not null) _dialogs.Reveal(_lastBackup); return Task.CompletedTask; }, () => _lastBackup is not null);
    }

    public ObservableCollection<SlotItem> Slots { get; } = new();
    public ObservableCollection<TargetSlotItem> TargetSlots { get; } = new();
    public ObservableCollection<PlannedSlot> Plan { get; } = new();
    public StatusMessage Status { get; } = new();

    public AsyncCommand BrowseZipCommand { get; }
    public AsyncCommand ChooseSaveFileCommand { get; }
    public AsyncCommand BrowsePcFolderCommand { get; }
    public AsyncCommand InstallCommand { get; }
    public AsyncCommand RevealBackupCommand { get; }

    // ---------- Step: Android save ----------

    public string ZipPath
    {
        get => _zipPath;
        set
        {
            if (!SetField(ref _zipPath, value))
                return;
            if (_fileItem is not null && !string.IsNullOrWhiteSpace(value))
                ClearFileSource();
            _ = LoadZipAsync();
        }
    }

    /// <summary>True when the character comes from a single save file instead of an Android zip.</summary>
    public bool IsFromFile => _fileItem is not null;

    public string FileSourceText => _fileItem is { Info.SourceFile: { } path } ? $"Using save file: {Path.GetFileName(path)}" : string.Empty;

    /// <summary>"Whole save" only makes sense for an Android zip.</summary>
    public bool CanBringWholeSave => !IsFromFile;

    /// <summary>Only shown when the zip can't be used.</summary>
    public string ZipNote
    {
        get => _zipNote;
        private set
        {
            if (SetField(ref _zipNote, value))
                OnPropertyChanged(nameof(HasZipNote));
        }
    }
    public bool HasZipNote => ZipNote.Length > 0;

    public bool IsZipReady => _zipSlots.Any(s => !s.IsDeleted) || IsFromFile;

    // ---------- Step: PC save folder ----------

    public string PcFolder
    {
        get => _pcFolder;
        set
        {
            if (SetField(ref _pcFolder, value))
                _ = LoadPcFolderAsync();
        }
    }

    /// <summary>Only shown for a new or unreadable folder.</summary>
    public string PcNote
    {
        get => _pcNote;
        private set
        {
            if (SetField(ref _pcNote, value))
                OnPropertyChanged(nameof(HasPcNote));
        }
    }
    public bool HasPcNote => PcNote.Length > 0;

    public bool IsPcStepEnabled => IsZipReady;
    public bool IsPcReady => IsZipReady && _pcReadable;

    // ---------- Step: what to bring over ----------

    public bool IsWholeSave
    {
        get => _wholeSave == true;
        set { if (value && !IsFromFile) SetMode(true); }
    }

    public bool IsOneCharacter
    {
        get => _wholeSave == false;
        set { if (value) SetMode(false); }
    }

    public string WholeSaveLabel
    {
        get
        {
            int count = _zipSlots.Count(s => !s.IsDeleted);
            return count switch
            {
                0 => "The whole Android save",
                1 => "The whole Android save (1 character)",
                _ => $"The whole Android save ({count} characters)",
            };
        }
    }

    public SlotItem? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (SetField(ref _selectedSlot, value))
                Rebuild();
        }
    }

    public TargetSlotItem? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetField(ref _selectedTarget, value))
                Rebuild();
        }
    }

    public bool HasPlan => Plan.Count > 0;

    public string ReplaceWarning
    {
        get
        {
            if (IsWholeSave)
            {
                int replaced = _pcSlots.Count(s => !s.IsDeleted);
                return replaced == 0 ? string.Empty
                    : replaced == 1 ? "The 1 character on this PC will be replaced. Your current saves are backed up first."
                    : $"The {replaced} characters on this PC will be replaced. Your current saves are backed up first.";
            }
            return SelectedTarget is { ReplacesCharacter: true, Occupant: { } who }
                ? $"{who.CharacterName} (level {who.Level}) in slot {SelectedTarget.Index + 1} will be replaced. Your current saves are backed up first."
                : string.Empty;
        }
    }
    public bool HasReplaceWarning => ReplaceWarning.Length > 0;

    public bool CanInstall =>
        !IsBusy && IsPcReady && (IsWholeSave || (IsOneCharacter && SelectedSlot is { IsSelectable: true } && SelectedTarget is not null));

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

    public void Initialize()
    {
        PcFolder = _settings.LastPcFolder is { } last && Directory.Exists(last) ? last : SaveLocations.DefaultPcCloudFolder;
    }

    public async Task LoadZipAsync()
    {
        int version = ++_zipVersion;
        string zip = ZipPath;
        if (IsFromFile)
            return; // the source is a save file; the zip box is empty
        _zipSlots = Array.Empty<SlotInfo>();
        Slots.Clear();
        SelectedSlot = null;
        Status.Clear();

        if (!File.Exists(zip))
        {
            ZipNote = string.IsNullOrWhiteSpace(zip) ? string.Empty : "File not found.";
            Rebuild();
            return;
        }

        IsBusy = true;
        try
        {
            var info = await Task.Run(() => ConversionService.ReadMobileZip(zip));
            if (version != _zipVersion)
                return;

            _zipSlots = info.Slots;
            foreach (var slot in info.Slots)
                Slots.Add(new SlotItem(slot));
            SelectedSlot = Slots.FirstOrDefault(s => s.IsSelectable);
            ZipNote = IsZipReady ? string.Empty : "This Android save has no characters.";

            _settings.LastZipFolder = Path.GetDirectoryName(zip);
            _settings.Save();
        }
        catch (Exception ex)
        {
            if (version == _zipVersion)
                ZipNote = $"Couldn't read this zip: {ex.Message}";
        }
        finally
        {
            if (version == _zipVersion)
            {
                IsBusy = false;
                OnPropertyChanged(nameof(WholeSaveLabel));
                Rebuild();
            }
        }
    }

    public async Task LoadPcFolderAsync()
    {
        int version = ++_pcVersion;
        string folder = PcFolder;
        _pcReadable = false;

        try
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                _pcSlots = Array.Empty<SlotInfo>();
                PcNote = string.Empty;
            }
            else
            {
                _pcSlots = ConversionService.IsPcCloudFolder(folder)
                    ? await Task.Run(() => ConversionService.ReadPcSlots(folder))
                    : Array.Empty<SlotInfo>();
                if (version != _pcVersion)
                    return;

                _pcReadable = true;
                PcNote = !Directory.Exists(folder) ? "This folder doesn't exist yet. It will be created."
                    : _pcSlots.Count == 0 ? "No PC saves here yet. A new save will be created."
                    : string.Empty;
            }
        }
        catch (Exception ex)
        {
            if (version != _pcVersion)
                return;
            _pcSlots = Array.Empty<SlotInfo>();
            PcNote = $"Couldn't read the saves in this folder: {ex.Message}";
        }
        RebuildTargets();
    }

    private void RebuildTargets()
    {
        int? previous = SelectedTarget?.Index;
        TargetSlots.Clear();
        for (int i = 0; i < ConversionService.MaxSlots; i++)
            TargetSlots.Add(new TargetSlotItem(i, _pcSlots.FirstOrDefault(s => s.Index == i)));

        // Keep the user's pick; otherwise default to the first slot nothing would be lost in.
        _selectedTarget = TargetSlots.FirstOrDefault(t => t.Index == previous) ?? TargetSlots.FirstOrDefault(t => !t.ReplacesCharacter);
        OnPropertyChanged(nameof(SelectedTarget));
        Rebuild();
    }

    private void SetMode(bool wholeSave)
    {
        if (_wholeSave == wholeSave)
            return;
        _wholeSave = wholeSave;
        OnPropertyChanged(nameof(IsWholeSave));
        OnPropertyChanged(nameof(IsOneCharacter));
        Rebuild();
    }

    /// <summary>Recomputes "Your PC will have" and which steps are unlocked.</summary>
    private void Rebuild()
    {
        Plan.Clear();
        if (IsPcReady && IsWholeSave)
        {
            foreach (var slot in _zipSlots)
                Plan.Add(PlannedSlot.From(slot, slot.Index, "From Android", isNew: true));
        }
        else if (IsPcReady && IsOneCharacter && SelectedSlot is { IsSelectable: true } pick && SelectedTarget is { } target)
        {
            var rows = _pcSlots.Where(s => !s.IsDeleted && s.Index != target.Index)
                .Select(s => PlannedSlot.From(s, s.Index, "Already on this PC", isNew: false))
                .Append(PlannedSlot.From(pick.Info, target.Index, pick.IsFromFile ? "From save file" : "From Android", isNew: true))
                .OrderBy(r => r.Index);
            foreach (var row in rows)
                Plan.Add(row);
        }
        Refresh();
    }

    private async Task BrowseZipAsync()
    {
        if (await _dialogs.PickZipToOpenAsync("Choose the Android save zip", _settings.LastZipFolder ?? SaveLocations.Desktop) is { } zip)
            ZipPath = zip;
    }

    private async Task ChooseSaveFileAsync()
    {
        if (await _dialogs.PickSaveFileAsync("Choose a save file", _settings.LastZipFolder ?? SaveLocations.Desktop) is { } path)
            await UseSaveFileAsync(path);
    }

    /// <summary>Uses a single save file (unencrypted, PC or Android encrypted) as the character to install.</summary>
    public async Task UseSaveFileAsync(string path)
    {
        try
        {
            var (info, character) = await Task.Run(() => ConversionService.LoadSingleSave(path));
            if (info.IsDeleted)
            {
                ZipNote = "That save file holds a deleted character.";
                return;
            }

            ++_zipVersion; // cancel any zip load in flight
            _fileItem = new SlotItem(info) { Character = character };
            _zipPath = string.Empty;
            OnPropertyChanged(nameof(ZipPath));
            _zipSlots = new[] { info };
            Slots.Clear();
            Slots.Add(_fileItem);
            _selectedSlot = _fileItem;
            OnPropertyChanged(nameof(SelectedSlot));
            ZipNote = string.Empty;
            Status.Clear();
            OnFileSourceChanged();
            IsOneCharacter = true; // a single file can only go into one slot
        }
        catch (Exception ex)
        {
            ZipNote = $"Couldn't use that save file: {ex.Message}";
        }
    }

    private void ClearFileSource()
    {
        _fileItem = null;
        _zipSlots = Array.Empty<SlotInfo>();
        Slots.Clear();
        _selectedSlot = null;
        OnPropertyChanged(nameof(SelectedSlot));
        OnFileSourceChanged();
    }

    private void OnFileSourceChanged()
    {
        OnPropertyChanged(nameof(IsFromFile));
        OnPropertyChanged(nameof(FileSourceText));
        OnPropertyChanged(nameof(CanBringWholeSave));
        OnPropertyChanged(nameof(WholeSaveLabel));
        if (IsFromFile && _wholeSave == true)
        {
            _wholeSave = null;
            OnPropertyChanged(nameof(IsWholeSave));
            OnPropertyChanged(nameof(IsOneCharacter));
        }
        Rebuild();
    }

    private async Task BrowsePcFolderAsync()
    {
        string start = Directory.Exists(PcFolder) ? PcFolder : SaveLocations.DefaultPcCloudFolder;
        if (await _dialogs.PickFolderAsync("Choose your Infinity Blade III SwordGame\\Cloud folder", start) is { } folder)
            PcFolder = folder;
    }

    private async Task InstallAsync()
    {
        if (!CanInstall)
            return;

        bool whole = IsWholeSave;
        int pcCharacters = _pcSlots.Count(s => !s.IsDeleted);
        if (whole && pcCharacters > 0)
        {
            int incoming = _zipSlots.Count(s => !s.IsDeleted);
            string theyAre = pcCharacters == 1 ? "the 1 character" : $"all {pcCharacters} characters";
            if (!await _dialogs.ConfirmAsync("Replace the PC's characters?",
                    $"This replaces {theyAre} on this PC with the {incoming} from the Android save.\n\nYour current saves are copied to a backup folder first.",
                    "Replace"))
                return;
        }
        else if (!whole && SelectedTarget is { ReplacesCharacter: true, Occupant: { } who })
        {
            if (!await _dialogs.ConfirmAsync("Replace a character?",
                    $"Slot {SelectedTarget.Index + 1} has {who.CharacterName} (level {who.Level}). Putting {SelectedSlot!.Name} there replaces them.\n\nYour current saves are copied to a backup folder first.",
                    "Replace"))
                return;
        }

        string zip = ZipPath, folder = PcFolder;
        int source = SelectedSlot?.Index ?? 0, target = SelectedTarget?.Index ?? 0;
        var fileCharacter = IsFromFile ? _fileItem!.Character : null;
        IsBusy = true;
        Status.Set(StatusKind.Info, "Installing...");
        try
        {
            var result = await Task.Run(() => fileCharacter is not null
                ? ConversionService.SaveFileToPc(fileCharacter, folder, target)
                : whole
                    ? ConversionService.AndroidToPcWhole(zip, folder)
                    : ConversionService.AndroidToPc(zip, source, folder, target));
            _lastBackup = result.BackupFolder;
            _settings.LastPcFolder = folder;
            _settings.Save();

            int count = result.Characters.Count;
            string done = whole
                ? (count == 1 ? "The Android character is on this PC." : count == 2 ? "Both Android characters are on this PC." : $"All {count} Android characters are on this PC.")
                : $"{result.Characters[0].CharacterName} is in slot {target + 1}. The game will open that slot next.";
            string backup = result.BackupFolder is null ? "" : $" Your old saves are in \"{Path.GetFileName(result.BackupFolder)}\" next to the Cloud folder.";
            Status.Set(StatusKind.Success, $"Done. {done}{backup}");
            await LoadPcFolderAsync();
        }
        catch (InstallCopyException ex)
        {
            _lastBackup = ex.BackupFolder;
            Status.Set(StatusKind.Error, $"Install failed. {ex.Message} Is the game still running?");
        }
        catch (Exception ex)
        {
            Status.Set(StatusKind.Error, $"Install failed: {ex.Message} Your Cloud folder was not changed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Refresh()
    {
        foreach (string name in new[] { nameof(IsZipReady), nameof(IsPcStepEnabled), nameof(IsPcReady), nameof(CanInstall),
                                        nameof(HasPlan), nameof(ReplaceWarning), nameof(HasReplaceWarning) })
            OnPropertyChanged(name);
        BrowseZipCommand.RaiseCanExecuteChanged();
        ChooseSaveFileCommand.RaiseCanExecuteChanged();
        BrowsePcFolderCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
        RevealBackupCommand.RaiseCanExecuteChanged();
    }
}
