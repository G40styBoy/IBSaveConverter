using Avalonia.Controls;
using Avalonia.Platform.Storage;
using IBSaveConverter.Views;

namespace IBSaveConverter.Services;

public sealed class WindowDialogService : IDialogService
{
    private static readonly FilePickerFileType ZipFiles = new("Zip files") { Patterns = new[] { "*.zip" } };
    private static readonly FilePickerFileType SaveFiles = new("Save files") { Patterns = new[] { "*.bin" } };

    private readonly Window _owner;

    public WindowDialogService(Window owner) => _owner = owner;

    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await StartAt(startFolder),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickZipToOpenAsync(string title, string? startFolder)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { ZipFiles },
            SuggestedStartLocation = await StartAt(startFolder),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickZipToSaveAsync(string title, string suggestedFileName, string? startFolder)
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "zip",
            FileTypeChoices = new[] { ZipFiles },
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await StartAt(startFolder),
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickSaveFileAsync(string title, string? startFolder)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { SaveFiles, FilePickerFileTypes.All },
            SuggestedStartLocation = await StartAt(startFolder),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = new ConfirmDialog(title, message, confirmText);
        return await dialog.ShowDialog<bool>(_owner);
    }

    public void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                string args = File.Exists(path) || Directory.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
                System.Diagnostics.Process.Start("explorer.exe", args);
            }
            else
            {
                string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Convenience only.
        }
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (_owner.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private async Task<IStorageFolder?> StartAt(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;
        return await _owner.StorageProvider.TryGetFolderFromPathAsync(folder);
    }
}
