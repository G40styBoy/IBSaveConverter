namespace IBSaveConverter.ViewModels;

public enum StatusKind
{
    None,
    Info,
    Success,
    Error,
}

public sealed class StatusMessage : ViewModelBase
{
    private string _text = string.Empty;
    private StatusKind _kind;

    public string Text
    {
        get => _text;
        private set => SetField(ref _text, value);
    }

    public StatusKind Kind
    {
        get => _kind;
        private set
        {
            if (SetField(ref _kind, value))
            {
                OnPropertyChanged(nameof(IsSuccess));
                OnPropertyChanged(nameof(IsError));
                OnPropertyChanged(nameof(IsVisible));
            }
        }
    }

    public bool IsSuccess => Kind is StatusKind.Success;
    public bool IsError => Kind is StatusKind.Error;
    public bool IsVisible => Kind is not StatusKind.None;

    public void Set(StatusKind kind, string text)
    {
        Text = text;
        Kind = kind;
    }

    public void Clear() => Set(StatusKind.None, string.Empty);
}
