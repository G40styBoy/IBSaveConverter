using System.Text;

namespace IBSaveEditor.Package.Conversion;

/// <summary>
/// Writes a save set with nothing encrypted or compressed:
///   Files/        every file's plain payload, under its original name
///   Characters/   each character as an unencrypted save (the format the PC game and the editor import)
///   device-id.txt the key the set was encrypted with, when there is one
/// </summary>
public static class DecryptedExport
{
    public static void Write(ICharacterSource source, string folder, string? deviceId = null)
    {
        string files = Path.Combine(folder, "Files");
        string characters = Path.Combine(folder, "Characters");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(characters);

        foreach (var (name, payload) in source.Files)
            File.WriteAllBytes(Path.Combine(files, name), payload);

        foreach (int slot in source.Slots)
        {
            SaveSlotData character = source.GetSlot(slot);
            string name = $"Slot {slot + 1} - {SafeFileName(character.Info.DisplayName)}.bin";
            File.WriteAllBytes(Path.Combine(characters, name), SingleSaveFile.ToUnencrypted(character.Save));
        }

        if (deviceId is not null)
            File.WriteAllText(Path.Combine(folder, "device-id.txt"), deviceId + Environment.NewLine, Encoding.ASCII);
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        string safe = new(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Unnamed" : safe.Trim();
    }
}
