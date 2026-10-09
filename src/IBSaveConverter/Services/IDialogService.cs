namespace IBSaveConverter.Services;

public interface IDialogService
{
    Task<string?> PickFolderAsync(string title, string? startFolder);
    Task<string?> PickZipToOpenAsync(string title, string? startFolder);
    Task<string?> PickZipToSaveAsync(string title, string suggestedFileName, string? startFolder);
    Task<string?> PickSaveFileAsync(string title, string? startFolder);

    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    void Reveal(string path);

    Task CopyToClipboardAsync(string text);
}
