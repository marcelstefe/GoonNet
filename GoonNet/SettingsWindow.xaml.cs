using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace GoonNet;

public partial class SettingsWindow : FluentWindow
{
    private static readonly SolidColorBrush SaveActiveBrush   = new(Color.FromRgb(0x00, 0x67, 0xc0));
    private static readonly SolidColorBrush SaveHoverBrush    = new(Color.FromRgb(0x00, 0x55, 0x9f));
    private static readonly SolidColorBrush SaveDisabledBrush = new(Color.FromRgb(0x88, 0x88, 0x88));

    private bool _isDirty;

    public SettingsWindow()
    {
        InitializeComponent();
        SetSavedState();
    }

    private void SetSavedState()
    {
        _isDirty = false;
        SaveButtonText.Text = "Saved";
        SaveButton.Background = SaveDisabledBrush;
        SaveButton.Cursor = System.Windows.Input.Cursors.Arrow;
    }

    private void SaveButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_isDirty) return;

        SettingsService.Save(new AppSettings());
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

    private void SettingsNavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (PageGeneral == null) return;
        PageGeneral.Visibility = sender == NavGeneral ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BottomBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        Close();
    }
}
