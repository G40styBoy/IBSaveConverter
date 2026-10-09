using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels;

/// <summary>
/// The window: a start menu to pick a game, then that game's page with a Back button to the menu.
/// Each page keeps its state while another one is shown.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private ViewModelBase _currentPage;

    public ShellViewModel(AppSettings settings, IDialogService dialogs)
    {
        var ib3 = new ConverterPageViewModel(GameSaves.Ib3, settings, dialogs, "Move characters between PC, Android and iOS.",
                                             "avares://IBSaveConverter/Assets/ib3.png", hasIos: true);
        var ib2 = new ConverterPageViewModel(GameSaves.Ib2, settings, dialogs, "Move characters between PC and Android.",
                                             "avares://IBSaveConverter/Assets/ib2.png", hasIos: false);

        StartMenu = new StartMenuViewModel(new[] { Choice(ib3), Choice(ib2) });
        BackCommand = new AsyncCommand(() => Show(StartMenu));
        _currentPage = StartMenu;
    }

    public StartMenuViewModel StartMenu { get; }

    public AsyncCommand BackCommand { get; }

    /// <summary>The start menu or the game page being shown.</summary>
    public ViewModelBase CurrentPage
    {
        get => _currentPage;
        private set => SetField(ref _currentPage, value);
    }

    private GameChoice Choice(ConverterPageViewModel page) => new(page, new AsyncCommand(() =>
    {
        page.Open();
        return Show(page);
    }));

    private Task Show(ViewModelBase page)
    {
        CurrentPage = page;
        return Task.CompletedTask;
    }
}
