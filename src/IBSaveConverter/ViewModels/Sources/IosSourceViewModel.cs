using IBSaveConverter.Services;
using IBSaveEditor.Package.Conversion;

namespace IBSaveConverter.ViewModels.Sources;

/// <summary>
/// An iOS SAVE folder copied off the device. A save from a game with the fixed-key patch opens straight away.
/// A legacy save is locked to its device's own key; the key section then appears so it can be read from an
/// encrypted backup or typed in. Any key found is remembered.
/// </summary>
public sealed class IosSourceViewModel : SourceViewModel
{
    private bool _fromBackup = true;
    private bool _needsDeviceKey;
    private string _backupFolder = string.Empty;
    private string _password = string.Empty;
    private string _deviceId = string.Empty;
    private string _saveFolder = string.Empty;
    private string _foundDeviceId = string.Empty;
    private string _deviceName = string.Empty;
    private string? _matchedKey;  // a device key that unlocked the save index, set when the load failed afterwards
    private bool _keyNotFound;    // set when no known key unlocked the save index
    private IReadOnlyList<string> _backupKeys = Array.Empty<string>();

    public IosSourceViewModel(GameSaves game, AppSettings settings, IDialogService dialogs, StatusMessage status) : base(game, settings, dialogs, status)
    {
        BrowseSaveFolderCommand = new AsyncCommand(BrowseSaveFolderAsync, () => !IsLoading);
        BrowseBackupCommand     = new AsyncCommand(BrowseBackupAsync, () => !IsLoading);
        ReadBackupCommand       = new AsyncCommand(ReadBackupAsync, () => CanReadBackup);
        CopyDeviceIdCommand     = new AsyncCommand(() => Dialogs.CopyToClipboardAsync(FoundDeviceId), () => HasFoundDeviceId);
    }

    public override SourceKind Kind => SourceKind.Ios;
    public override string Origin => "From iOS";

    public AsyncCommand BrowseSaveFolderCommand { get; }
    public AsyncCommand BrowseBackupCommand { get; }
    public AsyncCommand ReadBackupCommand { get; }
    public AsyncCommand CopyDeviceIdCommand { get; }

    public string SaveFolder
    {
        get => _saveFolder;
        set
        {
            if (SetField(ref _saveFolder, value))
                _ = LoadSaveFolderAsync();
        }
    }

    /// <summary>The save is locked to its device and none of the known keys unlocks it.</summary>
    public bool NeedsDeviceKey
    {
        get => _needsDeviceKey;
        private set => SetField(ref _needsDeviceKey, value);
    }

    /// <summary>The save opened with the fixed key.</summary>
    public bool UsesFixedKey => Save is { DeviceId: null };

    public bool IsFromBackup
    {
        get => _fromBackup;
        set { if (value) SetMode(true); }
    }

    public bool IsFromDeviceId
    {
        get => !_fromBackup;
        set { if (value) SetMode(false); }
    }

