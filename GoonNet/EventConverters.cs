using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GoonNet;

/// <summary>
/// Maps a track's Event/type name to the matching icon under /res/events.
/// </summary>
public class EventIconConverter : IValueConverter
{
    private static readonly Dictionary<string, string> IconByType = new()
    {
        ["song"] = "music.svg",
        ["station id"] = "station-id.svg",
        ["promo"] = "promotion.svg",
        ["commercial"] = "commercial.svg",
        ["news"] = "news.svg",
        ["bed"] = "bed.svg",
        ["voice track"] = "voice-track.svg",
        ["command"] = "command.svg",
    };

    private static readonly Dictionary<string, Uri?> UriCache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string ?? string.Empty).Trim().ToLowerInvariant();
        if (!IconByType.TryGetValue(key, out var file)) return null;

        if (!UriCache.TryGetValue(file, out var uri))
        {
            uri = Resolve(file);
            UriCache[file] = uri;
        }
        return uri;
    }

    // Only hand SvgViewbox a Uri when the resource is actually embedded; otherwise
    // it throws an unhandled SvgErrorException and crashes the app. Missing -> blank cell.
    private static Uri? Resolve(string file)
    {
        var uri = new Uri($"pack://application:,,,/res/events/{file}", UriKind.Absolute);
        try
        {
            return System.Windows.Application.GetResourceStream(uri) != null ? uri : null;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a track's Event/type name to the foreground colour used for its row content.
/// </summary>
public class EventColorConverter : IValueConverter
{
    private static readonly Brush DefaultBrush = Make("#FFFFFF");

    private static readonly Dictionary<string, Brush> BrushByType = new()
    {
        ["song"] = Make("#FFFF00"),
        ["station id"] = Make("#00FFFF"),
        ["promo"] = Make("#FF1493"),
        ["commercial"] = Make("#00FF00"),
        ["news"] = Make("#9400D3"),
        ["bed"] = Make("#FF7F00"),
        ["voice track"] = Make("#FF0000"),
        ["command"] = Make("#FFFFFF"),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string ?? string.Empty).Trim().ToLowerInvariant();
        return BrushByType.TryGetValue(key, out var brush) ? brush : DefaultBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush Make(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
