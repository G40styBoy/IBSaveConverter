using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels.Destinations;

/// <summary>
/// A mobile save (an Android zip or an iOS SAVE folder). It is either a new save with the picked characters, or an
/// existing save that keeps its characters and gets the picked ones in its free slots. Subclasses say where the
/// save is read from and written to.
/// </summary>
public abstract class MobileDestinationViewModel : DestinationViewModel
{
    private bool? _addToExisting; // null until the user picks
    private string _existingPath = string.Empty;
    private string _existingNote = string.Empty;
    private int _existingVersion;

    protected MobileDestinationViewModel(GameSaves game, AppSettings settings, IDialogService dialogs) : base(game, settings, dialogs)
    {
        BrowseExistingCommand = new AsyncCommand(async () =>
        {
            if (await PickExistingAsync() is { } path)
                ExistingPath = path;
        });
    }

    /// <summary>"Android save", "iOS save": used in the preview and in notes.</summary>
    protected abstract string SaveName { get; }

    public override string PlanTitle => IsAddToExisting ? $"The {SaveName} will now have:" : $"The new {SaveName} will have:";

    public AsyncCommand BrowseExistingCommand { get; }

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

    public string ExistingPath
    {
        get => _existingPath;
        set
        {
            if (SetField(ref _existingPath, value))
                _ = LoadExistingAsync();
        }
    }

    public string ExistingNote
    {
        get => _existingNote;
        protected set
        {
            if (SetField(ref _existingNote, value))
                OnPropertyChanged(nameof(HasExistingNote));
        }
    }
    public bool HasExistingNote => ExistingNote.Length > 0;

    public override bool IsReady => IsPlanComplete && Plan.Count(p => p.IsNew) == Picked.Count && HasOutput;

    /// <summary>The characters already in the existing save.</summary>
    protected IReadOnlyList<CharacterInfo> Existing { get; private set; } = Array.Empty<CharacterInfo>();

    protected bool IsPlanComplete => Picked.Count > 0 && _addToExisting is not null && (_addToExisting == false || IsExistingLoaded);

    private bool IsExistingLoaded => Existing.Count > 0 && ExistsOnDisk(ExistingPath);

    /// <summary>True once there is somewhere to write the save.</summary>
    protected abstract bool HasOutput { get; }

    protected abstract bool ExistsOnDisk(string path);

    /// <summary>Reads the existing save. Runs off the UI thread.</summary>
    protected abstract LoadedSave ReadExisting(string path);

    protected abstract Task<string?> PickExistingAsync();

    /// <summary>Called on the UI thread after the existing save was read.</summary>
    protected virtual void OnExistingLoaded(string path, LoadedSave save) { }

    /// <summary>The note shown when the existing save can't be read.</summary>
    protected virtual string ReadFailedNote(Exception ex) => $"Couldn't read this {SaveName}: {ex.Message}";

    /// <summary>Called after the preview is rebuilt.</summary>
    protected virtual void OnPlanRebuilt() { }

    protected override void Rebuild()
    {
        var kept = IsAddToExisting ? Existing.Where(c => !c.IsDeleted).ToList() : new List<CharacterInfo>();
        var targets = Game.PlanFreeSlots(kept, Picked.Count);
        string origin = Source?.Origin ?? string.Empty;

        SetPlan(!IsPlanComplete ? Array.Empty<PlannedSlot>() :
            kept.Select(c => PlannedSlot.From(c, c.Slot, $"Already in this {SaveName}", isNew: false))
                .Concat(Picked.Take(targets.Count).Select((c, i) => PlannedSlot.From(c, targets[i], origin, isNew: true))));

        Warning = !IsPlanComplete || targets.Count == Picked.Count ? string.Empty
            : targets.Count == 0 ? $"A save can only have {Game.MaxSlots} characters at a time, and this one is full. Start a new save instead."
            : $"A save can only have {Game.MaxSlots} characters at a time. Untick {Picked.Count - targets.Count}.";

        OnPlanRebuilt();
    }

    /// <summary>Reads the existing save again, for example after its key changed.</summary>
    protected async Task LoadExistingAsync()
    {
        int version = ++_existingVersion;
        string path = ExistingPath;
        Existing = Array.Empty<CharacterInfo>();

        if (!ExistsOnDisk(path))
        {
            ExistingNote = string.IsNullOrWhiteSpace(path) ? string.Empty : "Not found.";
            Rebuild();
            return;
        }

        try
        {
            var save = await Task.Run(() => ReadExisting(path));
            if (version != _existingVersion)
                return;

            Existing = save.Characters;
            ExistingNote = save.Characters.Count == 0 ? $"This {SaveName} has no characters. Start a new save instead." : string.Empty;
            OnExistingLoaded(path, save);
        }
        catch (Exception ex)
        {
            if (version != _existingVersion)
                return;
            ExistingNote = ReadFailedNote(ex);
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
}