    public string BackupFolder
    {
        get => _backupFolder;
        set
        {
            if (SetField(ref _backupFolder, value))
                Refresh();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetField(ref _password, value))
                Refresh();
        }
    }

    public bool CanReadBackup => !IsLoading && !string.IsNullOrWhiteSpace(BackupFolder) && Password.Length > 0;

    public string DeviceId
    {
        get => _deviceId;
        set
        {
            if (SetField(ref _deviceId, value?.Trim() ?? string.Empty))
                _ = LoadSaveFolderAsync();
        }
    }

    public string FoundDeviceId
    {
        get => _foundDeviceId;
        private set
        {
            if (SetField(ref _foundDeviceId, value))
            {
                OnPropertyChanged(nameof(HasFoundDeviceId));
                OnPropertyChanged(nameof(FoundText));
                Refresh();
            }
        }
    }
    public bool HasFoundDeviceId => FoundDeviceId.Length > 0;

    public string FoundText => _deviceName.Length > 0 ? $"Device key from {_deviceName}:" : "Device key:";

    // Every key tried after the fixed key: from a backup read this session, typed in, or remembered.
    private IReadOnlyList<string> KnownKeys =>
        _backupKeys.Append(DeviceId).Append(Settings.IosDeviceId ?? string.Empty).Where(k => k.Length > 0).Distinct().ToList();

    public override void Initialize()
    {
        _backupFolder = Settings.LastIosBackup ?? SaveLocations.IosBackupRoot ?? string.Empty;
        _deviceId = Settings.IosDeviceId ?? string.Empty;
        OnPropertyChanged(nameof(BackupFolder));
        OnPropertyChanged(nameof(DeviceId));
        SaveFolder = Settings.LastIosSaveFolder ?? string.Empty;
        Refresh();
    }

    protected override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(CanReadBackup));
        OnPropertyChanged(nameof(UsesFixedKey));
        BrowseSaveFolderCommand.RaiseCanExecuteChanged();
        BrowseBackupCommand.RaiseCanExecuteChanged();
        ReadBackupCommand.RaiseCanExecuteChanged();
        CopyDeviceIdCommand.RaiseCanExecuteChanged();
    }

    private void SetMode(bool fromBackup)
    {
        if (_fromBackup == fromBackup)
            return;
        _fromBackup = fromBackup;
        OnPropertyChanged(nameof(IsFromBackup));
        OnPropertyChanged(nameof(IsFromDeviceId));
    }

    private async Task LoadSaveFolderAsync()
    {
        string folder = SaveFolder;
        if (!ConversionService.IsSaveFolder(folder))
        {
            NeedsDeviceKey = false;
            FoundDeviceId = string.Empty;
            Reset(string.IsNullOrWhiteSpace(folder) ? string.Empty
                : Directory.Exists(folder) ? "No save in this folder. Pick the game's SAVE folder."
                : "SAVE folder not found.");
            return;
        }

        var keys = KnownKeys;
        await ReadAsync(() => ConversionService.ReadIosSaveFolder(folder, keys), folder, "Couldn't read this SAVE folder");
    }

    // Reads the backup's keys and tries them on the SAVE folder.
    private async Task ReadBackupAsync()
    {
        if (IosBackupReader.FindBackupFolder(BackupFolder) is not { } backup)
        {
            Note = "No iOS backup here. Pick the backup's folder (it holds Manifest.plist).";
            return;
        }

        string password = Password;
        string folder = SaveFolder;
        string deviceName = string.Empty;
        await ReadAsync(() =>
        {
            var result = ConversionService.ReadIosBackup(backup, password, folder);
            _backupKeys = result.DeviceIds;
            deviceName = result.DeviceName;
            return result.Save;
        }, folder, "Couldn't unlock this SAVE folder");

        if (_backupKeys.Count > 0)
        {
            Password = string.Empty;
            Settings.LastIosBackup = backup;
            Settings.Save();
        }
        if (Save is { DeviceId: not null })
        {
            _deviceName = deviceName;
            OnPropertyChanged(nameof(FoundText));
        }
    }

    private async Task ReadAsync(Func<LoadedSave?> read, string folder, string failureText)
    {
        _keyNotFound = false;
        bool loaded = await LoadAsync(() =>
        {
            try
            {
                return read();
            }
            catch (SaveKeyNotFoundException)
            {
                _keyNotFound = true;
                throw;
            }
            catch (MatchedKeyException ex)
            {
                _matchedKey = ex.DeviceId;
                throw;
            }
        }, failureText);

        if (loaded && Save is { } save)
        {
            NeedsDeviceKey = false;
            Remember(save.DeviceId, folder);
            return;
        }

        FoundDeviceId = string.Empty;
        if (_keyNotFound)
        {
            NeedsDeviceKey = true;
            Note = "This save is locked to the device it came from, so it needs that device's key.";
        }
        else
        {
            ShowMatchedKey(folder);
        }
        Refresh();
    }

    // The key unlocked the save index but the files still couldn't be read. Show it and keep it anyway.
    private void ShowMatchedKey(string folder)
    {
        if (_matchedKey is not { } id)
            return;
        _matchedKey = null;
        Remember(id, folder);
        Note += $" The device key that matched is {id}. It was saved to {AppSettings.LastPath}.";
    }

    // Keeps the folder, and the device key if the save has one, for next time.
    private void Remember(string? deviceId, string folder)
    {
        FoundDeviceId = deviceId ?? string.Empty;
        if (deviceId is not null)
        {
            _deviceId = deviceId;
            OnPropertyChanged(nameof(DeviceId));
            Settings.IosDeviceId = deviceId;
        }
        Settings.LastIosSaveFolder = folder;
        Settings.Save();
        Refresh();
    }

    private async Task BrowseSaveFolderAsync()
    {
        string start = Directory.Exists(SaveFolder) ? SaveFolder : SaveLocations.Desktop;
        if (await Dialogs.PickFolderAsync("Choose the SAVE folder copied off the device", start) is { } folder)
            SaveFolder = folder;
    }

    private async Task BrowseBackupAsync()
    {
        string? start = Directory.Exists(BackupFolder) ? BackupFolder : SaveLocations.IosBackupRoot;
        if (await Dialogs.PickFolderAsync("Choose the iOS backup folder", start) is { } folder)
            BackupFolder = folder;
    }
}
