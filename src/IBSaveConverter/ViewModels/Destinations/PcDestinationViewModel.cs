using System.Collections.ObjectModel;
using IBSaveConverter.Services;
using IBSaveEditor.Package.Conversion;

namespace IBSaveConverter.ViewModels.Destinations;

/// <summary>
/// The PC Cloud folder, installed in place with a backup first. The picked characters are added to the PC's
/// save, or replace every character on it.
/// </summary>
public sealed class PcDestinationViewModel : DestinationViewModel
{
    private string _folder = string.Empty;
    private string _note = string.Empty;
    private string _notice = string.Empty;
    private bool _readable;
    private bool _replace;
    private IReadOnlyList<CharacterInfo> _pcCharacters = Array.Empty<CharacterInfo>();
    private TargetSlotItem? _selectedTarget;
    private IReadOnlyList<SlotPlacement> _placements = Array.Empty<SlotPlacement>();
    private int _currentSlot;
    private int _loadVersion;

    public PcDestinationViewModel(GameSaves game, AppSettings settings, IDialogService dialogs) : base(game, settings, dialogs)
    {
        BrowseCommand = new AsyncCommand(BrowseAsync);
    }

    public override DestinationKind Kind => DestinationKind.Pc;
    public override string ActionText => "Install on PC";
    public override string RevealText => "Show backup";
    public override string PlanTitle => "Your PC will have:";

    public AsyncCommand BrowseCommand { get; }

    public string FolderHint => Game.PcFolderHint;
    public string CloseGameNote => $"Close {Game.Name} before installing.";
    public ObservableCollection<TargetSlotItem> TargetSlots { get; } = new();

    public string Folder
    {
        get => _folder;
        set
        {
            if (SetField(ref _folder, value))
                _ = LoadAsync();
        }
    }

    /// <summary>Only shown for a new or unreadable folder.</summary>
    public string Note
    {
        get => _note;
        private set
        {
            if (SetField(ref _note, value))
                OnPropertyChanged(nameof(HasNote));
        }
    }
    public bool HasNote => Note.Length > 0;

    public bool IsAdd
    {
        get => !_replace;
        set { if (value) SetMode(replace: false); }
    }

    public bool IsReplace
    {
        get => _replace;
        set { if (value) SetMode(replace: true); }
    }

    public bool ShowTargetPicker => IsAdd && Picked.Count == 1;

