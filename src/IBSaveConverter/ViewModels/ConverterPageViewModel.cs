using System.Collections.ObjectModel;
using System.ComponentModel;
using IBSaveConverter.Services;
using IBSaveConverter.ViewModels.Destinations;
using IBSaveConverter.ViewModels.Sources;

namespace IBSaveConverter.ViewModels;

/// <summary>
/// One game's page, opened from the start menu. One flow for every conversion: pick where the characters are,
/// tick the ones to bring, pick where they go. Steps unlock in order. Both games use it; only Infinity Blade III
/// has the iOS SAVE folder source and destination.
/// </summary>
public sealed class ConverterPageViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private SourceViewModel _source;
    private DestinationViewModel _destination;
    private bool _isBusy;
    private bool _opened;

    /// <param name="subtitle">The line under the title.</param>
    /// <param name="iconPath">An avares:// path to the game's icon.</param>
    /// <param name="hasIos">Adds the iOS SAVE folder source and destination.</param>
    public ConverterPageViewModel(GameSaves game, AppSettings settings, IDialogService dialogs, string subtitle, string iconPath, bool hasIos)
    {
        _dialogs = dialogs;
        Title = game.Name;
        Subtitle = subtitle;
        IconPath = iconPath;

        PcSource = new PcSourceViewModel(game, settings, dialogs, Status);
        AndroidSource = new AndroidSourceViewModel(game, settings, dialogs, Status);
        SaveFileSource = new SaveFileSourceViewModel(game, settings, dialogs, Status);
        PcDestination = new PcDestinationViewModel(game, settings, dialogs);
        AndroidDestination = new AndroidDestinationViewModel(game, settings, dialogs);
        if (hasIos)
        {
            IosSource = new IosSourceViewModel(game, settings, dialogs, Status);
            IosDestination = new IosDestinationViewModel(game, settings, dialogs);
        }

        foreach (var source in Sources)
        {
            source.CharactersChanged += () => { if (source == _source) RebuildCharacters(); };
            source.PropertyChanged += OnSourcePropertyChanged;
        }
        foreach (var destination in Destinations)
            destination.StateChanged += Refresh;

        _source = PcSource;
        _destination = AndroidDestination;

        ConvertCommand = new AsyncCommand(ConvertAsync, () => CanConvert);
        RevealResultCommand = new AsyncCommand(() =>
        {
            if (Destination.ResultPath is { } path)
                _dialogs.Reveal(path);
            return Task.CompletedTask;
        }, () => Destination.ResultPath is not null);
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string IconPath { get; }

    public PcSourceViewModel PcSource { get; }
    public AndroidSourceViewModel AndroidSource { get; }
    public IosSourceViewModel? IosSource { get; }
    public SaveFileSourceViewModel SaveFileSource { get; }
    public PcDestinationViewModel PcDestination { get; }
    public AndroidDestinationViewModel AndroidDestination { get; }
    public IosDestinationViewModel? IosDestination { get; }

    public bool HasIos => IosSource is not null;

    public ObservableCollection<CharacterItem> Characters { get; } = new();
    public StatusMessage Status { get; } = new();

    public AsyncCommand ConvertCommand { get; }
    public AsyncCommand RevealResultCommand { get; }

    // ---------- Step: where the characters are ----------

    public SourceViewModel Source
    {
        get => _source;
        private set
        {
            if (!SetField(ref _source, value))
                return;
            OnPropertyChanged(nameof(IsPcSource));
            OnPropertyChanged(nameof(IsAndroidSource));
            OnPropertyChanged(nameof(IsIosSource));
            OnPropertyChanged(nameof(IsSaveFileSource));
            OnPropertyChanged(nameof(CanSendToPc));
            OnPropertyChanged(nameof(CanSendToAndroid));
            OnPropertyChanged(nameof(CanSendToIos));
            Status.Clear();

            if (!CanSendTo(Destination.Kind))
                Destination = Destination.Kind is DestinationKind.Pc ? AndroidDestination : PcDestination;
            RebuildCharacters();
        }
    }

    public bool IsPcSource { get => Source == PcSource; set { if (value) Source = PcSource; } }
    public bool IsAndroidSource { get => Source == AndroidSource; set { if (value) Source = AndroidSource; } }
    public bool IsIosSource { get => Source == IosSource; set { if (value && IosSource is { } ios) Source = ios; } }
    public bool IsSaveFileSource { get => Source == SaveFileSource; set { if (value) Source = SaveFileSource; } }

    // ---------- Step: characters ----------

    public bool HasCharacters => Characters.Count > 0;
    public bool CanPickCharacters => Characters.Any(c => c.IsSelectable);
    public IReadOnlyList<CharacterItem> PickedCharacters => Characters.Where(c => c.IsChecked).ToList();
    public bool AreCharactersPicked => PickedCharacters.Count > 0;

    // ---------- Step: where they go ----------

    public DestinationViewModel Destination
    {
        get => _destination;
        private set
        {
            if (!SetField(ref _destination, value))
                return;
            OnPropertyChanged(nameof(IsPcDestination));
            OnPropertyChanged(nameof(IsAndroidDestination));
            OnPropertyChanged(nameof(IsIosDestination));
            Refresh();
        }
    }

    public bool IsPcDestination { get => Destination == PcDestination; set { if (value && CanSendToPc) Destination = PcDestination; } }
    public bool IsAndroidDestination { get => Destination == AndroidDestination; set { if (value && CanSendToAndroid) Destination = AndroidDestination; } }
    public bool IsIosDestination { get => Destination == IosDestination; set { if (value && CanSendToIos && IosDestination is { } ios) Destination = ios; } }

    public bool CanSendToPc => CanSendTo(DestinationKind.Pc);
    public bool CanSendToAndroid => CanSendTo(DestinationKind.Android);
    public bool CanSendToIos => CanSendTo(DestinationKind.Ios);

    // ---------- Busy ----------

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                Refresh();
        }
    }

    public bool IsWorking => IsBusy || Source.IsLoading;
    public bool IsIdle => !IsWorking;

    public bool CanConvert => !IsWorking && AreCharactersPicked && Destination.IsReady;

    /// <summary>Called each time the page is shown. Loads remembered folders the first time only.</summary>
    public void Open()
    {
        if (_opened)
            return;
        _opened = true;
        foreach (var source in Sources)
            source.Initialize();
        foreach (var destination in Destinations)
            destination.Initialize();
    }

    private IEnumerable<SourceViewModel> Sources =>
        new SourceViewModel?[] { PcSource, AndroidSource, IosSource, SaveFileSource }.OfType<SourceViewModel>();
    private IEnumerable<DestinationViewModel> Destinations =>
        new DestinationViewModel?[] { PcDestination, AndroidDestination, IosDestination }.OfType<DestinationViewModel>();

    // A save set can't be sent back to its own kind of save. iOS to iOS is allowed: it moves a save between the
    // device key and the fixed key.
    private bool CanSendTo(DestinationKind kind) => kind switch
    {
        DestinationKind.Pc => Source.Kind is not SourceKind.Pc,
        DestinationKind.Android => Source.Kind is not SourceKind.Android,
        DestinationKind.Ios => HasIos,
        _ => false,
    };

    // Ticks survive a reload of the same characters. Otherwise new save files start ticked, and a save set
    // starts with its last played character ticked.
    private void RebuildCharacters()
    {
        var ticked = Characters.Where(c => c.IsChecked).Select(c => c.Info).ToHashSet();
        var known = Characters.Select(c => c.Info).ToHashSet();

        Characters.Clear();
        foreach (var info in Source.Characters)
        {
            var item = new CharacterItem(info, OnTicksChanged, Source.RemoveCommandFor(info));
            Characters.Add(item);
            if (ticked.Contains(info) || (!known.Contains(info) && info.SourceFile is not null))
                item.IsChecked = true;
        }

        if (!Characters.Any(c => c.IsChecked))
        {
            int? current = Source.Save?.CurrentSlot;
            var first = Characters.FirstOrDefault(c => c.IsSelectable && c.Info.Slot == current) ?? Characters.FirstOrDefault(c => c.IsSelectable);
            if (first is not null)
                first.IsChecked = true;
        }

        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(CanPickCharacters));
        OnTicksChanged();
    }

    private void OnTicksChanged()
    {
        var picked = PickedCharacters.Select(c => c.Info).ToList();
        foreach (var destination in Destinations)
            destination.SetCharacters(Source, picked);
        OnPropertyChanged(nameof(AreCharactersPicked));
        Refresh();
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == Source && e.PropertyName == nameof(SourceViewModel.IsLoading))
            Refresh();
    }

    private async Task ConvertAsync()
    {
        if (!CanConvert)
            return;

        IsBusy = true;
        try
        {
            await Destination.RunAsync(Status);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanConvert));
        ConvertCommand.RaiseCanExecuteChanged();
        RevealResultCommand.RaiseCanExecuteChanged();
    }
}
