using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels;

/// <summary>One character row in a slot list.</summary>
public sealed class SlotItem : ViewModelBase
{
    private readonly Action? _checkedChanged;
    private bool _isChecked;

    public SlotInfo Info { get; }
    public bool IsCurrent { get; }

    public SlotItem(SlotInfo info, bool isCurrent = false, Action? checkedChanged = null)
    {
        Info = info;
        IsCurrent = isCurrent;
        _checkedChanged = checkedChanged;
    }

    /// <summary>A single save file's character, kept loaded so it doesn't depend on the file staying put.</summary>
    public IBSaveEditor.Package.Conversion.SaveSlotData? Character { get; init; }

    /// <summary>Removes a single save file's row (only set for those rows).</summary>
    public System.Windows.Input.ICommand? RemoveCommand { get; init; }

    public bool IsFromFile => Info.SourceFile is not null;

    /// <summary>Ticked in a multi-pick list (PC -> Android).</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetField(ref _isChecked, value && IsSelectable))
                _checkedChanged?.Invoke();
        }
    }

    public int Index => Info.Index;
    public string Title => IsFromFile ? "File" : $"Slot {Info.Number}";
    public string Name => string.IsNullOrWhiteSpace(Info.CharacterName) ? "Unnamed character" : Info.CharacterName;
    public string Detail
    {
        get
        {
            string detail = string.IsNullOrEmpty(Info.Map) ? $"Level {Info.Level}" : $"Level {Info.Level}  ·  {MapLabel(Info.Map)}";
            return IsFromFile ? $"{detail}  ·  {Path.GetFileName(Info.SourceFile)}" : detail;
        }
    }
    public bool IsDeleted => Info.IsDeleted;
    public bool IsSelectable => !Info.IsDeleted;

    public string Badge => IsDeleted ? "Deleted" : string.Empty;
    public bool HasBadge => Badge.Length > 0;

    /// <summary>"A00_Beach_Nav" -> "A00 Beach", "E03_Throne_Nav_Isa" -> "E03 Throne Isa".</summary>
    public static string MapLabel(string map) =>
        string.Join(' ', map.Split('_', StringSplitOptions.RemoveEmptyEntries).Where(part => part != "Nav"));
}

/// <summary>A PC slot the Android character can be installed into.</summary>
public sealed class TargetSlotItem
{
    public int Index { get; }
    public SlotInfo? Occupant { get; }

    public TargetSlotItem(int index, SlotInfo? occupant)
    {
        Index = index;
        Occupant = occupant;
    }

    public bool ReplacesCharacter => Occupant is { IsDeleted: false };

    public string Label => Occupant switch
    {
        null => $"Slot {Index + 1}  ·  empty",
        { IsDeleted: true } => $"Slot {Index + 1}  ·  deleted character (will be replaced)",
        _ => $"Slot {Index + 1}  ·  replaces {Occupant.CharacterName}, level {Occupant.Level}",
    };
}

/// <summary>One row of a "will have" preview: where a character ends up, and where it came from.</summary>
public sealed record PlannedSlot(int Index, string Name, string Detail, string Origin, bool IsNew)
{
    public string Title => $"Slot {Index + 1}";

    public static PlannedSlot From(SlotInfo info, int index, string origin, bool isNew)
    {
        var item = new SlotItem(info);
        string name = info.IsDeleted ? $"{item.Name} (deleted)" : item.Name;
        return new PlannedSlot(index, name, item.Detail, origin, isNew);
    }
}
