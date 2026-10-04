using System.Collections.Concurrent;
using System.Reflection;
using IBSaveEditor.Package;

namespace IBSaveEditor.Enums;

// Maps enum-indexed static save arrays (NumConsumable, ShowConsumableBadge, SavedCheevo, LastVoteCount) to/from integer indices.
public static class IBEnum
{
    private const string CONSUMABLE      = "NumConsumable";
    private const string SHOW_CONSUMABLE = "ShowConsumableBadge";
    private const string CHEEVO          = "SavedCheevo";
    private const string VOTE_COUNT      = "LastVoteCount";

    private static Game _game;

    public static void SetGame(Game game) => _game = game;

    // Index -> enum name for the current game/alias. Falls back to "Element{idx+1}" if unregistered.
    public static string GetEnumEntryFromIndex(string alias, int idx)
    {
        return (_game, alias) switch
        {
            (Game.IB1 or Game.IB2, CONSUMABLE)      => EnumToString<eTouchRewardActor_IB2>(idx),
            (Game.IB1 or Game.IB2, CHEEVO)          => EnumToString<eAchievements_IB2>(idx),
            (Game.IB3,             CONSUMABLE)      => EnumToString<eTouchRewardActor_IB3>(idx),
            (Game.IB3,             SHOW_CONSUMABLE) => EnumToString<eTouchRewardActor_IB3>(idx),
            (Game.IB3,             CHEEVO)          => EnumToString<eAchievements_IB3>(idx),
            (Game.VOTE,            VOTE_COUNT)      => EnumToString<CharacterFilterEnum>(idx),
            _                                        => $"Element{idx + 1}"
        };
    }

    // Enum type that provides index keys for the given alias/game.
    public static Type GetArrayIndexEnum(string alias)
    {
        return (_game, alias) switch
        {
            (Game.IB1 or Game.IB2, CONSUMABLE)      => typeof(eTouchRewardActor_IB2),
            (Game.IB1 or Game.IB2, CHEEVO)          => typeof(eAchievements_IB2),
            (Game.IB3,             CONSUMABLE)      => typeof(eTouchRewardActor_IB3),
            (Game.IB3,             SHOW_CONSUMABLE) => typeof(eTouchRewardActor_IB3),
            (Game.IB3,             CHEEVO)          => typeof(eAchievements_IB3),
            (Game.VOTE,            VOTE_COUNT)      => typeof(CharacterFilterEnum),
            _ => throw new InvalidDataException($"No array index enum is registered for '{alias}' in game {_game}.")
        };
    }

    // Enum value name -> zero-based index.
    public static int GetArrayIndexFromEnum<T>(string fName) where T : Enum
    {
        var idx = Array.IndexOf(Enum.GetNames(typeof(T)), fName);
        if (idx < 0)
            throw new InvalidDataException($"'{fName}' is not defined in {typeof(T).Name}.");

        return idx;
    }

    private static readonly ConcurrentDictionary<Type, MethodInfo> _arrayIndexMethods = new();

    // Reflection entry point for GetArrayIndexFromEnum<T> when T is only known at runtime.
    public static int GetArrayIndexUsingReflection(Type enumType, string value)
    {
        var method = _arrayIndexMethods.GetOrAdd(enumType,
            t => typeof(IBEnum).GetMethod(nameof(GetArrayIndexFromEnum))!.MakeGenericMethod(t));

        try
        {
            return (int?)method.Invoke(null, [value]) ?? -1;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(ex.Message);
        }
    }

    private static string EnumToString<T>(int idx) where T : Enum
        => ((T)(object)idx).ToString();
}
