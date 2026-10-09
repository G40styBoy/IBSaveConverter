using Avalonia.Controls;
using IBSaveConverter.Services;
using IBSaveConverter.ViewModels;

namespace IBSaveConverter.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(AppSettings.Load(), new WindowDialogService(this));
    }
}
