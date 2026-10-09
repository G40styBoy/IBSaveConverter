using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels.Sources;

public sealed class AndroidSourceViewModel : SourceViewModel
{
    private string _zipPath = string.Empty;

    public AndroidSourceViewModel(GameSaves game, AppSettings settings, IDialogService dialogs, StatusMessage status) : base(game, settings, dialogs, status)
    {
        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsLoading);
    }

    public override SourceKind Kind => SourceKind.Android;
    public override string Origin => "From Android";

    public AsyncCommand BrowseCommand { get; }

    public string ZipPath
    {
        get => _zipPath;
        set
        {
            if (SetField(ref _zipPath, value))
                _ = LoadAsync();
        }
    }

    public async Task LoadAsync()
    {
        string zip = ZipPath;
        if (!File.Exists(zip))
        {
            Reset(string.IsNullOrWhiteSpace(zip) ? string.Empty : "File not found.");
            return;
        }

        if (await LoadAsync(() => Game.ReadAndroidZip(zip), "Couldn't read this zip"))
        {
            Settings.LastZipFolder = Path.GetDirectoryName(zip);
            Settings.Save();
        }
    }

    protected override void Refresh()
    {
        base.Refresh();
        BrowseCommand.RaiseCanExecuteChanged();
    }

    private async Task BrowseAsync()
    {
        if (await Dialogs.PickZipToOpenAsync("Choose the Android save zip", Settings.LastZipFolder ?? SaveLocations.Desktop) is { } zip)
            ZipPath = zip;
    }
}