    public TargetSlotItem? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetField(ref _selectedTarget, value))
                Rebuild();
        }
    }

    /// <summary>Which characters on the PC get replaced. Doesn't block the install.</summary>
    public string Notice
    {
        get => _notice;
        private set
        {
            if (SetField(ref _notice, value))
                OnPropertyChanged(nameof(HasNotice));
        }
    }
    public bool HasNotice => Notice.Length > 0;

    public override bool IsReady => _readable && Picked.Count > 0 && _placements.Count == Picked.Count;

    public override void Initialize()
    {
        Folder = Game.LastPcFolder(Settings) is { } last && Directory.Exists(last) ? last : Game.DefaultPcFolder;
    }

    public async Task LoadAsync()
    {
        int version = ++_loadVersion;
        string folder = Folder;
        _readable = false;

        try
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                _pcCharacters = Array.Empty<CharacterInfo>();
                Note = string.Empty;
            }
            else
            {
                _pcCharacters = Game.IsPcFolder(folder)
                    ? (await Task.Run(() => Game.ReadPcFolder(folder))).Characters
                    : Array.Empty<CharacterInfo>();
                if (version != _loadVersion)
                    return;

                _readable = true;
                Note = !Directory.Exists(folder) ? "This folder doesn't exist yet. It will be created."
                    : _pcCharacters.Count == 0 ? "No PC saves here yet. A new save will be created."
                    : string.Empty;
            }
        }
        catch (Exception ex)
        {
            if (version != _loadVersion)
                return;
            _pcCharacters = Array.Empty<CharacterInfo>();
            Note = $"Couldn't read the saves in this folder: {ex.Message}";
        }
        RebuildTargets();
    }

    public override async Task RunAsync(StatusMessage status)
    {
        if (!IsReady || !await ConfirmAsync())
            return;

        string folder = Folder;
        bool replace = IsReplace;
        var placements = _placements;
        int currentSlot = _currentSlot;

        status.Set(StatusKind.Info, "Installing...");
        try
        {
            var result = await Task.Run(() => replace
                ? Game.ReplacePc(placements, currentSlot, folder)
                : Game.AddToPc(placements, folder));
            ResultPath = result.BackupFolder;
            Game.RememberPcFolder(Settings, folder);
            Settings.Save();

            var first = result.Characters[0];
            string done = result.Characters.Count == 1
                ? $"{first.CharacterName} is in slot {first.Slot + 1}."
                : $"{result.Characters.Count} characters are on this PC.";
            string backup = result.BackupFolder is null ? "" : $" Your old saves are in \"{Path.GetFileName(result.BackupFolder)}\" next to the Cloud folder.";
            status.Set(StatusKind.Success, $"Done. {done}{backup}");
            await LoadAsync();
        }
        catch (InstallCopyException ex)
        {
            ResultPath = ex.BackupFolder;
            status.Set(StatusKind.Error, $"Install failed. {ex.Message} Is the game still running?");
        }
        catch (Exception ex)
        {
            status.Set(StatusKind.Error, $"Install failed: {ex.Message} Your Cloud folder was not changed.");
        }
    }

    protected override void Rebuild()
    {
        OnPropertyChanged(nameof(ShowTargetPicker));
        if (!_readable || Picked.Count == 0)
        {
            _placements = Array.Empty<SlotPlacement>();
            Warning = Notice = string.Empty;
            SetPlan(Array.Empty<PlannedSlot>());
            return;
        }

        var live = _pcCharacters.Where(c => !c.IsDeleted).ToList();
        var targets = _replace ? ReplaceTargets() : AddTargets(live);
        var placed = Picked.Zip(targets).ToList();
        _placements = placed.Select(p => new SlotPlacement(p.First.Data, p.Second)).ToList();
        _currentSlot = Source?.Save?.CurrentSlot ?? targets.FirstOrDefault();

        string origin = Source?.Origin ?? string.Empty;
        var kept = _replace ? Enumerable.Empty<CharacterInfo>() : live.Where(c => !targets.Contains(c.Slot));
        SetPlan(kept.Select(c => PlannedSlot.From(c, c.Slot, "Already on this PC", isNew: false))
            .Concat(placed.Select(p => PlannedSlot.From(p.First, p.Second, origin, isNew: true))));

        Warning = targets.Count == Picked.Count ? string.Empty : MissingSlotsWarning(targets.Count);
        Notice = ReplacedNotice(live, targets);
    }

    // Characters from a save set keep their slot numbers; single save files fill the slots left over.
    private List<int> ReplaceTargets()
    {
        var targets = new List<int>();
        var free = new Queue<int>(Enumerable.Range(0, Game.MaxSlots)
            .Where(slot => !Picked.Any(c => c.SourceFile is null && c.Slot == slot)));
        foreach (var character in Picked)
        {
            if (character.SourceFile is null && character.Slot >= 0 && character.Slot < Game.MaxSlots)
                targets.Add(character.Slot);
            else if (free.TryDequeue(out int slot))
                targets.Add(slot);
            else
                break;
        }
        return targets;
    }

    private List<int> AddTargets(IReadOnlyList<CharacterInfo> live)
    {
        if (Picked.Count == 1)
            return SelectedTarget is { } target ? new List<int> { target.Index } : new List<int>();
        return Game.PlanFreeSlots(live, Picked.Count).ToList();
    }

    private string MissingSlotsWarning(int placed)
    {
        int extra = Picked.Count - placed;
        if (_replace)
            return $"A save folder can only have {Game.MaxSlots} saves at a time. Untick {extra}.";
        return placed == 0
            ? "This PC save is full. Pick one character to choose a slot to replace, or replace the characters on this PC."
            : $"This PC save only has {placed} free slot{(placed == 1 ? "" : "s")}. Untick {extra}, or replace the characters on this PC.";
    }

    private string ReplacedNotice(IReadOnlyList<CharacterInfo> live, IReadOnlyList<int> targets)
    {
        var replaced = _replace ? live : live.Where(c => targets.Contains(c.Slot)).ToList();
        string backedUp = " Your current saves are backed up first.";
        return replaced.Count switch
        {
            0 => string.Empty,
            1 when !_replace => $"{replaced[0].Name} (level {replaced[0].Level}) in slot {replaced[0].Slot + 1} will be replaced.{backedUp}",
            1 => $"The 1 character on this PC will be replaced.{backedUp}",
            _ => $"The {replaced.Count} characters on this PC will be replaced.{backedUp}",
        };
    }

    private async Task<bool> ConfirmAsync()
    {
        var live = _pcCharacters.Where(c => !c.IsDeleted).ToList();
        if (_replace && live.Count > 0)
        {
            string theyAre = live.Count == 1 ? "the 1 character" : $"all {live.Count} characters";
            string incoming = Picked.Count == 1 ? "the 1 you picked" : $"the {Picked.Count} you picked";
            return await Dialogs.ConfirmAsync("Replace the PC's characters?",
                $"This replaces {theyAre} on this PC with {incoming}.\n\nYour current saves are copied to a backup folder first.", "Replace");
        }

        var overwritten = live.Where(c => _placements.Any(p => p.Slot == c.Slot)).ToList();
        if (overwritten.Count == 0)
            return true;

        string names = string.Join(", ", overwritten.Select(c => $"{c.Name} (slot {c.Slot + 1})"));
        return await Dialogs.ConfirmAsync("Replace a character?",
            $"This replaces {names} on this PC.\n\nYour current saves are copied to a backup folder first.", "Replace");
    }

    private void SetMode(bool replace)
    {
        if (_replace == replace)
            return;
        _replace = replace;
        OnPropertyChanged(nameof(IsAdd));
        OnPropertyChanged(nameof(IsReplace));
        Rebuild();
    }

    private void RebuildTargets()
    {
        int? previous = SelectedTarget?.Index;
        TargetSlots.Clear();
        for (int i = 0; i < Game.MaxSlots; i++)
            TargetSlots.Add(new TargetSlotItem(i, _pcCharacters.FirstOrDefault(c => c.Slot == i)));

        // Keep the user's pick; otherwise the first slot where nothing is replaced.
        _selectedTarget = TargetSlots.FirstOrDefault(t => t.Index == previous) ?? TargetSlots.FirstOrDefault(t => !t.ReplacesCharacter);
        OnPropertyChanged(nameof(SelectedTarget));
        Rebuild();
    }

    private async Task BrowseAsync()
    {
        string start = Directory.Exists(Folder) ? Folder : Game.DefaultPcFolder;
        if (await Dialogs.PickFolderAsync($"Choose your {Game.Name} SwordGame\\Cloud folder", start) is { } folder)
            Folder = folder;
    }
}
