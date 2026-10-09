namespace IBSaveEditor.Package;

public static class PackageConstants
{
    /// <summary>The first int32 of an unencrypted single save file.</summary>
    public const int UNENCRYPTED_SAVE_VERSION = 5;

    public const uint IB2_SAVE_MAGIC = 709824353u;
    public const uint IB3_SAVE_MAGIC = 541812089u;
    public const uint NO_MAGIC = 4294967295u;
}

public enum Game
{
    IB2,
    IB3
}
