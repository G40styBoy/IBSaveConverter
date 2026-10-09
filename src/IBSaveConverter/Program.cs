using Avalonia;
using IBSaveConverter.Services;

namespace IBSaveConverter;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // A windowed exe has no console, so without this a crash closes the app silently.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashReport.Write(e.ExceptionObject as Exception);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashReport.Write(ex);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
