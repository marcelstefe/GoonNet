using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NAudio.Wave;
using Wpf.Ui.Controls;

namespace GoonNet;

/// <summary>Picks many audio files (or whole folders) at once and adds them all to the library.</summary>
public partial class MassImportWindow : FluentWindow
{
    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".flac", ".ogg", ".m4a" };

    private readonly ObservableCollection<LibraryTrack> _pending = new();
    // Paths already in the library, and paths in the list, so nothing is added twice.
    private readonly HashSet<string> _libraryPaths;
    private readonly HashSet<string> _listPaths = new(StringComparer.OrdinalIgnoreCase);

    private int _scanning;
    private int _lengthsToRead;
    private int _skipped;
    private int _unreadable;
    // Bumped by Clear and on close, so background work started before then is dropped.
    private int _generation;

    public MassImportWindow(IEnumerable<string> libraryPaths)
    {
        InitializeComponent();
        _libraryPaths = new HashSet<string>(libraryPaths, StringComparer.OrdinalIgnoreCase);
        FileGrid.ItemsSource = _pending;
        _pending.CollectionChanged += (_, _) => UpdateStatus();
        Closed += (_, _) => _generation++;
        UpdateStatus();
    }

    /// <summary>The tracks to add, set when Import is pressed.</summary>
    public List<LibraryTrack> Result { get; } = new();

    // ---------- Adding ----------
    private async void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select files to import",
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.ogg;*.m4a|All Files|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true) await AddFilesAsync(dlg.FileNames);
    }

    private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select folders to import", Multiselect = true };
        if (dlg.ShowDialog(this) == true) await AddFoldersAsync(dlg.FolderNames);
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void FileList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        await AddFilesAsync(paths.Where(p => File.Exists(p) && IsAudioFile(p)).ToList());
        await AddFoldersAsync(paths.Where(Directory.Exists).ToList());
    }

    // Folders are searched in the background for audio files (by extension).
    private async Task AddFoldersAsync(IReadOnlyCollection<string> folders)
    {
        if (folders.Count == 0) return;
        var generation = _generation;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = IncludeSubfoldersBox.IsChecked == true,
            IgnoreInaccessible = true,
        };

        _scanning++;
        UpdateStatus();
        List<string> files;
        try
        {
            files = await Task.Run(() => folders
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*", options))
                .Where(IsAudioFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList());
        }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            System.Windows.MessageBox.Show(this, $"Could not read the folder:\n{ex.Message}",
                "Mass import", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        finally
        {
            if (generation == _generation) _scanning--;
        }

        if (generation != _generation) return;
        await AddFilesAsync(files);
    }

    // Artist and title come from the file name; the length is read afterwards.
    private async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var added = new List<LibraryTrack>();
        foreach (var path in paths)
        {
            if (_libraryPaths.Contains(path) || !_listPaths.Add(path))
            {
                _skipped++;
                continue;
            }
            var (artist, title) = SplitFileName(path);
            var track = new LibraryTrack { FilePath = path, Artist = artist, Title = title };
            _pending.Add(track);
            added.Add(track);
        }
        UpdateStatus();
        await ReadLengthsAsync(added);
    }

    // One file at a time in the background. Files NAudio can't open are dropped from the
    // list, as the playout engine couldn't play them either.
    private async Task ReadLengthsAsync(List<LibraryTrack> tracks)
    {
        var generation = _generation;
        _lengthsToRead += tracks.Count;
        UpdateStatus();

        foreach (var track in tracks)
        {
            var length = await Task.Run(() => ReadLength(track.FilePath!));
            if (generation != _generation) return;

            _lengthsToRead--;
            if (length is { } l) track.Length = FormatLength(l);
            else if (_pending.Remove(track))
            {
                _listPaths.Remove(track.FilePath!);
                _unreadable++;
            }
            UpdateStatus();
        }
    }

    private static TimeSpan? ReadLength(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            return reader.TotalTime;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatLength(TimeSpan t)
        => t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");

    private static bool IsAudioFile(string path)
        => AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // "Artist - Title.mp3" gives both; otherwise the whole file name is the title.
    private static (string? Artist, string Title) SplitFileName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var dash = stem.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? (stem[..dash].Trim(), stem[(dash + 3)..].Trim()) : (null, stem);
    }

    // ---------- Removing ----------
    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LibraryTrack track) Remove(track);
    }

    private void FileGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        foreach (var track in FileGrid.SelectedItems.Cast<LibraryTrack>().ToList()) Remove(track);
        e.Handled = true;
    }

    private void Remove(LibraryTrack track)
    {
        if (_pending.Remove(track)) _listPaths.Remove(track.FilePath!);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _generation++;
        _scanning = 0;
        _lengthsToRead = 0;
        _skipped = 0;
        _unreadable = 0;
        _listPaths.Clear();
        _pending.Clear();
    }

    // ---------- Status and buttons ----------
    private void UpdateStatus()
    {
        EmptyText.Visibility = _pending.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var parts = new List<string> { _pending.Count == 1 ? "1 file" : $"{_pending.Count} files" };
        if (_scanning > 0) parts.Add("searching folders...");
        if (_lengthsToRead > 0) parts.Add($"reading lengths ({_lengthsToRead} left)...");
        if (_skipped > 0) parts.Add($"{_skipped} skipped (already in the library or list)");
        if (_unreadable > 0) parts.Add($"{_unreadable} couldn't be read");
        StatusText.Text = string.Join("  ·  ", parts);

        // Import once every file's length is known.
        var ready = _pending.Count > 0 && _scanning == 0 && _lengthsToRead == 0;
        ImportButton.IsEnabled = ready;
        ImportButton.Opacity = ready ? 1 : 0.5;
    }

    private void ImportButton_Click(object sender, MouseButtonEventArgs e)
    {
        var type = (CategoryBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        foreach (var track in _pending) track.Event = type;
        Result.AddRange(_pending);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, MouseButtonEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
