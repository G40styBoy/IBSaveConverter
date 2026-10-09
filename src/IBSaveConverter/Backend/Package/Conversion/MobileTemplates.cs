using System.Text;

namespace IBSaveEditor.Package.Conversion;

/// <summary>
/// The non-save files a mobile export zip needs when there is no zip to copy them from.
/// Copied from real IB3 and IB2 exports that the Android port imported successfully.
/// </summary>
internal static class MobileTemplates
{
    /// <summary>The Android port uses the same device ID on every install.</summary>
    public const string AndroidDeviceId = "1B3E0000-0000-4000-8000-00000000C0DE";

    /// <summary>
    /// The AES key for a mobile save. A device ID gives a device key; null gives the game's fixed IB3 key, which the
    /// PC version uses and iOS uses once the game has the fixed-key patch.
    /// </summary>
    public static byte[]? KeyFor(string? deviceId) => deviceId is null ? null : KeyFromDeviceId(deviceId);

    /// <summary>The AES key is the device ID without dashes, case kept, as 32 ASCII bytes.</summary>
    public static byte[] KeyFromDeviceId(string deviceId)
    {
        string compact = deviceId.Trim().Replace("-", string.Empty);
        if (compact.Length != 32)
            throw new ArgumentException($"Device ID must be 32 characters without dashes, got {compact.Length}.", nameof(deviceId));

        return Encoding.ASCII.GetBytes(compact);
    }

    public const string PortInfoPath = "ib-port-saves.txt";

    /// <summary>True when ib-port-saves.txt names a game other than <paramref name="expectedGame"/>.</summary>
    public static bool IsOtherGame(byte[] portInfo, string expectedGame) =>
        Encoding.UTF8.GetString(portInfo).Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("game=", StringComparison.Ordinal)) is { } line &&
        !string.Equals(line["game=".Length..], expectedGame, StringComparison.OrdinalIgnoreCase);
    public const string SaveFolder   = "userdata/Documents/SAVE/";
    public const string KeychainPath = "userdata/Library/keychain.plist";
    public const string PrefsPath    = "userdata/Library/Preferences/com.chairentertainment.IB3.plist";

    // Built with explicit "\n" so Git line-ending settings cannot change the bytes.
    public static byte[] PortInfo => Lf(
        "game=Infinity Blade III",
        "bundleId=com.chairentertainment.IB3",
        "app=1.8.1",
        "");

    public static byte[] Keychain(string deviceId) => Lf(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
        "<plist version=\"1.0\">",
        "<dict>",
        "\t<key>||</key>",
        $"\t<data>{Convert.ToBase64String(Encoding.ASCII.GetBytes(deviceId))}</data>",
        "</dict>",
        "</plist>",
        "");

    /// <summary>Mostly usage stats. Only CLOUD::UpgradeKey=1 matters: it marks the saves as already encrypted.</summary>
    public static byte[] Prefs => Lf(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
        "<plist version=\"1.0\">",
        "<dict>",
        Pair("IPhoneHome::LastRunCrashed", "0"),
        Pair("IPhoneHome::NumInvocations", "5"),
        Pair("IPhoneHome::TotalSurveys", "2"),
        Pair("IPhoneHome::LastFailure", "1791130466"),
        Pair("IPhoneHome::TotalSurveyFails", "2"),
        Pair("IPhoneHome::NumSurveyFailsSinceLast", "2"),
        Pair("CLOUD::UpgradeKey", "1"),
        Pair("DEFEATED0", "0"),
        Pair("IPhoneHome::LastTutorialContent", "0"),
        Pair("IPhoneHome::Gold", "2544"),
        Pair("IPhoneHome::Fight", "4"),
        Pair("IPhoneHome::GodKing", "0"),
        Pair("IPhoneHome::GodKingLose", "0"),
        Pair("IPhoneHome::MaxBloodLine", "1"),
        Pair("IPhoneHome::PlayerLevel", "2"),
        Pair("IPhoneHome::TProgress", "3256783550"),
        Pair("IPhoneHome::Option01", "0"),
        Pair("IPhoneHome::BattleStart", "5"),
        Pair("IPhoneHome::BloodLineS", "1"),
        Pair("IPhoneHome::AppPlaytimeSecs", "412"),
        "</dict>",
        "</plist>",
        "");

    // ---------- Infinity Blade II: the same zip layout and file paths (the prefs file is named IB3 there too) ----------

    public static byte[] Ib2PortInfo => Lf(
        "game=Infinity Blade II",
        "bundleId=com.chairentertainment.IB2",
        "app=1.7",
        "");

    /// <summary>From a real IB2 export, without the per-slot CLOUD:: keys, which are written for the slots in the save.</summary>
    public static byte[] Ib2Prefs => Lf(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
        "<plist version=\"1.0\">",
        "<dict>",
        Pair("IPhoneHome::LastRunCrashed", "0"),
        Pair("IPhoneHome::NumInvocations", "10"),
        Pair("IPhoneHome::TotalSurveys", "1"),
        Pair("IPhoneHome::LastFailure", "1791436456"),
        Pair("IPhoneHome::TotalSurveyFails", "1"),
        Pair("IPhoneHome::NumSurveyFailsSinceLast", "1"),
        Pair("CLOUD::UpgradeKey", "1"),
        Pair("IPhoneHome::AppPlaytimeSecs", "365"),
        Pair("IPhoneHome::BloodLineS", "2"),
        Pair("IPhoneHome::LastTutorialContent", "3"),
        Pair("IPhoneHome::Gold", "3054"),
        Pair("IPhoneHome::Fight", "5"),
        Pair("IPhoneHome::GodKing", "0"),
        Pair("IPhoneHome::GodKingLose", "0"),
        Pair("IPhoneHome::MaxBloodLine", "1"),
        Pair("IPhoneHome::MaxPlaythrough", "0"),
        Pair("IPhoneHome::PlayerLevel", "3"),
        Pair("IPhoneHome::TProgress", "3256783582"),
        Pair("IPhoneHome::Option01", "0"),
        Pair("IPhoneHome::BattleStart", "7"),
        Pair("IPhoneHome::LastTutorialContent2", "1"),
        "</dict>",
        "</plist>",
        "");

    private static string Pair(string key, string value) => $"\t<key>{key}</key>\n\t<string>{value}</string>";

    private static byte[] Lf(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\n", lines));
}
