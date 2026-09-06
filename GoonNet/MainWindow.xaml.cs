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

    public MainWindow()
    {
        InitializeComponent();

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
        LiveDate.Text = now.ToString(s.DateFormat == "D/M/Y" ? "d/M/yyyy" : "dd MMM yyyy");
        LiveTime.Text = now.ToString(s.TimeFormat == "12-Hour" ? "hh:mm:ss tt" : "HH:mm:ss");
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
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