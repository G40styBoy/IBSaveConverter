using Avalonia.Controls;
using IBSaveConverter.Services;
using IBSaveConverter.ViewModels;

namespace IBSaveConverter;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainWindowViewModel(AppSettings.Load(), new WindowDialogService(this));
        DataContext = viewModel;
        Opened += (_, _) => viewModel.Initialize();
    }
}
