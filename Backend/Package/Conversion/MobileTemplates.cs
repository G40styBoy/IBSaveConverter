using System.Text;

namespace IBSaveEditor.Package.Conversion;

/// <summary>
/// The non-save files a mobile export zip needs. Used when there is no mobile zip to copy them from.
/// These are the exact files from a real export that the Android port imported successfully.
/// </summary>
internal static class MobileTemplates
{
    /// <summary>The Android port uses this same device ID on every install.</summary>
    public const string FixedDeviceId = "1B3E0000-0000-4000-8000-00000000C0DE";

    /// <summary>
    /// Turns a device ID into the AES key: dashes removed, letter case kept, used as 32 ASCII bytes.
    /// </summary>
    public static byte[] KeyFromDeviceId(string deviceId)
    {
        string compact = deviceId.Trim().Replace("-", string.Empty);
        if (compact.Length != 32)
            throw new ArgumentException($"Device ID must be 32 characters without dashes, got {compact.Length}.", nameof(deviceId));

        return Encoding.ASCII.GetBytes(compact);
    }

    public const string PortInfoPath = "ib-port-saves.txt";
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

    /// <summary>
    /// The game's preferences. Almost all values are "IPhoneHome" usage stats; the one that matters is
    /// CLOUD::UpgradeKey=1, which says the saves already use the encrypted format.
    /// </summary>
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

    private static string Pair(string key, string value) => $"\t<key>{key}</key>\n\t<string>{value}</string>";

    private static byte[] Lf(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\n", lines));
}
