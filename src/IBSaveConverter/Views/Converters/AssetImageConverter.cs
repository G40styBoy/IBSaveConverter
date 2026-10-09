using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace IBSaveConverter.Views.Converters;

/// <summary>Turns an avares:// path into an image, for icons named by a view model.</summary>
public sealed class AssetImageConverter : IValueConverter
{
    public static AssetImageConverter Instance { get; } = new();

    private readonly Dictionary<string, Bitmap> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0)
            return null;
        if (!_cache.TryGetValue(path, out var bitmap))
            _cache[path] = bitmap = new Bitmap(AssetLoader.Open(new Uri(path)));
        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
