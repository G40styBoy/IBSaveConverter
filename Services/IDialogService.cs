namespace IBSaveConverter.Services;

/// <summary>
/// File pickers and prompts the view models need. The window provides the real ones;
/// tests can provide fakes.
/// </summary>
public interface IDialogService
{
    Task<string?> PickFolderAsync(string title, string? startFolder);
    Task<string?> PickZipToOpenAsync(string title, string? startFolder);
    Task<string?> PickZipToSaveAsync(string title, string suggestedFileName, string? startFolder);
    Task<string?> PickSaveFileAsync(string title, string? startFolder);

    /// <summary>A yes/no question. Returns true only if the user confirms.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>Opens a file manager window with the file or folder selected.</summary>
    void Reveal(string path);
}
