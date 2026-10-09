using System.Windows.Input;
using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels.Sources;

/// <summary>Loose save files, in any format the game's <see cref="GameSaves.SaveFileHint"/> lists.</summary>
public sealed class SaveFileSourceViewModel : SourceViewModel
{
    private readonly List<CharacterInfo> _files = new();

    public SaveFileSourceViewModel(GameSaves game, AppSettings settings, IDialogService dialogs, StatusMessage status) : base(game, settings, dialogs, status)
    {
        AddFileCommand = new AsyncCommand(AddFileAsync, () => !IsLoading);
    }

    public override SourceKind Kind => SourceKind.SaveFile;
    public override string Origin => "From save file";

    public AsyncCommand AddFileCommand { get; }

    /// <summary>The kinds of file this game's save file source takes.</summary>
    public string Hint => Game.SaveFileHint;

    public override ICommand? RemoveCommandFor(CharacterInfo character) =>
        new AsyncCommand(() =>
        {
            _files.Remove(character);
            SetCharacters(_files.ToList(), null);
            return Task.CompletedTask;
        });

    public async Task AddFileAsync(string path)
    {
        if (_files.Any(f => string.Equals(f.SourceFile, path, StringComparison.OrdinalIgnoreCase)))
            return;

        IsLoading = true;
        try
        {
            var character = await Task.Run(() => Game.ReadSaveFile(path));
            if (character.IsDeleted)
            {
                Note = "That save file holds a deleted character.";
                return;
            }
            _files.Add(character);
            Note = string.Empty;
            SetCharacters(_files.ToList(), null);
            Settings.LastZipFolder = Path.GetDirectoryName(path);
            Settings.Save();
        }
        catch (Exception ex)
        {
            Note = $"Couldn't use that save file: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected override void Refresh()
    {
        base.Refresh();
        AddFileCommand.RaiseCanExecuteChanged();
    }

    private async Task AddFileAsync()
    {
        if (await Dialogs.PickSaveFileAsync("Choose a save file", Settings.LastZipFolder ?? SaveLocations.Desktop) is { } path)
            await AddFileAsync(path);
    }
}
