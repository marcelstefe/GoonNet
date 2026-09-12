using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NAudio.Wave.SampleProviders;
using Wpf.Ui.Controls;

namespace GoonNet;

/// <summary>
/// Mix editor for two neighbouring playlist items: when the next item starts
/// (<see cref="LibraryTrack.NextStartSeconds"/>) and both items' volume lines.
/// Nothing is written to the tracks until Save.
/// </summary>
public partial class MixEditorWindow : FluentWindow
{
    private const double PreRollSeconds = 5;

    private readonly LibraryTrack _outgoing;
    private readonly LibraryTrack _incoming;
    private bool _loaded;

    // Player position (outgoing-item seconds): where Play starts; set by clicking the editor.
    private double _cursor;

    // Preview uses its own output, never the on-air playout engine.
    private PlayoutEngine? _preview;
    private bool _paused;
    private readonly List<PlayoutDeck> _previewDecks = new();
    private LibraryTrack? _previewOutgoing;   // copies carrying the unsaved volume lines
    private LibraryTrack? _previewIncoming;
    private readonly Stopwatch _previewClock = new();
    private double _previewFrom;
    private double _previewEnd;
    private readonly DispatcherTimer _playheadTimer;

    public MixEditorWindow(LibraryTrack outgoing, LibraryTrack incoming)
    {
        InitializeComponent();
        _outgoing = outgoing;
        _incoming = incoming;

        MixView.Changed += MixView_Changed;
        MixView.SeekRequested += MixView_SeekRequested;

        _playheadTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _playheadTimer.Tick += (_, _) => UpdatePlayhead();

        Loaded += async (_, _) => await LoadWaveformsAsync();
        Closed += (_, _) => StopPreview();
    }

    private static string Describe(LibraryTrack t)
        => string.IsNullOrWhiteSpace(t.Artist) ? t.Title ?? "" : $"{t.Title} – {t.Artist}";

    private async Task LoadWaveformsAsync()
    {
        try
        {
            var pathA = _outgoing.FilePath ?? throw new InvalidOperationException($"'{_outgoing.Title}' has no audio file.");
            var pathB = _incoming.FilePath ?? throw new InvalidOperationException($"'{_incoming.Title}' has no audio file.");
            var (peaksA, peaksB) = await Task.Run(() => (WaveformPeaks.Load(pathA), WaveformPeaks.Load(pathB)));

            MixView.SetItems(
                Describe(_outgoing), peaksA, PlayerSlot.ParseSeconds(_outgoing.Outro), _outgoing.VolumePoints,
                Describe(_incoming), peaksB, PlayerSlot.ParseSeconds(_incoming.Intro), _incoming.VolumePoints,
                _outgoing.NextStartSeconds ?? DefaultNextStart(peaksA.LengthSeconds));

            LoadingText.Visibility = Visibility.Collapsed;
            PlayPauseButton.IsEnabled = StopButton.IsEnabled = ResetButton.IsEnabled = true;
            _loaded = true;
            SetCursor(PreRollStart);
        }
        catch (Exception ex)
        {
            LoadingText.Text = $"Could not load the audio: {ex.Message}";
        }
    }

    // Without a saved mix point: the outgoing item's outro marker, otherwise its end.
    private double DefaultNextStart(double lengthA)
    {
        var outro = PlayerSlot.ParseSeconds(_outgoing.Outro);
        return outro > 0 && outro < lengthA ? outro : lengthA;
    }

    // A few seconds before the mix point.
    private double PreRollStart => Math.Max(0, MixView.NextStart - PreRollSeconds);
    private double MixEnd => Math.Max(MixView.LengthA, MixView.NextStart + MixView.LengthB);

    private void SetCursor(double seconds)
    {
        _cursor = Math.Clamp(seconds, 0, MixEnd);
        if (_preview is null) MixView.Playhead = _cursor;
    }

    private void MixView_Changed()
    {
        // A running preview follows volume edits live.
        if (_previewOutgoing is not null) _previewOutgoing.VolumePoints = MixView.PointsA.ToArray();
        if (_previewIncoming is not null) _previewIncoming.VolumePoints = MixView.PointsB.ToArray();
    }

