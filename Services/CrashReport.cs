using System.Runtime.InteropServices;

namespace IBSaveConverter.Services;

/// <summary>
/// Saves the details of a crash to crash.log and tells the user where it is. The app is a windowed exe with
/// no console, so without this a crash at startup just closes it with no message. The message box uses
/// Windows directly, so it still shows when the crash happens before Avalonia has started.
/// </summary>
public static class CrashReport
{
    private const string FILE_NAME = "crash.log";
    private static int _reported;

    public static void Write(Exception? ex)
    {
        if (Interlocked.Exchange(ref _reported, 1) == 1)
            return; // Only report the first crash.

        string text =
            $"IBSaveConverter crashed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
            $"App: {typeof(CrashReport).Assembly.GetName().Version}  .NET: {Environment.Version}  OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}){Environment.NewLine}" +
            $"Folder: {AppContext.BaseDirectory}{Environment.NewLine}{Environment.NewLine}" +
            (ex?.ToString() ?? "Unknown error");

        string? path = Save(text);

        string message = $"{ex?.GetType().Name}: {ex?.Message}{Environment.NewLine}{Environment.NewLine}" +
                         (path is null ? "The details couldn't be saved." : $"The details were saved to:{Environment.NewLine}{path}");
        try
        {
            if (OperatingSystem.IsWindows())
                MessageBoxW(IntPtr.Zero, message, "IB3 Save Converter hit an error", 0x10 /* MB_ICONERROR */);
            else
                Console.Error.WriteLine(text);
        }
        catch
        {
            // Nothing more we can do; the log file is the fallback.
        }
    }

    private static string? Save(string text)
    {
        string[] paths =
        {
            Path.Combine(AppContext.BaseDirectory, FILE_NAME),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IBSaveConverter", FILE_NAME),
            Path.Combine(Path.GetTempPath(), "IBSaveConverter-" + FILE_NAME),
        };
        foreach (string path in paths)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
                return path;
            }
            catch
            {
                // Not writable: try the next place.
            }
        }
        return null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
