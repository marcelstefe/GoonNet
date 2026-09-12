using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace GoonNet;

/// <summary>
/// Asks for a Fixed Time Marker's time within an hour and its type (Hard / Soft / Not Before).
/// </summary>
public partial class FixedTimeMarkerWindow : FluentWindow
{
    // Same order as the ModeBox items and FixedTimeMode.
    private static readonly string[] Descriptions =
    {
        "At this time the playing item stops immediately and playback continues after the marker.",
        "At this time the playing item is allowed to finish, then playback continues after the marker.",
        "The items after the marker won't start before this time.",
    };

    private readonly DateTime _hour;

    public FixedTimeMarkerWindow(DateTime hour)
    {
        InitializeComponent();
        _hour = hour;

        var hourText = hour.ToString(SettingsService.Current.TimeFormat == "12-Hour" ? "h tt" : "HH:00");
        HourLabel.Text = $"Time within the {hourText} hour (mm:ss)";
        TimeBox.Text = "00:00";

        ModeBox.SelectionChanged += (_, _) => ModeDescription.Text = Descriptions[Math.Max(0, ModeBox.SelectedIndex)];
        ModeBox.SelectedIndex = 0;

        Loaded += (_, _) =>
        {
            TimeBox.Focus();
            TimeBox.SelectAll();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) TryInsert();
            else if (e.Key == Key.Escape) DialogResult = false;
        };
    }

    /// <summary>The chosen time (the hour plus the entered mm:ss).</summary>
    public DateTime FixedTime { get; private set; }

    public FixedTimeMode Mode { get; private set; }

    private void TryInsert()
    {
        if (!TimeSpan.TryParseExact(TimeBox.Text.Trim(), new[] { @"m\:ss", @"mm\:ss" },
                CultureInfo.InvariantCulture, out var offset))
        {
            ShowError("Enter the time within the hour as mm:ss, for example 30:00.");
            return;
        }

        var at = _hour + offset;
        if (at <= DateTime.Now)
        {
            ShowError("That time has already passed.");
            return;
        }

        FixedTime = at;
        Mode = (FixedTimeMode)ModeBox.SelectedIndex;
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
