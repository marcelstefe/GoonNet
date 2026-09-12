using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace GoonNet;

/// <summary>
/// One of the top-panel "player" lines. Wraps a playlist track and exposes the live
/// progress and count-down for the part being played: intro, main part or outro.
/// </summary>
public class PlayerSlot : INotifyPropertyChanged
{
    private LibraryTrack? _track;
    public LibraryTrack? Track
    {
        get => _track;
        set
        {
            _track = value;
            _elapsed = 0;
            _isPlaying = false;
            RaiseAll();
        }
    }

    private bool _isPlaying;
    /// <summary>True while this slot is on air; the line's text turns white.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            OnChanged();
            OnChanged(nameof(Countdown));
        }
    }

    private double _elapsed;
    /// <summary>Seconds played of the current track.</summary>
    public double Elapsed
    {
        get => _elapsed;
        set
        {
            _elapsed = value;
            OnChanged(nameof(Elapsed));
            OnChanged(nameof(PartProgress));
            OnChanged(nameof(IsInOutro));
            OnChanged(nameof(Countdown));
        }
    }

    public bool HasTrack => _track is not null;
    public string? EventName => _track?.Event;
    public string? Title => _track?.Title;
    public string? Artist => _track?.Artist;

    public double LengthSeconds => ParseSeconds(_track?.Length);

    // Timeline parts: intro [0, IntroEnd], main [IntroEnd, OutroStart], outro [OutroStart, End].
    // A missing or out-of-range marker collapses its part to zero.
    private double End => LengthSeconds > 0 ? LengthSeconds : 0;

    private double IntroEnd
    {
        get
        {
            var intro = ParseSeconds(_track?.Intro);
            return intro > 0 && intro < End ? intro : 0;
        }
    }

    private double OutroStart
    {
        get
        {
            var outro = ParseSeconds(_track?.Outro);
            return outro > IntroEnd && outro < End ? outro : End;
        }
    }

    /// <summary>True once playback has reached the outro; the line's bar turns red.</summary>
    public bool IsInOutro => OutroStart < End && _elapsed >= OutroStart;

    // Bounds of the part currently playing.
    private (double Start, double End) CurrentPart =>
        _elapsed < IntroEnd ? (0, IntroEnd)
        : IsInOutro ? (OutroStart, End)
        : (IntroEnd, OutroStart);

    /// <summary>0..1 through the current part; the bar restarts at each part.</summary>
    public double PartProgress
    {
        get
        {
            var (start, end) = CurrentPart;
            return end > start ? Math.Clamp((_elapsed - start) / (end - start), 0, 1) : 0;
        }
    }

    /// <summary>On air: time left in the current part. Otherwise: the item's length.</summary>
    public string Countdown
    {
        get
        {
            if (!(End > 0)) return "";
            var target = _isPlaying ? CurrentPart.End : End;
            var remaining = Math.Ceiling(Math.Max(0, target - _elapsed));
            var left = TimeSpan.FromSeconds(remaining);
            return left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"mm\:ss");
        }
    }

    private static readonly string[] TimeFormats = { @"mm\:ss", @"m\:ss", @"h\:mm\:ss" };

    internal static double ParseSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NaN;
        return TimeSpan.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, out var ts)
            ? ts.TotalSeconds
            : double.NaN;
    }

    // An empty property name tells WPF every property changed.
    private void RaiseAll() => OnChanged(string.Empty);

    /// <summary>Re-reads the track's details (e.g. a Wait-until that just got its length).</summary>
    public void Refresh() => RaiseAll();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
