using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels.Destinations;

/// <summary>
/// An Android save zip. The game loads one zip holding the whole save, so the zip is either a new save with
/// the picked characters or an existing Android save with them added to its free slots.
/// </summary>
public sealed class AndroidDestinationViewModel : MobileDestinationViewModel
{
    private string _outputZip = string.Empty;
    private bool _outputChosenByUser;

    public AndroidDestinationViewModel(GameSaves game, AppSettings settings, IDialogService dialogs) : base(game, settings, dialogs)
    {
        BrowseOutputCommand = new AsyncCommand(BrowseOutputAsync);
    }

    public override DestinationKind Kind => DestinationKind.Android;
    public override string ActionText => "Make Android save";
    public override string RevealText => "Show in folder";
    protected override string SaveName => "Android save";

    public AsyncCommand BrowseOutputCommand { get; }

    public string OutputZip
    {
        get => _outputZip;
        set
        {
            if (SetField(ref _outputZip, value))
                NotifyStateChanged();
        }
    }

    protected override bool HasOutput => !string.IsNullOrWhiteSpace(OutputZip);

    private string OutputFolder =>
        Path.GetDirectoryName(_outputZip) is { Length: > 0 } dir ? dir : Settings.LastZipFolder ?? SaveLocations.Desktop;

    public override async Task RunAsync(StatusMessage status)
    {
        if (!IsReady)
            return;

        string output = OutputZip;
        string? existing = IsAddToExisting ? ExistingPath : null;
        var characters = Picked.Select(c => c.Data).ToList();
        int total = Plan.Count;

        status.Set(StatusKind.Info, "Converting...");
        try
        {
            await Task.Run(() => Game.ToAndroid(characters, output, existing));
            ResultPath = output;
            Settings.LastZipFolder = Path.GetDirectoryName(output);
            Settings.Save();

            string what = total == 1 ? "1 character" : $"{total} characters";
            status.Set(StatusKind.Success,
                $"Done. {Path.GetFileName(output)} holds {what}. Load it in the Android game; it replaces the save that's on the phone now.");
        }
        catch (Exception ex)
        {
            status.Set(StatusKind.Error, $"Conversion failed: {ex.Message}");
        }
    }

    protected override bool ExistsOnDisk(string path) => File.Exists(path);

    protected override LoadedSave ReadExisting(string path) => Game.ReadAndroidZip(path);

    protected override Task<string?> PickExistingAsync() =>
        Dialogs.PickZipToOpenAsync("Choose the Android save to add to", Settings.LastZipFolder ?? SaveLocations.Desktop);

    protected override void OnExistingLoaded(string path, LoadedSave save)
    {
        Settings.LastZipFolder = Path.GetDirectoryName(path);
        Settings.Save();
    }

    protected override void OnPlanRebuilt()
    {
        if (!_outputChosenByUser && Picked.Count > 0)
            OutputZip = Path.Combine(OutputFolder, Game.SuggestedZipName);
    }

    private async Task BrowseOutputAsync()
    {
        if (await Dialogs.PickZipToSaveAsync("Save the Android save as", Game.SuggestedZipName, OutputFolder) is { } path)
        {
            _outputChosenByUser = true;
            OutputZip = path;
        }
    }
}
