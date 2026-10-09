namespace IBSaveConverter.ViewModels;

/// <summary>The first screen: one tile per game.</summary>
public sealed class StartMenuViewModel : ViewModelBase
{
    public StartMenuViewModel(IReadOnlyList<GameChoice> games) => Games = games;

    public IReadOnlyList<GameChoice> Games { get; }
}

/// <summary>A game on the start menu and the command that opens its page.</summary>
public sealed record GameChoice(ConverterPageViewModel Page, AsyncCommand OpenCommand);
