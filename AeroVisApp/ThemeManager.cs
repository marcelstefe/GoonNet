using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace AeroVisApp;

public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static event Action<bool>? ThemeChanged;

    public static void Apply(bool isDark)
    {
        IsDark = isDark;
        var r = Application.Current.Resources;

        if (isDark)
        {
            r["AppCardBg"]           = Brush(0x0c, 0x0c, 0x0c);
            r["AppNavActive"]        = Brush(0x38, 0x38, 0x38);
            r["AppNavHover"]         = Brush(0x2d, 0x2d, 0x2d);
            r["AppNavPressed"]       = Brush(0x2d, 0x2d, 0x2d);
            r["AppNavActiveHover"]   = Brush(0x2d, 0x2d, 0x2d);
            r["AppSubtleBg"]         = Brush(0x2a, 0x2a, 0x2a);
            r["AppSubtleHover"]      = Brush(0x36, 0x36, 0x36);
            r["AppNavBtnHover"]      = Brush(0x2d, 0x2d, 0x2d);
            r["AppSidebarValue"]     = new SolidColorBrush(Colors.White);
            r["AppProgressTrack"]    = Brush(0x0d, 0x2a, 0x45);
            r["AppDividerLine"]      = Brush(0x99, 0x99, 0x99);
            r["AppTabActive"]        = new SolidColorBrush(Colors.White);
            r["AppNavText"]          = new SolidColorBrush(Colors.White);
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }
        else
        {
            r["AppCardBg"]           = Brush(0xFE, 0xFE, 0xFE);
            r["AppNavActive"]        = new SolidColorBrush(Colors.White);
            r["AppNavHover"]         = Brush(0xef, 0xef, 0xef);
            r["AppNavPressed"]       = Brush(0xdc, 0xdc, 0xdc);
            r["AppNavActiveHover"]   = Brush(0xd6, 0xe2, 0xf0);
            r["AppSubtleBg"]         = Brush(0xF5, 0xF5, 0xF5);
            r["AppSubtleHover"]      = Brush(0xE8, 0xE8, 0xE8);
            r["AppNavBtnHover"]      = Brush(0xf2, 0xf2, 0xf2);
            r["AppSidebarValue"]     = Brush(0x44, 0x44, 0x44);
            r["AppProgressTrack"]    = Brush(0xB0, 0xC4, 0xDE);
            r["AppDividerLine"]      = Brush(0xd0, 0xd0, 0xd0);
            r["AppTabActive"]        = Brush(0x22, 0x22, 0x22);
            r["AppNavText"]          = Brush(0x1a, 0x1a, 0x1a);
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        }

        ThemeChanged?.Invoke(isDark);
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
        => new(Color.FromRgb(r, g, b));
}
