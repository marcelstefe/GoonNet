using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GoonNet;

/// <summary>
/// One playlist item on air. Reads the file, converts it to the mixer format, applies the
/// item's volume line and fade-outs. Returns end-of-stream once the file runs out or a fade
/// completes, which makes the mixer drop it and <see cref="PlayoutEngine.DeckEnded"/> fire.
/// </summary>
public sealed class PlayoutDeck : ISampleProvider, IDisposable
{
    private readonly AudioFileReader? _reader;   // null for a silent (Wait) deck
    private readonly ISampleProvider _source;
    private readonly Stopwatch _clock = new();
    private readonly TimeSpan _length;
    private readonly int _channels;
    private readonly double _secondsPerFrame;

    // Audio thread only.
    private long _delayFrames;      // silence still to output before the item starts
    private double _position;       // item time of the next frame, for the volume line

    // Fade: _level runs 1 -> 0 (audio thread only); the applied gain is _level squared.
    private float _level = 1f;
    private volatile float _fadeStep;   // level drop per frame, 0 = not fading
    private bool _ended;

    /// <param name="startSeconds">Where in the item to start playing.</param>
    /// <param name="delaySeconds">Silence before the item starts (lines up a mix preview).</param>
    public PlayoutDeck(LibraryTrack track, WaveFormat mixFormat, double startSeconds = 0, double delaySeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(track.FilePath))
            throw new InvalidOperationException("Item has no audio file.");

        Track = track;
        WaveFormat = mixFormat;
        _channels = mixFormat.Channels;
        _secondsPerFrame = 1.0 / mixFormat.SampleRate;
        _delayFrames = (long)Math.Round(Math.Max(0, delaySeconds) * mixFormat.SampleRate);

        _reader = new AudioFileReader(track.FilePath);
        try
        {
            _length = _reader.TotalTime;
            _position = Math.Clamp(startSeconds, 0, _length.TotalSeconds);
            if (_position > 0) _reader.CurrentTime = TimeSpan.FromSeconds(_position);

            ISampleProvider sp = _reader;
            if (sp.WaveFormat.Channels == 1)
                sp = new MonoToStereoSampleProvider(sp);
            else if (sp.WaveFormat.Channels != mixFormat.Channels)
                throw new NotSupportedException($"{sp.WaveFormat.Channels}-channel audio is not supported.");
            if (sp.WaveFormat.SampleRate != mixFormat.SampleRate)
                sp = new WdlResamplingSampleProvider(sp, mixFormat.SampleRate);
            _source = sp;
        }
        catch
        {
            _reader.Dispose();
            throw;
        }
    }

    /// <summary>A silent deck lasting <paramref name="silence"/> (the Wait command).</summary>
    public PlayoutDeck(LibraryTrack track, WaveFormat mixFormat, TimeSpan silence)
    {
        Track = track;
        WaveFormat = mixFormat;
        _channels = mixFormat.Channels;
        _secondsPerFrame = 1.0 / mixFormat.SampleRate;
        _length = silence;
        _source = new SilenceProvider(mixFormat, silence);
    }

    /// <summary>The playlist item being played (its volume line is read live).</summary>
    public LibraryTrack Track { get; set; }

    public WaveFormat WaveFormat { get; }

    /// <summary>Time on air, from a steady clock so the progress display moves smoothly.</summary>
    public TimeSpan Elapsed => _clock.Elapsed < _length ? _clock.Elapsed : _length;

    /// <summary>Wall-clock time the deck went on air.</summary>
    public DateTime StartedAt { get; private set; }

    /// <summary>Length of the audio file.</summary>
    public TimeSpan Length => _length;

    internal void Start()
    {
        StartedAt = DateTime.Now;
        _clock.Start();
    }

    /// <summary>
    /// Fades from the current level to silence over <paramref name="duration"/>, then ends.
    /// A fade already in progress is only ever shortened.
    /// </summary>
    public void FadeOut(TimeSpan duration)
    {
        var frames = Math.Max(1.0, duration.TotalSeconds * WaveFormat.SampleRate);
        var step = (float)Math.Max(_level / frames, 1e-9);
        if (step > _fadeStep) _fadeStep = step;
    }

    public int Read(Span<float> buffer)
    {
        if (_ended) return 0;
        var step = _fadeStep;

        // Leading silence (a mix preview's incoming item waiting for its start).
        int total = 0;
        if (_delayFrames > 0)
        {
            if (step > 0)
            {
                _ended = true;   // faded before it even started
                return 0;
            }
            int silent = (int)Math.Min(_delayFrames * _channels, buffer.Length);
            buffer[..silent].Clear();
            _delayFrames -= silent / _channels;
            total = silent;
        }
        int audioStart = total;

        // Fill completely: the mixer treats a short read as end of stream.
        while (total < buffer.Length)
        {
            int read = _source.Read(buffer[total..]);
            if (read == 0) break;
            total += read;
        }
        if (total < buffer.Length) _ended = true;

        var envelope = Track.VolumePoints;
        if (envelope.Length == 0 && step <= 0)
        {
            _position += (total - audioStart) / _channels * _secondsPerFrame;
            return total;
        }

        for (int i = audioStart; i < total; i += _channels)
        {
            float gain = envelope.Length == 0 ? 1f : VolumeEnvelope.GainAt(envelope, _position);
            _position += _secondsPerFrame;
            if (step > 0)
            {
                _level -= step;
                if (_level <= 0)
                {
                    _level = 0;
                    total = i;
                    _ended = true;
                    break;
                }
                gain *= _level * _level;
            }
            for (int c = 0; c < _channels; c++) buffer[i + c] *= gain;
        }
        return total;
    }

    public void Dispose() => _reader?.Dispose();

    // Plays nothing for a set time, then ends.
    private sealed class SilenceProvider(WaveFormat format, TimeSpan length) : ISampleProvider
    {
        private long _remaining = Math.Max(0, (long)(length.TotalSeconds * format.SampleRate)) * format.Channels;

        public WaveFormat WaveFormat => format;

        public int Read(Span<float> buffer)
        {
            int count = (int)Math.Min(buffer.Length, _remaining);
            buffer[..count].Clear();
            _remaining -= count;
            return count;
        }
    }
}
