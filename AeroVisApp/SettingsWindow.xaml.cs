using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace AeroVisApp;

public partial class SettingsWindow : FluentWindow
{
    private static readonly SolidColorBrush SaveActiveBrush   = new(Color.FromRgb(0x00, 0x67, 0xc0));
    private static readonly SolidColorBrush SaveHoverBrush    = new(Color.FromRgb(0x00, 0x55, 0x9f));
    private static readonly SolidColorBrush SaveDisabledBrush = new(Color.FromRgb(0x88, 0x88, 0x88));

    private bool _loading = true;
    private bool _isDirty;

    public SettingsWindow()
    {
        InitializeComponent();
        ApplySettingsIconTheme(ThemeManager.IsDark);
        ThemeManager.ThemeChanged += ApplySettingsIconTheme;
        LoadSettingsIntoUi();
        SetSavedState();
    }

    private void LoadSettingsIntoUi()
    {
        _loading = true;
        var s = SettingsService.Current;
        DarkModeToggle.IsChecked        = s.DarkMode;
        ThrottleSensitivityDropDown.Content = s.ThrottleSensitivity;
        FoggerPowerDropDown.Content     = s.FoggerPower;
        _loading = false;
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _isDirty = true;
        SaveButtonText.Text = "Save";
        SaveButton.Background = SaveActiveBrush;
        SaveButton.Cursor = System.Windows.Input.Cursors.Hand;
    }

    private void SetSavedState()
    {
        _isDirty = false;
        SaveButtonText.Text = "Saved";
        SaveButton.Background = SaveDisabledBrush;
        SaveButton.Cursor = System.Windows.Input.Cursors.Arrow;
    }

    private void Setting_Changed(object sender, RoutedEventArgs e) => MarkDirty();

    private static string? HeaderText(object sender)
        => (sender as MenuItem)?.Header?.ToString();

    private void ApplyDropDownChoice(Wpf.Ui.Controls.DropDownButton dd, object sender)
    {
        if (HeaderText(sender) is not { } h) return;
        if (dd.Content?.ToString() == h) return;
        dd.Content = h;
        MarkDirty();
    }

    private void ThrottleSensitivity_Click(object sender, RoutedEventArgs e) => ApplyDropDownChoice(ThrottleSensitivityDropDown, sender);
    private void FoggerPower_Click(object sender, RoutedEventArgs e)         => ApplyDropDownChoice(FoggerPowerDropDown, sender);

    private void SaveButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_isDirty) return;

        var s = new AppSettings
        {
            DarkMode            = DarkModeToggle.IsChecked == true,
            ThrottleSensitivity = ThrottleSensitivityDropDown.Content?.ToString() ?? "5%",
            FoggerPower         = FoggerPowerDropDown.Content?.ToString() ?? "50%",
        };

        if (s.DarkMode != ThemeManager.IsDark)
            ThemeManager.Apply(s.DarkMode);

        SettingsService.Save(s);
        SetSavedState();
    }

    private void SaveButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDirty) SaveButton.Background = SaveHoverBrush;
    }

    private void SaveButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SaveButton.Background = _isDirty ? SaveActiveBrush : SaveDisabledBrush;
    }

    private void ApplySettingsIconTheme(bool isDark)
    {
        var p = isDark ? "pack://application:,,,/res/settings/dark/" : "pack://application:,,,/res/settings/";
        Uri U(string name) => new($"{p}{name}");
        SettingsNavGeneral.Source = U("settings.svg");
        SettingsNavDevice.Source  = U("fan.svg");
        SettingsNavData.Source    = U("chart-line.svg");
        SettingsNavAbout.Source   = U("info.svg");

        var wp = isDark ? "pack://application:,,,/res/window/dark/" : "pack://application:,,,/res/window/";
        DeviceDropletIcon.Source = new Uri($"{wp}droplet.svg");
    }

    private void SettingsNavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (PageGeneral == null) return;
        PageGeneral.Visibility  = sender == NavGeneral  ? Visibility.Visible : Visibility.Collapsed;
        PageDevice.Visibility   = sender == NavDevice   ? Visibility.Visible : Visibility.Collapsed;
        PageData.Visibility     = sender == NavData     ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility    = sender == NavAbout    ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DarkModeToggle_Changed(object sender, RoutedEventArgs e) => MarkDirty();

    private void BottomBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void OpenWebsiteButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://www.aerovis.org/",
            UseShellExecute = true
        });
    }

    private void CloseButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        Close();
    }
}
