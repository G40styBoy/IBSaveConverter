using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IBSaveConverter.Services;

/// <summary><see cref="SaveFolder"/> is null when the backup doesn't hold the game's saves.</summary>
public sealed record IosBackup(string? SaveFolder, string DeviceName, IReadOnlyList<string> DeviceIds);

public sealed class IosBackupException(string message) : Exception(message);

/// <summary>
/// Runs the Python helper (tools/ios-backup) that unlocks an encrypted iOS backup.
/// Uses iOSBackup.exe next to the app if it's there, otherwise iOSBackup.py with Python from PATH.
/// </summary>
public static class IosBackupReader
{
    private const string HELPER_EXE = "iOSBackup.exe";
    private const string HELPER_SCRIPT = "iOSBackup.py";
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromMinutes(10);

    public const string MissingHelperMessage =
        "Reading an iOS backup needs Python 3 with the iphone_backup_decrypt package (pip install iphone_backup_decrypt), " +
        "or iOSBackup.exe next to this app. You can also enter your device ID by hand.";

    /// <summary>Accepts a backup folder, or the folder holding several backups (the newest one is used).</summary>
    public static string? FindBackupFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;
        if (IsBackup(folder))
            return folder;

        return Directory.GetDirectories(folder)
            .Where(IsBackup)
            .OrderByDescending(dir => File.GetLastWriteTimeUtc(Path.Combine(dir, "Manifest.plist")))
            .FirstOrDefault();
    }

    public static IosBackup Extract(string backupFolder, string password, string outputFolder)
    {
        var start = HelperStartInfo() ?? throw new IosBackupException(MissingHelperMessage);
        start.ArgumentList.Add("--backup");
        start.ArgumentList.Add(backupFolder);
        start.ArgumentList.Add("--out");
        start.ArgumentList.Add(outputFolder);
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardInputEncoding = new UTF8Encoding(false);
        start.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = Process.Start(start) ?? throw new IosBackupException("The iOS helper didn't start.");
        // The password goes through stdin so it never shows up in a process list.
        process.StandardInput.WriteLine(password);
        process.StandardInput.Close();

        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(HelperTimeout))
        {
            process.Kill(entireProcessTree: true);
            throw new IosBackupException("Reading the backup took too long and was stopped.");
        }

        return ParseResult(output.Result, errors.Result);
    }

    private static IosBackup ParseResult(string output, string errors)
    {
        string? json = output.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.StartsWith('{'));
        if (json is null)
            throw new IosBackupException($"The iOS helper failed. {LastLine(errors)}".Trim());

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new IosBackupException(error.GetString() ?? "The iOS helper failed.");

        return new IosBackup(
            root.TryGetProperty("saveFolder", out var folder) ? folder.GetString() : null,
            root.TryGetProperty("deviceName", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            root.GetProperty("deviceIds").EnumerateArray().Select(id => id.GetString()!).ToList());
    }

    private static ProcessStartInfo? HelperStartInfo()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, HELPER_EXE);
        if (File.Exists(exe))
            return new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };

        string script = Path.Combine(AppContext.BaseDirectory, HELPER_SCRIPT);
        if (!File.Exists(script))
            return null;

        foreach (var (file, args) in PythonCommands())
        {
            if (!CanRun(file, args))
                continue;
            var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in args)
                start.ArgumentList.Add(arg);
            start.ArgumentList.Add(script);
            return start;
        }
        return null;
    }

    private static IEnumerable<(string File, string[] Args)> PythonCommands() => OperatingSystem.IsWindows()
        ? new[] { ("py", new[] { "-3" }), ("python", Array.Empty<string>()) }
        : new[] { ("python3", Array.Empty<string>()), ("python", Array.Empty<string>()) };

    private static bool CanRun(string file, string[] args)
    {
        try
        {
            var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in args)
                start.ArgumentList.Add(arg);
            start.ArgumentList.Add("--version");

            using var process = Process.Start(start);
            return process is not null && process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsBackup(string folder) => File.Exists(Path.Combine(folder, "Manifest.plist"));

    private static string LastLine(string text) =>
        text.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? string.Empty;
}
