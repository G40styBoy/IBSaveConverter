using System.Windows.Input;
using IBSaveConverter.Services;

namespace IBSaveConverter.ViewModels;

/// <summary>One row in the characters list.</summary>
public sealed class CharacterItem : ViewModelBase
{
    private readonly Action _checkedChanged;
    private bool _isChecked;

    public CharacterItem(CharacterInfo info, Action checkedChanged, ICommand? removeCommand = null)
    {
        Info = info;
        _checkedChanged = checkedChanged;
        RemoveCommand = removeCommand;
    }

    public CharacterInfo Info { get; }
    public ICommand? RemoveCommand { get; }
    public bool CanRemove => RemoveCommand is not null;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetField(ref _isChecked, value && IsSelectable))
                _checkedChanged();
        }
    }

    public bool IsSelectable => !Info.IsDeleted;
    public string Title => Info.SourceFile is not null ? "File" : $"Slot {Info.Slot + 1}";
    public string Name => Info.Name;
    public string Detail => DetailOf(Info);
    public string Badge => Info.IsDeleted ? "Deleted" : string.Empty;
    public bool HasBadge => Badge.Length > 0;

    public static string DetailOf(CharacterInfo info)
    {
        string detail = string.IsNullOrEmpty(info.Map) ? $"Level {info.Level}" : $"Level {info.Level}, {MapLabel(info.Map)}";
        return info.SourceFile is null ? detail : $"{detail}, {Path.GetFileName(info.SourceFile)}";
    }

    /// <summary>"A00_Beach_Nav" -> "A00 Beach", "E03_Throne_Nav_Isa" -> "E03 Throne Isa", IB2's "03_P_Forest" -> "03 Forest".</summary>
    public static string MapLabel(string map) =>
        string.Join(' ', map.Split('_', StringSplitOptions.RemoveEmptyEntries)
                            .Where(part => !part.Equals("Nav", StringComparison.OrdinalIgnoreCase) && !part.Equals("P", StringComparison.OrdinalIgnoreCase)));
}

/// <summary>A PC slot a single character can be put into.</summary>
public sealed class TargetSlotItem
{
    public TargetSlotItem(int index, CharacterInfo? occupant)
    {
        Index = index;
        Occupant = occupant;
    }

    public int Index { get; }
    public CharacterInfo? Occupant { get; }

    public bool ReplacesCharacter => Occupant is { IsDeleted: false };

    public string Label => Occupant switch
    {
        null => $"Slot {Index + 1}: empty",
        { IsDeleted: true } => $"Slot {Index + 1}: deleted character (will be replaced)",
        _ => $"Slot {Index + 1}: replaces {Occupant.Name}, level {Occupant.Level}",
    };
}

/// <summary>One row of a "will have" preview.</summary>
public sealed record PlannedSlot(int Index, string Name, string Detail, string Origin, bool IsNew)
{
    public string Title => $"Slot {Index + 1}";

    public static PlannedSlot From(CharacterInfo info, int index, string origin, bool isNew)
    {
        string name = info.Name;
        return new PlannedSlot(index, info.IsDeleted ? $"{name} (deleted)" : name, CharacterItem.DetailOf(info), origin, isNew);
    }
}
