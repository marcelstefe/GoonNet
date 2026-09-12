using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace GoonNet;

/// <summary>
/// Asks how a Wait command runs: for a set length, or until a clock time.
/// </summary>
public partial class WaitWindow : FluentWindow
{
    private static readonly string[] LengthFormats = { @"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss" };
    private static readonly string[] ClockFormats = { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" };

    private readonly DateTime _hour;

    /// <param name="hour">The playlist hour it's inserted in; a wait-until time is taken on that day.</param>
    public WaitWindow(DateTime hour)
    {
        InitializeComponent();
        _hour = hour;

        LengthBox.Text = "01:00";
        UntilBox.Text = hour.AddMinutes(30).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        // Typing in a box picks its option.
        LengthBox.GotFocus += (_, _) => ForLengthOption.IsChecked = true;
        UntilBox.GotFocus += (_, _) => UntilOption.IsChecked = true;

        Loaded += (_, _) =>
        {
            LengthBox.Focus();
            LengthBox.SelectAll();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) TryInsert();
            else if (e.Key == Key.Escape) DialogResult = false;
        };
    }

    /// <summary>Set when waiting for a length.</summary>
    public TimeSpan? Length { get; private set; }

    /// <summary>Set when waiting until a time.</summary>
    public DateTime? Until { get; private set; }

    private void TryInsert()
    {
        if (ForLengthOption.IsChecked == true)
        {
            if (!TimeSpan.TryParseExact(LengthBox.Text.Trim(), LengthFormats, CultureInfo.InvariantCulture, out var length)
                || length <= TimeSpan.Zero)
            {
                ShowError("Enter the length as mm:ss or h:mm:ss, for example 2:30.");
                return;
            }
            Length = length;
        }
        else
        {
            if (!TimeSpan.TryParseExact(UntilBox.Text.Trim(), ClockFormats, CultureInfo.InvariantCulture, out var clock)
                || clock >= TimeSpan.FromDays(1))
            {
                ShowError("Enter the time as HH:mm:ss, for example 23:30:00.");
                return;
            }

            // On the day of the hour it's inserted in; a time before that hour means the next day.
            var until = _hour.Date + clock;
            if (until < _hour) until = until.AddDays(1);
            if (until <= DateTime.Now)
            {
                ShowError("That time has already passed.");
                return;
            }
            Until = until;
        }
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void InsertButton_Click(object sender, MouseButtonEventArgs e) => TryInsert();

    private void CancelButton_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;
}
