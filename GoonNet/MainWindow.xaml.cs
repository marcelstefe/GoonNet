using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
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
    private readonly DispatcherTimer _playerTimer;
    private readonly Stopwatch _playerClock = Stopwatch.StartNew();
    private TimeSpan _lastPlayerTick;
    private readonly PlayerSlot[] _players = { new(), new(), new(), new() };
    private SettingsWindow? _settingsWindow;

    public System.Collections.ObjectModel.ObservableCollection<LibraryTrack> LibraryTracks { get; } = new();

    public System.Collections.ObjectModel.ObservableCollection<LibraryTrack> PlaylistTracks { get; } = new();

    private ICollectionView? _libraryView;

    public MainWindow()
    {
        InitializeComponent();

        _libraryView = CollectionViewSource.GetDefaultView(LibraryTracks);
        _libraryView.Filter = LibraryFilter;
        LibraryGrid.ItemsSource = _libraryView;

        // Built-in commands, listed under the "Command" type.
        LibraryTracks.Add(new LibraryTrack { Event = "Command", Title = "Fixed Time Marker", Template = CommandKind.FixedTimeMarker });
        LibraryTracks.Add(new LibraryTrack { Event = "Command", Title = "Wait", Template = CommandKind.Wait });
        LibraryTracks.Add(new LibraryTrack { Event = "Command", Title = "Note", Template = CommandKind.Note });

        ScheduleGrid.ItemsSource = PlaylistTracks;

        // Keep the insert hint lined up with the columns as they're resized or reordered.
        var columnWidth = DependencyPropertyDescriptor.FromProperty(DataGridColumn.ActualWidthProperty, typeof(DataGridColumn));
        foreach (var column in ScheduleGrid.Columns)
            columnWidth.AddValueChanged(column, (_, _) => UpdateInsertHintLayout());
        ScheduleGrid.ColumnReordered += (_, _) => UpdateInsertHintLayout();

        Player0Host.Content = _players[0];
        Player1Host.Content = _players[1];
        Player2Host.Content = _players[2];
        Player3Host.Content = _players[3];
        PlaylistTracks.CollectionChanged += Playlist_CollectionChanged;
        SyncPlayers();

        BuildVuMeter(_vuLeftLeds, VuLeftLeds);
        BuildVuMeter(_vuRightLeds, VuRightLeds);

        _engine.DeckEnded += deck => Dispatcher.InvokeAsync(() => OnDeckEnded(deck));
        Closed += (_, _) => _engine.Dispose();

        // ~60 fps so the progress fill moves smoothly; time advance comes from _playerClock.
        _playerTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _playerTimer.Tick += PlayerTimer_Tick;
        _playerTimer.Start();

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
        EnsureHourMarkers();
        UpdateAirTimes();
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
    }

    private bool LibraryFilter(object item)
    {
        if (item is not LibraryTrack track) return false;

        // Type filter from the dropdown ("All" shows everything).
        var selectedType = (LibraryTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrEmpty(selectedType) && selectedType != "All"
            && !string.Equals(track.Event, selectedType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Free-text search across the visible text fields.
        var query = LibrarySearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            bool matches =
                Contains(track.Title, query) ||
                Contains(track.Artist, query) ||
                Contains(track.Event, query) ||
                Contains(track.Note, query);
            if (!matches) return false;
        }

        return true;
    }

    private static bool Contains(string? value, string query)
        => value?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;

    private void LibraryTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _libraryView?.Refresh();

    private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => _libraryView?.Refresh();

    // ---------- Drag/drop: library -> playlist (copy), playlist -> playlist (reorder) ----------
    private const string PlaylistReorderFormat = "GoonNet.PlaylistReorder";

    private Point _dragStart;
    private LibraryTrack? _dragCandidate;
    private Point _scheduleDragStart;
    private LibraryTrack? _scheduleDragCandidate;

    private void LibraryGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        var origin = (DependencyObject)e.OriginalSource;
        var row = FindAncestor<DataGridRow>(origin);
        // Don't start a drag from the row's hover buttons.
        _dragCandidate = FindAncestor<System.Windows.Controls.Button>(origin) is null
            ? row?.Item as LibraryTrack
            : null;
        // Clicking blank space clears the selection.
        if (row is null) LibraryGrid.UnselectAll();
    }

    private void LibraryGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null) return;

        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var data = new DataObject(typeof(LibraryTrack), _dragCandidate);
        _dragCandidate = null;
        DragDrop.DoDragDrop(LibraryGrid, data, DragDropEffects.Copy);
    }

    private void ScheduleGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _scheduleDragStart = e.GetPosition(null);
        var origin = (DependencyObject)e.OriginalSource;
        var row = FindAncestor<DataGridRow>(origin);

        // Fixed rows (hour markers) can't be clicked: no selecting, editing or dragging.
        if (row?.Item is LibraryTrack { IsEditable: false })
        {
            _scheduleDragCandidate = null;
            e.Handled = true;
            return;
        }

        _scheduleDragCandidate = FindAncestor<System.Windows.Controls.Button>(origin) is null
            ? row?.Item as LibraryTrack
            : null;
        if (row is null) ScheduleGrid.UnselectAll();
    }

    // Fixed rows are never edited...
    private void ScheduleGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is LibraryTrack { IsEditable: false }) e.Cancel = true;
    }

    // ...nor selected (e.g. by keyboard or select-all).
    private void ScheduleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var fixedItem in e.AddedItems.OfType<LibraryTrack>().Where(t => !t.IsEditable).ToList())
            ScheduleGrid.SelectedItems.Remove(fixedItem);
    }

    // Built-in library commands can't be renamed.
    private void LibraryGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is LibraryTrack { IsEditable: false }) e.Cancel = true;
    }

    private void ScheduleGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _scheduleDragCandidate is null) return;

        var diff = _scheduleDragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var data = new DataObject(PlaylistReorderFormat, _scheduleDragCandidate);
        _scheduleDragCandidate = null;
        DragDrop.DoDragDrop(ScheduleGrid, data, DragDropEffects.Move);
    }

    private void ScheduleGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(PlaylistReorderFormat))
            e.Effects = DragDropEffects.Move;
        else if (e.Data.GetDataPresent(typeof(LibraryTrack)))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void ScheduleGrid_Drop(object sender, DragEventArgs e)
    {
        // Reorder within the playlist.
        if (e.Data.GetDataPresent(PlaylistReorderFormat))
        {
            if (e.Data.GetData(PlaylistReorderFormat) is not LibraryTrack moved) return;
            var from = PlaylistTracks.IndexOf(moved);
            if (from < 0) return;

            var to = GetPlaylistDropIndex(e);
            if (to < 0) to = PlaylistTracks.Count;   // dropped past the last row
            if (to > from) to--;                      // removing the source shifts indices
            to = Math.Clamp(to, 0, PlaylistTracks.Count - 1);
            if (to != from) PlaylistTracks.Move(from, to);
            return;
        }

        // Copy in from the library.
        if (e.Data.GetDataPresent(typeof(LibraryTrack)) &&
            e.Data.GetData(typeof(LibraryTrack)) is LibraryTrack source)
        {
            var index = GetPlaylistDropIndex(e);
            if (index < 0 || index > PlaylistTracks.Count) index = PlaylistTracks.Count;

            // A built-in command: ask for its settings once the drop has finished.
            if (source.IsCommandTemplate)
            {
                var before = index < PlaylistTracks.Count ? PlaylistTracks[index] : null;
                var hour = HourAt(index);
                var kind = source.Template;
                Dispatcher.InvokeAsync(() =>
                {
                    switch (kind)
                    {
                        case CommandKind.Wait: InsertWait(before, hour); break;
                        case CommandKind.Note: InsertNote(before); break;
                        default: InsertFixedTimeMarker(before, hour); break;
                    }
                });
                return;
            }

            var copy = CloneTrack(source);
            if (index >= PlaylistTracks.Count) PlaylistTracks.Add(copy);
            else PlaylistTracks.Insert(index, copy);
        }
    }

    // Opens the mix editor for this item and the one that plays after it.
    private void PlaylistEditButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LibraryTrack track) return;

        if (NextAfter(track) is not { } next)
        {
            System.Windows.MessageBox.Show(this, "There is no next item in the playlist to mix into.", "Mix Editor",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        new MixEditorWindow(track, next) { Owner = this }.ShowDialog();
    }

    // The item that plays after this one: the next in the playlist, skipping the item on air.
    private LibraryTrack? NextAfter(LibraryTrack track)
    {
        var onAir = _onAir?.Track;
        int start = ReferenceEquals(track, onAir) ? 0 : PlaylistTracks.IndexOf(track) + 1;
        for (int i = start; i < PlaylistTracks.Count; i++)
        {
            var candidate = PlaylistTracks[i];
            if (!ReferenceEquals(candidate, onAir) && !ReferenceEquals(candidate, track)
                && !IsTail(candidate) && candidate.HasAudio)
                return candidate;
        }
        return null;
    }

    private void PlaylistRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LibraryTrack track)
            PlaylistTracks.Remove(track);
    }

    // Insert before the row under the cursor (after it if past its vertical midpoint).
    private int GetPlaylistDropIndex(DragEventArgs e)
    {
        if (FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource) is not { } row)
            return -1;
        var index = ScheduleGrid.ItemContainerGenerator.IndexFromContainer(row);
        if (index < 0) return -1;
        var pos = e.GetPosition(row);
        return pos.Y > row.ActualHeight / 2 ? index + 1 : index;
    }

    private static LibraryTrack CloneTrack(LibraryTrack t) => new()
    {
        FilePath = t.FilePath,
        Artist = t.Artist,
        Title = t.Title,
        Length = t.Length,
        Event = t.Event,
        Intro = t.Intro,
        Outro = t.Outro,
        Hook = t.Hook,
        Category = t.Category,
        Note = t.Note,
        NextStartSeconds = t.NextStartSeconds,
        VolumePoints = t.VolumePoints,
    };

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null and not T)
            current = VisualTreeHelper.GetParent(current);
        return current as T;
    }

    // ---------- Top-panel players (items playing, then the next playlist items) ----------
    // An item keeps its line until its audio has fully stopped: one mixed under the next item
    // (or fading after a skip) stays above it, in the order they started.
    private void SyncPlayers()
    {
        var playing = _tails.Select(d => d.Track).ToList();
        if (_onAir is { } onAir) playing.Add(onAir.Track);

        var lineup = playing.Take(_players.Length).ToList();
        foreach (var t in PlaylistTracks)
        {
            if (lineup.Count == _players.Length) break;
            if (!playing.Contains(t) && t.IsPlayable) lineup.Add(t);
        }

        for (int i = 0; i < _players.Length; i++)
        {
            var slot = _players[i];
            var track = i < lineup.Count ? lineup[i] : null;
            if (!ReferenceEquals(slot.Track, track)) slot.Track = track;
            var deck = track is null ? null : DeckFor(track);
            slot.IsPlaying = deck is not null;
            // Now rather than on the next tick, so a line moving up doesn't flash back to the start.
            if (deck is not null) slot.Elapsed = deck.Elapsed.TotalSeconds;
        }

        // Playlist rows of items playing get white text, like their player lines.
        foreach (var t in PlaylistTracks) t.IsPlaying = DeckFor(t) is not null;

        UpdateAirTimes();
        UpdateInsertHints();
    }

    private void Playlist_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_onAir is { } deck && !PlaylistTracks.Contains(deck.Track))
        {
            if (e.Action == NotifyCollectionChangedAction.Replace
                && e.OldItems?.Contains(deck.Track) == true
                && e.NewItems?[0] is LibraryTrack edited)
            {
                // Edited while on air: keep playing with the new details.
                deck.Track = edited;
            }
            else
            {
                // Removed from the playlist while on air: take it off.
                _onAir = null;
                deck.FadeOut(SkipFadeDuration);
            }
        }

        // A playing-out item deleted from the playlist by hand: cut it short.
        foreach (var tail in _tails)
            if (!PlaylistTracks.Contains(tail.Track)) tail.FadeOut(SkipFadeDuration);

        // A pending Fixed Time Marker deleted by hand: forget it, and stop waiting for it.
        if (_softPending is not null && !PlaylistTracks.Contains(_softPending)) _softPending = null;
        if (_waitingAt is not null && !PlaylistTracks.Contains(_waitingAt))
        {
            _waitingAt = null;
            // Not from inside this handler: StartNext changes the playlist.
            Dispatcher.InvokeAsync(() =>
            {
                if (_onAir is null && !_isManualMode && !_autoHalted) StartNext();
            });
        }

        SyncPlayers();
    }

    private void PlayerTimer_Tick(object? sender, EventArgs e)
    {
        var now = _playerClock.Elapsed;
        var delta = (now - _lastPlayerTick).TotalSeconds;
        _lastPlayerTick = now;

        // Every line still sounding (on air or playing out) moves on.
        foreach (var slot in _players)
            if (slot.IsPlaying && slot.Track is { } track && DeckFor(track) is { } playing)
                slot.Elapsed = playing.Elapsed.TotalSeconds;

        if (_onAir is { } deck)
        {
            // Mix point reached (auto mode): start the next item and let this one play out
            // underneath; it stays in the playlist until it ends.
            if (!_isManualMode && !_autoHalted
                && deck.Track.NextStartSeconds is double mixAt && deck.Elapsed.TotalSeconds >= mixAt)
            {
                _onAir = null;
                _tails.Add(deck);
                StartNext();
            }
        }

        CheckFixedTimeMarkers();
        UpdateVuMeter(delta);
    }

    // ---------- On-air playout ----------
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SkipFadeDuration = TimeSpan.FromMilliseconds(150);

    private readonly PlayoutEngine _engine = new();
    private PlayoutDeck? _onAir;
    // Set by Fade Out: auto mode won't start the next item until Play Next is pressed.
    private bool _autoHalted;
    // Items still playing out (mixed under the next item, or skipped and fading). They stay in
    // the playlist until their audio ends, but no longer count as on air or up next.
    private readonly List<PlayoutDeck> _tails = new();

    private bool IsTail(LibraryTrack track) => _tails.Any(d => ReferenceEquals(d.Track, track));

    private void PlayNextButton_Click(object sender, RoutedEventArgs e)
    {
        _autoHalted = false;
        foreach (var tail in _tails) tail.FadeOut(SkipFadeDuration);
        if (_onAir is { } current)
        {
            // Skip: quick fade on the current item (it leaves the playlist when the fade ends), start the next.
            _onAir = null;
            current.FadeOut(SkipFadeDuration);
            _tails.Add(current);
        }
        StartNext(force: true);
    }

    private void FadeOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_onAir is null && _tails.Count == 0) return;
        _autoHalted = true;
        _onAir?.FadeOut(FadeOutDuration);
        foreach (var tail in _tails) tail.FadeOut(FadeOutDuration);
    }

    /// <param name="force">Play Next: go straight past a Not Before marker instead of waiting at it.</param>
    private void StartNext(bool force = false)
    {
        _waitingAt = null;

        // A Soft marker's time came while the last item played: continue after the marker.
        if (_softPending is { } soft && _onAir is null)
        {
            JumpPast(soft);
            return;
        }

        try
        {
            _engine.Start();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Could not open the audio output:\n{ex.Message}", "GoonNet",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        // First item that isn't still playing out.
        while (_onAir is null && PlaylistTracks.FirstOrDefault(t => !IsTail(t)) is { } track)
        {
            if (!track.IsPlayable)
            {
                // Not Before: hold here until its time (Play Next goes straight past).
                if (!force && track.FixedMode == FixedTimeMode.NotBefore && track.FixedTime > DateTime.Now)
                {
                    _waitingAt = track;
                    break;
                }
                // Other commands (hour markers, Fixed Time Markers reached early) are passed through.
                PlaylistTracks.Remove(track);
                continue;
            }
            try
            {
                _onAir = track.IsWait ? StartWait(track) : _engine.Play(track);
            }
            catch (Exception ex)
            {
                // Unplayable (missing file, bad format): drop it so the playlist keeps moving.
                Debug.WriteLine($"Skipping '{track.Title}': {ex.Message}");
                PlaylistTracks.Remove(track);
            }
        }
        SyncPlayers();
        // A Wait-until may have been in a player line already, before it got its length.
        if (_onAir?.Track is { IsWait: true } wait)
            _players.FirstOrDefault(p => ReferenceEquals(p.Track, wait))?.Refresh();
    }

    private void OnDeckEnded(PlayoutDeck deck)
    {
        deck.Dispose();
        _tails.Remove(deck);
        bool wasOnAir = ReferenceEquals(deck, _onAir);
        if (wasOnAir) _onAir = null;
        PlaylistTracks.Remove(deck.Track);

        // Auto mode chains to the next item; manual mode (or after Fade Out) stops here.
        if (wasOnAir && !_isManualMode && !_autoHalted)
            StartNext();
        else
            SyncPlayers();
    }

    // ---------- Fixed Time Markers ----------
    // Hard: at its time, cut whatever plays and continue after the marker.
    // Soft: at its time, let the current item finish, then continue after the marker.
    // Not Before: playback waits at the marker until its time (see StartNext).
    // They act in auto mode while something is playing (or waiting); reached early by playback,
    // Hard and Soft markers are simply passed. Items before a marker that it jumps over are skipped.
    private LibraryTrack? _softPending;   // Soft marker whose time came; applied when the current item ends
    private LibraryTrack? _waitingAt;     // Not Before marker playback is waiting at

    private void CheckFixedTimeMarkers()
    {
        var now = DateTime.Now;
        bool auto = !_isManualMode && !_autoHalted;

        // Waiting at a Not Before marker: carry on once its time comes.
        if (_waitingAt is { FixedTime: { } waitUntil } wait && now >= waitUntil)
        {
            _waitingAt = null;
            PlaylistTracks.Remove(wait);
            if (auto) StartNext();
        }

        // First Hard (or, when none is pending, Soft) marker whose time has come.
        var due = PlaylistTracks.FirstOrDefault(t => t.FixedTime <= now
            && (t.FixedMode == FixedTimeMode.Hard || (t.FixedMode == FixedTimeMode.Soft && _softPending is null)));
        if (due is null) return;

        if (!auto || (_onAir is null && _waitingAt is null))
            PlaylistTracks.Remove(due);   // nothing was running at its time: it has expired
        else if (due.FixedMode == FixedTimeMode.Hard || _onAir is null)
            JumpPast(due);                // Hard (or nothing playing): continue after it right now
        else
            _softPending = due;           // Soft: continue after it once the current item ends
    }

    // Continue after a Fixed Time Marker: cut the on-air item if it's scheduled before the marker,
    // drop everything before the marker (skipped) and the marker itself, then start what follows.
    private void JumpPast(LibraryTrack marker)
    {
        _softPending = null;
        _waitingAt = null;

        var markerIndex = PlaylistTracks.IndexOf(marker);
        if (_onAir is { } deck && PlaylistTracks.IndexOf(deck.Track) < markerIndex)
        {
            _onAir = null;
            deck.FadeOut(SkipFadeDuration);
            _tails.Add(deck);
        }

        for (int i = markerIndex; i >= 0; i--)
        {
            var track = PlaylistTracks[i];
            if (!IsTail(track) && !ReferenceEquals(track, _onAir?.Track)) PlaylistTracks.RemoveAt(i);
        }

        if (_onAir is null) StartNext();
    }

    // Dropped from the library: ask for the time and type, then insert before `before` (or at the end).
    private void InsertFixedTimeMarker(LibraryTrack? before, DateTime hour)
    {
        var win = new FixedTimeMarkerWindow(hour) { Owner = this };
        if (win.ShowDialog() != true) return;

        // Look the row up again: playback may have changed the playlist while the dialog was open.
        var index = before is null ? -1 : PlaylistTracks.IndexOf(before);
        PlaylistTracks.Insert(index < 0 ? PlaylistTracks.Count : index, CreateFixedTimeMarker(win.FixedTime, win.Mode));
    }

    // ---------- Wait command ----------
    // Plays silence as a normal player item: for a set length, or until a clock time.

    // Dropped from the library: ask how it waits, then insert before `before` (or at the end).
    private void InsertWait(LibraryTrack? before, DateTime hour)
    {
        var win = new WaitWindow(hour) { Owner = this };
        if (win.ShowDialog() != true) return;

        var index = before is null ? -1 : PlaylistTracks.IndexOf(before);
        PlaylistTracks.Insert(index < 0 ? PlaylistTracks.Count : index, CreateWait(win.Length, win.Until));
    }

    private static LibraryTrack CreateWait(TimeSpan? length, DateTime? until)
    {
        if (length is { } l)
            return new LibraryTrack { Event = "Command", Title = $"Wait: {FormatLength(l)}", Length = FormatLength(l), WaitLength = l };

        var time = until!.Value.ToString(SettingsService.Current.TimeFormat == "12-Hour" ? "h:mm:ss tt" : "HH:mm:ss");
        return new LibraryTrack { Event = "Command", Title = $"Wait until {time}", WaitUntil = until };
    }

    // A Wait-until runs from now until its time, and only now gets a length to show.
    private PlayoutDeck StartWait(LibraryTrack track)
    {
        var now = DateTime.Now;
        var length = track.WaitLength
                     ?? (track.WaitUntil is { } until && until > now ? until - now : TimeSpan.Zero);
        track.Length = FormatLength(length);
        return _engine.Play(track, silence: length);
    }

    // Length column format: mm:ss, or h:mm:ss from an hour up.
    private static string FormatLength(TimeSpan length)
    {
        var rounded = TimeSpan.FromSeconds(Math.Round(length.TotalSeconds));
        return rounded.TotalHours >= 1 ? rounded.ToString(@"h\:mm\:ss") : rounded.ToString(@"mm\:ss");
    }

    // ---------- Note command ----------
    // Just a note in the playlist: it has no length, doesn't play, and is passed like other commands.

    // Dropped from the library: ask for the text, then insert before `before` (or at the end).
    private void InsertNote(LibraryTrack? before)
    {
        var win = new NoteWindow { Owner = this };
        if (win.ShowDialog() != true) return;

        var index = before is null ? -1 : PlaylistTracks.IndexOf(before);
        PlaylistTracks.Insert(index < 0 ? PlaylistTracks.Count : index,
            new LibraryTrack { Event = "Command", Title = $"Note: {win.Text}" });
    }

    // The hour an insertion point belongs to: the nearest hour marker above it, else the current hour.
    private DateTime HourAt(int index)
    {
        for (int i = Math.Min(index, PlaylistTracks.Count) - 1; i >= 0; i--)
            if (PlaylistTracks[i].HourStart is { } hour) return hour;
        var now = DateTime.Now;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind);
    }

    private static LibraryTrack CreateFixedTimeMarker(DateTime at, FixedTimeMode mode)
    {
        var time = at.ToString(SettingsService.Current.TimeFormat == "12-Hour" ? "h:mm:ss tt" : "HH:mm:ss");
        var label = mode == FixedTimeMode.NotBefore ? "Not Before" : mode.ToString();
        return new LibraryTrack
        {
            Event = "Command",
            Title = $"Fixed Time Marker: {time} ({label})",
            FixedTime = at,
            FixedMode = mode,
        };
    }

    // ---------- Hour markers ----------
    // The playlist always holds an "Hour Playlist" command for every hour from the current one
    // until a day ahead; new hours are appended at the end as time moves on. An hour that is over
    // and still has nothing scheduled removes itself.
    private void EnsureHourMarkers()
    {
        var now = DateTime.Now;

        // Backwards, so a run of empty past hours all go in one pass.
        for (int i = PlaylistTracks.Count - 1; i >= 0; i--)
        {
            if (PlaylistTracks[i].HourStart is not { } hour || now < hour.AddHours(1)) continue;
            var next = i + 1 < PlaylistTracks.Count ? PlaylistTracks[i + 1] : null;
            if (next is null || next.IsHourMarker) PlaylistTracks.RemoveAt(i);
        }

        var thisHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind);
        var latest = PlaylistTracks.Max(t => t.HourStart);
        for (var hour = latest?.AddHours(1) ?? thisHour; hour <= thisHour.AddDays(1); hour = hour.AddHours(1))
            PlaylistTracks.Add(CreateHourMarker(hour));
    }

    private static LibraryTrack CreateHourMarker(DateTime hour)
    {
        var s = SettingsService.Current;
        var time = hour.ToString(s.TimeFormat == "12-Hour" ? "h:mm tt" : "HH:mm");
        var date = hour.ToString(s.DateFormat == "D/M/Y" ? "d/M/yyyy" : "dd MMM yyyy");
        return new LibraryTrack
        {
            Event = "Command",
            Title = $"Hour Playlist: {time}, {date}",
            HourStart = hour,
        };
    }

    // An hour marker shows "Insert Tracks Here..." while nothing is scheduled in its hour,
    // i.e. it's followed by another marker or is the last row.
    private void UpdateInsertHints()
    {
        for (int i = 0; i < PlaylistTracks.Count; i++)
        {
            var track = PlaylistTracks[i];
            if (!track.IsHourMarker) continue;
            var next = i + 1 < PlaylistTracks.Count ? PlaylistTracks[i + 1] : null;
            track.ShowInsertHint = next is null || next.IsHourMarker;
        }
    }

    // Row details span the whole row, so the hint is placed from the live column positions:
    // at the start of the Title column, past the cell padding, lined up with the titles.
    private void UpdateInsertHintLayout()
    {
        double titleLeft = 0;
        foreach (var column in ScheduleGrid.Columns.OrderBy(c => c.DisplayIndex))
        {
            if (column == ScheduleTitleColumn) break;
            if (column.Visibility == Visibility.Visible) titleLeft += column.ActualWidth;
        }

        const double cellPadding = 8;
        ScheduleGrid.Resources["InsertHintMargin"] = new Thickness(titleLeft + cellPadding, 0, 0, 0);
    }

    // ---------- Air times (playlist "Air Time" column) ----------
    // Items playing show when they started. The rest follow on in playlist order, each one
    // starting at the previous item's mix point (or end), on the real clock.
    private void UpdateAirTimes()
    {
        var format = SettingsService.Current.TimeFormat == "12-Hour" ? "hh:mm:ss tt" : "HH:mm:ss";
        var now = DateTime.Now;

        // Nothing on air, or it has overrun its hand-over: the next item would start now.
        var next = now;
        var onAirEnd = now;
        if (_onAir is { } onAir)
        {
            onAirEnd = onAir.StartedAt + TimeSpan.FromSeconds(HandOverSeconds(onAir.Track, onAir.Length.TotalSeconds));
            if (onAirEnd > now) next = onAirEnd;
        }

        // Items planned since the last Fixed Time Marker; the next one may cut or skip them.
        var planned = new List<PlannedItem>();

        foreach (var track in PlaylistTracks)
        {
            if (DeckFor(track) is { } deck)
            {
                track.AirTime = deck.StartedAt.ToString(format);
                if (ReferenceEquals(deck, _onAir)) planned.Add(new PlannedItem(track, deck.StartedAt, onAirEnd));
                continue;
            }

            // Fixed Time Markers show no air time; they only set where the schedule continues.
            if (track.FixedTime is { } fixedAt)
            {
                track.AirTime = null;
                next = ResumeAfterFixedTime(track.FixedMode, fixedAt, next, planned);
                planned.Clear();
                continue;
            }

            var at = next;
            // A Wait-until runs from whenever it starts until its time.
            next = track.WaitUntil is { } until && track.WaitLength is null
                ? (until > at ? until : at)
                : at + TimeSpan.FromSeconds(HandOverSeconds(track, PlayerSlot.ParseSeconds(track.Length)));

            // Hour markers show how early/late their hour starts against its scheduled time.
            if (track.HourStart is { } scheduled)
            {
                track.AirTime = FormatOffset(at - scheduled);
                continue;
            }
            track.AirTime = at.ToString(format);
            planned.Add(new PlannedItem(track, at, next));
        }
    }

    private readonly record struct PlannedItem(LibraryTrack Track, DateTime Start, DateTime End);

    // Where the schedule continues after a Fixed Time Marker that playback reaches at `arrival`.
    // Planned items it would skip get "Skip" in the Air Time column.
    private static DateTime ResumeAfterFixedTime(FixedTimeMode mode, DateTime fixedAt, DateTime arrival,
        List<PlannedItem> planned)
    {
        switch (mode)
        {
            case FixedTimeMode.NotBefore:
                return arrival < fixedAt ? fixedAt : arrival;

            case FixedTimeMode.Hard when arrival > fixedAt:
                // The item playing at the fixed time is cut; the ones after it are skipped.
                MarkSkipped(planned, fixedAt);
                return fixedAt;

            case FixedTimeMode.Soft when arrival > fixedAt:
                // The item playing at the fixed time finishes; the ones after it are skipped.
                var resume = planned.FirstOrDefault(p => p.End > fixedAt).End;
                if (resume == default) resume = arrival;
                MarkSkipped(planned, resume);
                return resume;

            default:
                return arrival;   // reached early: passed straight through
        }
    }

    private static void MarkSkipped(List<PlannedItem> planned, DateTime from)
    {
        foreach (var p in planned)
            if (p.Start >= from) p.Track.AirTime = "Skip";
    }

    private PlayoutDeck? DeckFor(LibraryTrack track)
        => ReferenceEquals(_onAir?.Track, track) ? _onAir : _tails.FirstOrDefault(d => ReferenceEquals(d.Track, track));

    // Seconds from an item's start until the next item starts: its mix point, else its length.
    private static double HandOverSeconds(LibraryTrack track, double length)
    {
        if (!(length > 0)) length = 0;
        return track.NextStartSeconds is double mix && (length <= 0 || mix < length) ? mix : length;
    }

    // "+2:15" (late), "-0:40" (early) or "0:00"; rounded to whole seconds so late + early add up.
    private static string FormatOffset(TimeSpan offset)
    {
        var abs = TimeSpan.FromSeconds(Math.Round(offset.Duration().TotalSeconds));
        if (abs == TimeSpan.Zero) return "0:00";
        var text = abs.TotalHours >= 1 ? abs.ToString(@"h\:mm\:ss") : abs.ToString(@"m\:ss");
        return offset > TimeSpan.Zero ? $"+{text}" : $"-{text}";
    }

    // ---------- Master VU meter (left sidebar) ----------
    private const int VuLedCount = 30;
    private const double VuFloorDb = -45;          // bottom LED; each LED is 1.5 dB
    private const double VuFallPerSecond = 0.6;    // share of the scale the bar drops per second

    private static readonly Brush VuRed = FrozenBrush(0xef, 0x44, 0x44);
    private static readonly Brush VuYellow = FrozenBrush(0xea, 0xb3, 0x08);
    private static readonly Brush VuGreen = FrozenBrush(0x22, 0xc5, 0x5e);

    private readonly Ellipse[] _vuLeftLeds = new Ellipse[VuLedCount];
    private readonly Ellipse[] _vuRightLeds = new Ellipse[VuLedCount];
    private double _vuLeft;
    private double _vuRight;
    private int _vuLeftLit;
    private int _vuRightLit;

    private static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static void BuildVuMeter(Ellipse[] leds, StackPanel host)
    {
        for (int i = 0; i < VuLedCount; i++)
        {
            var led = new Ellipse
            {
                Width = 10,
                Height = 10,
                Margin = new Thickness(0, 0, 0, i < VuLedCount - 1 ? 3 : 0)
            };
            led.SetResourceReference(Shape.FillProperty, "AppCardBg");
            leds[i] = led;
            host.Children.Add(led);
        }
    }

    private void UpdateVuMeter(double delta)
    {
        var (left, right) = _engine.TakePeaks();
        _vuLeft = Math.Max(ToMeterScale(left), _vuLeft - VuFallPerSecond * delta);
        _vuRight = Math.Max(ToMeterScale(right), _vuRight - VuFallPerSecond * delta);
        SetVuLeds(_vuLeftLeds, _vuLeft, ref _vuLeftLit);
        SetVuLeds(_vuRightLeds, _vuRight, ref _vuRightLit);
    }

    // Peak sample (0..1) -> 0..1 position on the meter's dB scale.
    private static double ToMeterScale(float peak)
        => peak <= 0 ? 0 : Math.Clamp(1 - 20 * Math.Log10(peak) / VuFloorDb, 0, 1);

    private static void SetVuLeds(Ellipse[] leds, double level, ref int lastLit)
    {
        int lit = (int)Math.Round(level * VuLedCount);
        if (lit == lastLit) return;

        // Only touch the LEDs between the old and new level; n counts up from the bottom.
        for (int n = Math.Min(lit, lastLit) + 1; n <= Math.Max(lit, lastLit); n++)
        {
            int i = VuLedCount - n;   // index from the top
            if (n <= lit)
                leds[i].Fill = i < 6 ? VuRed : i < 9 ? VuYellow : VuGreen;
            else
                leds[i].SetResourceReference(Shape.FillProperty, "AppCardBg");
        }
        lastLit = lit;
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

    private void MassImportButton_Click(object sender, RoutedEventArgs e)
    {
        var libraryPaths = LibraryTracks.Where(t => t.HasAudio).Select(t => t.FilePath!);
        var win = new MassImportWindow(libraryPaths) { Owner = this };
        if (win.ShowDialog() != true) return;
        foreach (var track in win.Result) LibraryTracks.Add(track);
    }

    private void EditTrackButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LibraryTrack track) return;

        if (_importFileWindow is { IsVisible: true })
        {
            _importFileWindow.Activate();
            return;
        }

        var win = new ImportFileWindow { Owner = this };
        win.LoadForEdit(track);
        _importFileWindow = win;
        win.Closed += (_, _) => _importFileWindow = null;
        if (win.ShowDialog() == true && win.Result is { } updated)
        {
            var index = LibraryTracks.IndexOf(track);
            if (index >= 0) LibraryTracks[index] = updated;
        }
    }

    private void RemoveTrackButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LibraryTrack track)
            LibraryTracks.Remove(track);
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