    // One click in the editor: move the player there (and keep playing if it was playing).
    private void MixView_SeekRequested(double seconds)
    {
        if (!_loaded) return;
        if (_preview is not null && !_paused)
        {
            StartPreview(seconds);
            return;
        }
        StopPreview();
        SetCursor(seconds);
    }

    // ---------- Preview ----------
    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_preview is null) StartPreview(_cursor);
        else if (_paused) ResumePreview();
        else PausePreview();
    }

    // Stop and go back to where playback started.
    private void StopButton_Click(object sender, RoutedEventArgs e) => StopPreview();

    /// <summary>Plays both items as they'll be mixed, from <paramref name="from"/> (outgoing-item seconds).</summary>
    private void StartPreview(double from)
    {
        StopPreview();
        if (!_loaded) return;
        SetCursor(from);
        from = _cursor;

        var nextStart = MixView.NextStart;
        _previewOutgoing = new LibraryTrack { FilePath = _outgoing.FilePath, VolumePoints = MixView.PointsA.ToArray() };
        _previewIncoming = new LibraryTrack { FilePath = _incoming.FilePath, VolumePoints = MixView.PointsB.ToArray() };

        try
        {
            _preview = new PlayoutEngine();
            var format = _preview.MixFormat;

            // One sub-mix so both items stay sample-aligned; it ends once both have ended.
            var mix = new MixingSampleProvider(format);
            if (from < MixView.LengthA)
                AddPreviewDeck(mix, new PlayoutDeck(_previewOutgoing, format, startSeconds: from));
            if (from < nextStart + MixView.LengthB)
                AddPreviewDeck(mix, new PlayoutDeck(_previewIncoming, format,
                    startSeconds: Math.Max(0, from - nextStart),
                    delaySeconds: Math.Max(0, nextStart - from)));
            _preview.AddInput(mix);
        }
        catch (Exception ex)
        {
            StopPreview();
            System.Windows.MessageBox.Show(this, $"Could not preview the mix:\n{ex.Message}", "Mix Editor",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        _previewFrom = from;
        _previewEnd = MixEnd;
        _previewClock.Restart();
        _playheadTimer.Start();
        SetPlayIcon(true);
    }

    private void AddPreviewDeck(MixingSampleProvider mix, PlayoutDeck deck)
    {
        _previewDecks.Add(deck);
        mix.AddMixerInput(deck);
    }

    private void PausePreview()
    {
        _preview?.Pause();
        _previewClock.Stop();
        _playheadTimer.Stop();
        _paused = true;
        SetPlayIcon(false);
    }

    private void ResumePreview()
    {
        _preview?.Resume();
        _previewClock.Start();
        _playheadTimer.Start();
        _paused = false;
        SetPlayIcon(true);
    }

    // Stops any preview; the player line goes back to the cursor (where playback started).
    private void StopPreview()
    {
        _playheadTimer.Stop();
        _previewClock.Reset();
        _preview?.Dispose();
        _preview = null;
        _paused = false;
        foreach (var deck in _previewDecks) deck.Dispose();
        _previewDecks.Clear();
        _previewOutgoing = null;
        _previewIncoming = null;
        MixView.Playhead = _loaded ? _cursor : null;
        SetPlayIcon(false);
    }

    private void UpdatePlayhead()
    {
        var t = _previewFrom + _previewClock.Elapsed.TotalSeconds;
        if (t >= _previewEnd)
            StopPreview();
        else
            MixView.Playhead = t;
    }

    private void SetPlayIcon(bool playing)
        => PlayPauseIcon.Source = playing
            ? new Uri("pack://application:,,,/res/studio/pause.svg", UriKind.Absolute)
            : new Uri("pack://application:,,,/res/window/play.svg", UriKind.Absolute);

    // ---------- Buttons ----------
    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        StopPreview();
        MixView.Reset(DefaultNextStart(MixView.LengthA));
        SetCursor(PreRollStart);
    }

    private void SaveButton_Click(object sender, MouseButtonEventArgs e)
    {
        if (_loaded)
        {
            var at = MixView.NextStart;
            _outgoing.NextStartSeconds = MixView.LengthA - at > 0.05 ? at : null;
            _outgoing.VolumePoints = MixView.PointsA.ToArray();
            _incoming.VolumePoints = MixView.PointsB.ToArray();
        }
        DialogResult = _loaded;
    }

    private void CancelButton_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;
}
