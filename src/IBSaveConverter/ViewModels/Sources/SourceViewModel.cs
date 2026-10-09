using System.Windows.Input;
using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels.Sources;

public enum SourceKind
{
    Pc,
    Android,
    Ios,
    SaveFile,
}

/// <summary>Where the characters come from. Each kind has its own panel; the main view model lists their characters.</summary>
public abstract class SourceViewModel : ViewModelBase
{
    private IReadOnlyList<CharacterInfo> _characters = Array.Empty<CharacterInfo>();
    private LoadedSave? _save;
    private string _note = string.Empty;
    private bool _isLoading;
    private int _loadVersion;

    protected SourceViewModel(GameSaves game, AppSettings settings, IDialogService dialogs, StatusMessage status)
    {
        Game = game;
        Settings = settings;
        Dialogs = dialogs;
        Status = status;
        ExportCommand = new AsyncCommand(ExportAsync, () => CanExport);
    }

    public event Action? CharactersChanged;

    public abstract SourceKind Kind { get; }

    /// <summary>Shown in the "will have" preview, such as "From PC".</summary>
    public abstract string Origin { get; }

    public IReadOnlyList<CharacterInfo> Characters => _characters;

    /// <summary>The loaded save set, or null for single save files.</summary>
    public LoadedSave? Save => _save;

    public string Note
    {
        get => _note;
        protected set
        {
            if (SetField(ref _note, value))
                OnPropertyChanged(nameof(HasNote));
        }
    }
    public bool HasNote => Note.Length > 0;

    public bool IsLoading
    {
        get => _isLoading;
        protected set
        {
            if (SetField(ref _isLoading, value))
                Refresh();
        }
    }

    public bool CanExport => !IsLoading && _save is not null;

    public AsyncCommand ExportCommand { get; }

    /// <summary>The game whose saves are read.</summary>
    protected GameSaves Game { get; }
    protected AppSettings Settings { get; }
    protected IDialogService Dialogs { get; }
    protected StatusMessage Status { get; }

    public virtual void Initialize() { }

    /// <summary>Only save file rows can be removed one by one.</summary>
    public virtual ICommand? RemoveCommandFor(CharacterInfo character) => null;

    protected void SetCharacters(IReadOnlyList<CharacterInfo> characters, LoadedSave? save)
    {
        _characters = characters;
        _save = save;
        Refresh();
        CharactersChanged?.Invoke();
    }

    protected void ClearCharacters() => SetCharacters(Array.Empty<CharacterInfo>(), null);

    /// <summary>
    /// Reads a save set off the UI thread. A newer load makes an older one's result be ignored.
    /// A null result is not an error; it just leaves the list empty.
    /// </summary>
    protected async Task<bool> LoadAsync(Func<LoadedSave?> read, string failureText)
    {
        int version = ++_loadVersion;
        ClearCharacters();
        Note = string.Empty;
        IsLoading = true;
        try
        {
            var save = await Task.Run(read);
            if (version != _loadVersion)
                return false;
            if (save is null)
                return true;

            SetCharacters(save.Characters, save);
            Note = save.Characters.Count == 0 ? "This save has no characters."
                : save.Characters.All(c => c.IsDeleted) ? "Every character in this save is deleted."
                : string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
                Note = $"{failureText}: {ex.Message}";
            return false;
        }
        finally
        {
            if (version == _loadVersion)
                IsLoading = false;
        }
    }

    /// <summary>Drops the current characters and any load still running.</summary>
    protected void Reset(string note)
    {
        ++_loadVersion;
        IsLoading = false;
        ClearCharacters();
        Note = note;
    }

    protected virtual void Refresh()
    {
        OnPropertyChanged(nameof(CanExport));
        ExportCommand.RaiseCanExecuteChanged();
    }

    private async Task ExportAsync()
    {
        if (_save is not { } save)
            return;
        if (await Dialogs.PickFolderAsync("Choose where to put the decrypted copy", Settings.LastZipFolder ?? SaveLocations.Desktop) is not { } parent)
            return;

        string folder = Path.Combine(parent, $"{Game.ShortName} decrypted save {DateTime.Now:yyyy-MM-dd HHmmss}");
        try
        {
            await Task.Run(() => Game.ExportDecrypted(save, folder));
            Status.Set(StatusKind.Success, $"Saved a decrypted copy to \"{folder}\".");
            Dialogs.Reveal(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status.Set(StatusKind.Error, $"Couldn't save the decrypted copy: {ex.Message}");
        }
    }
}
