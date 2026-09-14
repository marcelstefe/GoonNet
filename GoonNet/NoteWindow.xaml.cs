using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace GoonNet;

/// <summary>
/// Asks for the text of a Note command.
/// </summary>
public partial class NoteWindow : FluentWindow
{
    public NoteWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => NoteBox.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) TryInsert();
            else if (e.Key == Key.Escape) DialogResult = false;
        };
    }

    /// <summary>The note's text, set when inserted.</summary>
    public string Text { get; private set; } = "";

    private void TryInsert()
    {
        var text = NoteBox.Text.Trim();
        if (text.Length == 0)
        {
            ErrorText.Text = "Enter the note's text.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Text = text;
        DialogResult = true;
    }

    private void InsertButton_Click(object sender, MouseButtonEventArgs e) => TryInsert();

    private void CancelButton_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;
}
