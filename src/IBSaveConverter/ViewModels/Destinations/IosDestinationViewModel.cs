using IBSaveConverter.Services;
using IBSaveEditor.Package.Conversion;

namespace IBSaveConverter.ViewModels.Destinations;

/// <summary>
/// An iOS SAVE folder: the same files as an Android save, in a folder. It is encrypted with the fixed key (for a
/// game with the fixed-key patch), or with the device's own key when the toggle is on (for an unpatched game).
/// A new save goes into a folder of its own; adding to an existing one writes into that folder. Either way the
/// folder is backed up first if it has anything in it.
/// </summary>
public sealed class IosDestinationViewModel : MobileDestinationViewModel
{
    private const string SAVE_FOLDER_NAME = "SAVE";

    private string _outputFolder = string.Empty;
    private bool _useDeviceKey;
    private string _deviceKey = string.Empty;

    public IosDestinationViewModel(GameSaves game, AppSettings settings, IDialogService dialogs) : base(game, settings, dialogs)
    {
        BrowseOutputCommand = new AsyncCommand(BrowseOutputAsync);
    }

    public override DestinationKind Kind => DestinationKind.Ios;
    public override string ActionText => "Make iOS save";
    public override string RevealText => "Show SAVE folder";
    protected override string SaveName => "iOS save";

    public AsyncCommand BrowseOutputCommand { get; }

    /// <summary>Where a new save goes. Only used for a new save.</summary>
    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetField(ref _outputFolder, value))
            {
                OnPropertyChanged(nameof(OutputNote));
                OnPropertyChanged(nameof(HasOutputNote));
                NotifyStateChanged();
            }
        }
    }

    public string OutputNote => ConversionService.IsSaveFolder(OutputFolder)
        ? "This folder already has a save. Its files are backed up next to it, then replaced."
        : string.Empty;
    public bool HasOutputNote => OutputNote.Length > 0;

    public bool UseDeviceKey
    {
        get => _useDeviceKey;
        set
        {
            if (!SetField(ref _useDeviceKey, value))
                return;
            // Start from the last key the app found, such as the one the iOS source just used.
            if (value && DeviceKey.Length == 0 && Settings.IosDeviceId is { } known)
            {
                _deviceKey = known;
                OnPropertyChanged(nameof(DeviceKey));
            }
            OnKeyChanged();
        }
    }

    public string DeviceKey
    {
        get => _deviceKey;
        set
        {
            if (!SetField(ref _deviceKey, value?.Trim() ?? string.Empty))
                return;
            OnKeyChanged();
            // A typed key may be the one that unlocks the folder being added to.
            if (IsAddToExisting && Existing.Count == 0)
                _ = LoadExistingAsync();
        }
    }

    public string DeviceKeyNote => UseDeviceKey && DeviceKey.Length > 0 && !IsDeviceKeyValid
        ? "A device key is 32 letters and digits, not counting dashes, like 1B3E0000-0000-4000-8000-00000000C0DE."
        : string.Empty;
    public bool HasDeviceKeyNote => DeviceKeyNote.Length > 0;

    private bool IsDeviceKeyValid => ConversionService.IsValidDeviceId(DeviceKey);

    public override bool IsReady => base.IsReady && (!UseDeviceKey || IsDeviceKeyValid);

    protected override bool HasOutput => IsAddToExisting || !string.IsNullOrWhiteSpace(OutputFolder);

    // Every key the app knows, for reading a SAVE folder that is locked to its device.
    private IEnumerable<string> KnownKeys => new[] { DeviceKey, Settings.IosDeviceId }.OfType<string>().Where(k => k.Length > 0);

    public override void Initialize()
    {
        OutputFolder = Settings.LastIosOutputFolder ?? Path.Combine(SaveLocations.Desktop, SAVE_FOLDER_NAME);
    }

    public override async Task RunAsync(StatusMessage status)
    {
        if (!IsReady)
            return;

        bool keepExisting = IsAddToExisting;
        string folder = keepExisting ? ExistingPath : OutputFolder;
        string? deviceId = UseDeviceKey ? DeviceKey : null;
        var knownKeys = KnownKeys.ToList();
        var characters = Picked.Select(c => c.Data).ToList();
        int total = Plan.Count;

        status.Set(StatusKind.Info, "Converting...");
        try
        {
            var result = await Task.Run(() => ConversionService.ToIos(characters, folder, keepExisting, knownKeys, deviceId));
            ResultPath = folder;
            if (!keepExisting)
                Settings.LastIosOutputFolder = folder;
            if (deviceId is not null)
                Settings.IosDeviceId = deviceId;
            Settings.Save();

            string what = total == 1 ? "1 character" : $"{total} characters";
            string key = deviceId is null ? "the fixed key" : "the device key";
            string backup = result.BackupFolder is null ? "" : $" Its old files are in \"{Path.GetFileName(result.BackupFolder)}\" next to it.";
            status.Set(StatusKind.Success,
                $"Done. The SAVE folder holds {what}, on {key}. Copy it onto the device in place of the game's SAVE folder.{backup}");
        }
        catch (InstallCopyException ex)
        {
            ResultPath = ex.BackupFolder;
            status.Set(StatusKind.Error, $"Conversion failed. {ex.Message}");
        }
        catch (Exception ex)
        {
            status.Set(StatusKind.Error, $"Conversion failed: {ex.Message} The folder was not changed.");
        }
    }

    protected override bool ExistsOnDisk(string path) => Directory.Exists(path);

    protected override LoadedSave ReadExisting(string path) => ConversionService.ReadIosSaveFolder(path, KnownKeys.ToList());

    protected override Task<string?> PickExistingAsync() =>
        Dialogs.PickFolderAsync("Choose the iOS SAVE folder to add to", Settings.LastIosSaveFolder ?? SaveLocations.Desktop);

    // Match the folder's own key by default, so it keeps working with the game it came from.
    protected override void OnExistingLoaded(string path, LoadedSave save)
    {
        if (save.DeviceId is { } id)
            _deviceKey = id;
        _useDeviceKey = save.DeviceId is not null;
        OnPropertyChanged(nameof(DeviceKey));
        OnPropertyChanged(nameof(UseDeviceKey));
        OnKeyChanged();
    }

    protected override string ReadFailedNote(Exception ex) => ex is SaveKeyNotFoundException
        ? "This SAVE folder is locked to its device. Turn on \"Use the device's own key\" below and enter its key."
        : base.ReadFailedNote(ex);

    private void OnKeyChanged()
    {
        OnPropertyChanged(nameof(DeviceKeyNote));
        OnPropertyChanged(nameof(HasDeviceKeyNote));
        NotifyStateChanged();
    }

    // A picked folder named SAVE, or one that already holds a save, is used as is. Anything else gets a SAVE folder inside it.
    private async Task BrowseOutputAsync()
    {
        string start = Directory.Exists(OutputFolder) ? OutputFolder : SaveLocations.Desktop;
        if (await Dialogs.PickFolderAsync("Choose where to make the SAVE folder", start) is not { } picked)
            return;
        bool useAsIs = ConversionService.IsSaveFolder(picked) ||
                       string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(picked)), SAVE_FOLDER_NAME, StringComparison.OrdinalIgnoreCase);
        OutputFolder = useAsIs ? picked : Path.Combine(picked, SAVE_FOLDER_NAME);
    }
}
