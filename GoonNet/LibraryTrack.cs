using System.ComponentModel;

namespace GoonNet;

/// <summary>What a Fixed Time Marker does when its time is reached (in auto mode).</summary>
public enum FixedTimeMode
{
    /// <summary>Stop the playing item immediately and continue after the marker.</summary>
    Hard,
    /// <summary>Let the playing item finish, then continue after the marker.</summary>
    Soft,
    /// <summary>Items after the marker don't start before its time.</summary>
    NotBefore,
}

/// <summary>Which built-in command a library item stands for.</summary>
public enum CommandKind
{
    None,
    FixedTimeMarker,
    Wait,
    Note,
}

public class LibraryTrack : INotifyPropertyChanged
{
    public string? FilePath { get; set; }
    public string? Artist { get; set; }
    public string? Title { get; set; }
    private string? _length;
    /// <summary>mm:ss (or h:mm:ss). Notifies, as a Wait-until only gets its length when it starts.</summary>
    public string? Length
    {
        get => _length;
        set
        {
            if (_length == value) return;
            _length = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Length)));
        }
    }
    public string? Event { get; set; }
    public string? Intro { get; set; }
    public string? Outro { get; set; }
    public string? Hook { get; set; }
    public string? Category { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// Seconds into this item at which the next playlist item starts (set in the mix editor).
    /// Null = the next item starts when this one ends.
    /// </summary>
    public double? NextStartSeconds { get; set; }

    /// <summary>
    /// Volume line over this item's own timeline; empty = full volume throughout.
    /// Always replaced as a whole, never changed in place, so the audio thread can read it safely.
    /// </summary>
    public VolumePoint[] VolumePoints { get; set; } = [];

    /// <summary>For an hour marker ("Hour Playlist" command): the hour it stands for. Null otherwise.</summary>
    public DateTime? HourStart { get; set; }

    /// <summary>Whether the item has audio to play (commands such as hour markers don't).</summary>
    public bool HasAudio => !string.IsNullOrWhiteSpace(FilePath);

    public bool IsHourMarker => HourStart is not null;

    /// <summary>
    /// For a built-in command in the library (Fixed Time Marker, Wait): which one. Dropping it
    /// into the playlist asks for its settings and creates the real command.
    /// </summary>
    public CommandKind Template { get; init; }

    public bool IsCommandTemplate => Template != CommandKind.None;

    /// <summary>
    /// False for fixed rows (hour markers in the playlist, built-in commands in the library):
    /// no editing or removing; hour markers can't be selected or dragged either.
    /// </summary>
    public bool IsEditable => !IsHourMarker && !IsCommandTemplate;

    /// <summary>For a Fixed Time Marker command: the exact time it acts at. Null otherwise.</summary>
    public DateTime? FixedTime { get; set; }

    /// <summary>For a Fixed Time Marker command: what it does at <see cref="FixedTime"/>.</summary>
    public FixedTimeMode FixedMode { get; set; }

    /// <summary>For a Wait command set by length: how long it waits.</summary>
    public TimeSpan? WaitLength { get; set; }

    /// <summary>For a Wait command set by time: the clock time it waits until.</summary>
    public DateTime? WaitUntil { get; set; }

    public bool IsWait => WaitLength is not null || WaitUntil is not null;

    /// <summary>Goes on air as a player item: audio, or a Wait (silence).</summary>
    public bool IsPlayable => HasAudio || IsWait;

    private string? _airTime;
    /// <summary>When this playlist item plays (or started), formatted for the Air Time column.</summary>
    public string? AirTime
    {
        get => _airTime;
        set
        {
            if (_airTime == value) return;
            _airTime = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AirTime)));
        }
    }

    private bool _showInsertHint;
    /// <summary>Hour markers only: nothing is scheduled in the hour yet, so show "Insert Tracks Here...".</summary>
    public bool ShowInsertHint
    {
        get => _showInsertHint;
        set
        {
            if (_showInsertHint == value) return;
            _showInsertHint = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowInsertHint)));
        }
    }

    private bool _isPlaying;
    /// <summary>Playlist items on air or still playing out; the row's text turns white.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
