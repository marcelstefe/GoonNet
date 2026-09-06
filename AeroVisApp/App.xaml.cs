using System;
using System.Threading.Tasks;
using System.Windows;

namespace AeroVisApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        var settings = SettingsService.Load();
        ThemeManager.Apply(settings.DarkMode);

        var splash = new SplashWindow();
        splash.Show();

        await Task.Delay(TimeSpan.FromSeconds(2));

        var main = new MainWindow();
        main.Show();
        splash.Close();
    }
}