using System;
using System.Windows;
using System.Windows.Media;

namespace AeroVisApp;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        ApplyTheme(ThemeManager.IsDark);
    }

    private void ApplyTheme(bool isDark)
    {
        if (isDark)
        {
            SplashRoot.Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0c, 0x0c));
            SplashBanner.Source = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/res/window/dark/AeroVis-Banner-White.png"));
        }
        else
        {
            SplashRoot.Background = new SolidColorBrush(Colors.White);
            SplashBanner.Source = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/res/window/AeroVis-Banner-Orange.png"));
        }
    }
}
