using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(AppSettings settings, IDialogService dialogs)
    {
        PcToAndroid = new PcToAndroidViewModel(settings, dialogs);
        AndroidToPc = new AndroidToPcViewModel(settings, dialogs);
    }

    public PcToAndroidViewModel PcToAndroid { get; }
    public AndroidToPcViewModel AndroidToPc { get; }

    public void Initialize()
    {
        PcToAndroid.Initialize();
        AndroidToPc.Initialize();
    }
}
