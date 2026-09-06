using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AeroVisApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly DispatcherTimer _clockTimer;
    private SettingsWindow? _settingsWindow;

    private bool _isPlaying = false;
    private TimeSpan _elapsed = TimeSpan.Zero;
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DispatcherTimer? _hideTimerDelay;

    public MainWindow()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _sessionTimer.Tick += (_, _) =>
        {
            _elapsed = _elapsed.Add(TimeSpan.FromSeconds(1));
            TimerText.Text = _elapsed.ToString(@"mm\:ss");
        };

        ThemeManager.ThemeChanged += isDark => ApplyIconTheme(isDark);
        ApplyIconTheme(ThemeManager.IsDark);
        SettingsService.SettingsChanged += _ => UpdateClock();
    }

    private void ApplyIconTheme(bool isDark)
    {
        var p = isDark ? "pack://application:,,,/res/window/dark/" : "pack://application:,,,/res/window/";
        Uri U(string name) => new($"{p}{name}");

        AppBanner.Source    = new System.Windows.Media.Imaging.BitmapImage(
            new Uri(isDark
                ? "pack://application:,,,/res/window/dark/AeroVis-Banner-White.png"
                : "pack://application:,,,/res/window/AeroVis-Banner-Orange.png"));

        TimerIcon.Source      = U("timer.svg");
        PlayPauseIcon.Source  = U("play.svg");
        StopIcon.Source       = U("square.svg");
        SettingsIcon.Source   = U("settings.svg");
        NavGaugeIcon.Source   = U("gauge.svg");
        NavCarIcon.Source     = U("car.svg");
        NavHistoryIcon.Source = U("rotate-ccw-clock.svg");
        DropletIcon.Source    = U("droplet.svg");
        FanIcon.Source        = U("fan.svg");
        ThermometerIcon.Source = U("thermometer.svg");
        SidebarGaugeIcon.Source = U("gauge.svg");

        var dp = isDark ? "pack://application:,,,/res/datapanel/dark/" : "pack://application:,,,/res/datapanel/";
        Uri DP(string name) => new($"{dp}{name}");
        TileTempIcon.Source     = DP("thermometer.svg");
        TileXForceIcon.Source   = DP("arrow-right.svg");
        TileWindIcon.Source     = DP("wind.svg");
        TileHumidityIcon.Source = DP("droplets.svg");
        TileZForceIcon.Source   = DP("arrow-down.svg");
        TileDragIcon.Source     = DP("wind-arrow-down.svg");
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var s = SettingsService.Current;
        LiveDate.Text = now.ToString(s.DateFormat == "D/M/Y" ? "d/M/yyyy" : "dd MMM yyyy");
        LiveTime.Text = now.ToString(s.TimeFormat == "12-Hour" ? "hh:mm:ss tt" : "HH:mm:ss");
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying) return;

        _isPlaying = true;
        _hideTimerDelay?.Stop();
        SavingText.Visibility = Visibility.Collapsed;
        TimerPanel.Visibility = Visibility.Visible;
        _sessionTimer.Start();
        StopButton.Visibility = Visibility.Visible;
        PlayPauseButton.Visibility = Visibility.Collapsed;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        _sessionTimer.Stop();
        var prefix = ThemeManager.IsDark ? "pack://application:,,,/res/window/dark/" : "pack://application:,,,/res/window/";
        PlayPauseIcon.Source = new Uri($"{prefix}play.svg");
        PlayPauseButton.ToolTip = "Play";
        PlayPauseButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        SavingText.Visibility = Visibility.Visible;

        _hideTimerDelay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _hideTimerDelay.Tick += (_, _) =>
        {
            _hideTimerDelay.Stop();
            SavingText.Visibility = Visibility.Collapsed;
            TimerPanel.Visibility = Visibility.Collapsed;
            _elapsed = TimeSpan.Zero;
            TimerText.Text = "00:00";
        };
        _hideTimerDelay.Start();
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (PageManualSimulation == null) return; // called before InitializeComponent
        PageManualSimulation.Visibility = sender == NavManualSimulation ? Visibility.Visible : Visibility.Collapsed;
        PageMyCars.Visibility           = sender == NavMyCars           ? Visibility.Visible : Visibility.Collapsed;
        PageHistory.Visibility          = sender == NavHistory          ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Close();
            _settingsWindow = null;
            return;
        }

        _settingsWindow = new SettingsWindow { Owner = this };

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }
}