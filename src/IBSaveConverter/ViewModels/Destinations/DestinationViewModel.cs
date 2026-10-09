using System.Collections.ObjectModel;
using IBSaveConverter.Services;
using IBSaveConverter.ViewModels.Sources;

namespace IBSaveConverter.ViewModels.Destinations;

public enum DestinationKind
{
    Pc,
    Android,
    Ios,
}

/// <summary>Where the picked characters go. Builds the "will have" preview and runs the conversion.</summary>
public abstract class DestinationViewModel : ViewModelBase
{
    private string _warning = string.Empty;
    private string? _resultPath;

    protected DestinationViewModel(GameSaves game, AppSettings settings, IDialogService dialogs)
    {
        Game = game;
        Settings = settings;
        Dialogs = dialogs;
    }

    /// <summary>Raised when anything that decides whether the conversion can run has changed.</summary>
    public event Action? StateChanged;

    public abstract DestinationKind Kind { get; }
    public abstract string ActionText { get; }
    public abstract string RevealText { get; }
    public abstract string PlanTitle { get; }

    public ObservableCollection<PlannedSlot> Plan { get; } = new();
    public bool HasPlan => Plan.Count > 0;

    /// <summary>Why the conversion can't run as set up.</summary>
    public string Warning
    {
        get => _warning;
        protected set
        {
            if (SetField(ref _warning, value))
                OnPropertyChanged(nameof(HasWarning));
        }
    }
    public bool HasWarning => Warning.Length > 0;

    /// <summary>The file or folder to show after a conversion.</summary>
    public string? ResultPath
    {
        get => _resultPath;
        protected set
        {
            if (SetField(ref _resultPath, value))
                NotifyStateChanged();
        }
    }

    public abstract bool IsReady { get; }

    /// <summary>The game whose saves are written.</summary>
    protected GameSaves Game { get; }
    protected AppSettings Settings { get; }
    protected IDialogService Dialogs { get; }
    protected SourceViewModel? Source { get; private set; }
    protected IReadOnlyList<CharacterInfo> Picked { get; private set; } = Array.Empty<CharacterInfo>();

    public virtual void Initialize() { }

    public void SetCharacters(SourceViewModel source, IReadOnlyList<CharacterInfo> picked)
    {
        Source = source;
        Picked = picked;
        Rebuild();
    }

    /// <summary>Runs the conversion and reports the outcome in <paramref name="status"/>.</summary>
    public abstract Task RunAsync(StatusMessage status);

    protected abstract void Rebuild();

    protected void SetPlan(IEnumerable<PlannedSlot> rows)
    {
        Plan.Clear();
        foreach (var row in rows.OrderBy(r => r.Index))
            Plan.Add(row);
        OnPropertyChanged(nameof(HasPlan));
        NotifyStateChanged();
    }

    protected void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsReady));
        StateChanged?.Invoke();
    }
}
