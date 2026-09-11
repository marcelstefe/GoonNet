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

namespace GoonNet;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly DispatcherTimer _clockTimer;
    private SettingsWindow? _settingsWindow;

    public System.Collections.ObjectModel.ObservableCollection<LibraryTrack> LibraryTracks { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        LibraryGrid.ItemsSource = LibraryTracks;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        SettingsService.SettingsChanged += _ => UpdateClock();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var s = SettingsService.Current;
        LiveDate.Text = now.ToString(s.DateFormat == "D/M/Y" ? "dddd d/M/yyyy" : "dddd dd MMM yyyy");
        LiveTime.Text = now.ToString(s.TimeFormat == "12-Hour" ? "hh:mm:ss tt" : "HH:mm:ss");
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
    }

    private bool _isManualMode = true;
    private bool _isDlsOn = false;

    private void DlsButton_Click(object sender, RoutedEventArgs e)
    {
        _isDlsOn = !_isDlsOn;
        if (_isDlsOn)
        {
            DlsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ef4444"));
        }
        else
        {
            DlsButton.Background = new SolidColorBrush(Colors.Black);
        }
    }

    private void ManualModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isManualMode = !_isManualMode;
        if (_isManualMode)
        {
            ManualModeButton.Content = "Manual";
            ManualModeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#eab308"));
        }
        else
        {
            ManualModeButton.Content = "Auto";
            ManualModeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22c55e"));
        }
    }

    private ImportFileWindow? _importFileWindow;

    private void ImportFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_importFileWindow is { IsVisible: true })
        {
            _importFileWindow.Activate();
            return;
        }

        var win = new ImportFileWindow { Owner = this };
        _importFileWindow = win;
        win.Closed += (_, _) => _importFileWindow = null;
        if (win.ShowDialog() == true && win.Result is { } track)
        {
            LibraryTracks.Add(track);
        }
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