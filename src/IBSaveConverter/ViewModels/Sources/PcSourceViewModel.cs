using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels.Sources;

public sealed class PcSourceViewModel : SourceViewModel
{
    private string _folder = string.Empty;

    public PcSourceViewModel(GameSaves game, AppSettings settings, IDialogService dialogs, StatusMessage status) : base(game, settings, dialogs, status)
    {
        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsLoading);
        ReloadCommand = new AsyncCommand(LoadAsync, () => !IsLoading);
    }

    public override SourceKind Kind => SourceKind.Pc;
    public override string Origin => "From PC";

    public AsyncCommand BrowseCommand { get; }
    public AsyncCommand ReloadCommand { get; }

    public string FolderHint => Game.PcFolderHint;

    public string Folder
    {
        get => _folder;
        set
        {
            if (SetField(ref _folder, value))
                _ = LoadAsync();
        }
    }

    public override void Initialize()
    {
        Folder = Game.LastPcFolder(Settings) is { } last && Directory.Exists(last) ? last : Game.DefaultPcFolder;
    }

    public async Task LoadAsync()
    {
        string folder = Folder;
        if (!Game.IsPcFolder(folder))
        {
            Reset(Directory.Exists(folder)
                ? "No PC saves in this folder. Pick the SwordGame\\Cloud folder."
                : "Folder not found. Pick your SwordGame\\Cloud folder.");
            return;
        }

        if (await LoadAsync(() => Game.ReadPcFolder(folder), "Couldn't read the saves in this folder"))
        {
            Game.RememberPcFolder(Settings, folder);
            Settings.Save();
        }
    }

    protected override void Refresh()
    {
        base.Refresh();
        BrowseCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
    }

    private async Task BrowseAsync()
    {
        string start = Directory.Exists(Folder) ? Folder : Game.DefaultPcFolder;
        if (await Dialogs.PickFolderAsync($"Choose your {Game.Name} SwordGame\\Cloud folder", start) is { } folder)
            Folder = folder;
    }
}
