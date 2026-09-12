using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GoonNet;

/// <summary>
/// On-air audio output: one always-open device fed by a mixer, so a fading item can
/// overlap the next one. Collects master peak levels for the VU meter.
/// </summary>
public sealed class PlayoutEngine : IDisposable
{
    private readonly WaveFormat _mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    public WaveFormat MixFormat => _mixFormat;
    private readonly MixingSampleProvider _mixer;
    private readonly MeteringSampleProvider _meter;
    private WaveOut? _output;

    private readonly object _peakLock = new();
    private float _peakLeft;
    private float _peakRight;

    /// <summary>Raised on the audio thread when a deck has played out or finished fading.</summary>
    public event Action<PlayoutDeck>? DeckEnded;

    public PlayoutEngine()
    {
        _mixer = new MixingSampleProvider(_mixFormat) { ReadFully = true };
        _mixer.MixerInputEnded += (_, e) =>
        {
            if (e.SampleProvider is PlayoutDeck deck) DeckEnded?.Invoke(deck);
        };

        // ~100 level readings per second.
        _meter = new MeteringSampleProvider(_mixer, _mixFormat.SampleRate / 100);
        _meter.StreamVolume += OnStreamVolume;
    }

    /// <summary>Opens the output device if it isn't open yet. Throws if no device is available.</summary>
    public void Start()
    {
        if (_output is not null) return;

        // ~125 ms average latency.
        var output = new WaveOut { BufferMilliseconds = 50, NumberOfBuffers = 3 };
        try
        {
            output.Init(new SampleToWaveProvider16(_meter));
            output.Play();
        }
        catch
        {
            output.Dispose();
            throw;
        }
        _output = output;
    }

    /// <summary>
    /// Puts <paramref name="track"/> on air: its audio file, or <paramref name="silence"/> of that
    /// length (the Wait command). Throws if the file can't be read.
    /// </summary>
    public PlayoutDeck Play(LibraryTrack track, TimeSpan? silence = null)
    {
        Start();
        var deck = silence is { } length
            ? new PlayoutDeck(track, _mixFormat, length)
            : new PlayoutDeck(track, _mixFormat);
        deck.Start();
        _mixer.AddMixerInput(deck);
        return deck;
    }

    public void Pause() => _output?.Pause();
    // Play() on a paused WaveOut continues from where it was paused.
    public void Resume() => _output?.Play();

    /// <summary>Adds any source already in <see cref="MixFormat"/> (e.g. a mix preview).</summary>
    public void AddInput(ISampleProvider input)
    {
        Start();
        _mixer.AddMixerInput(input);
    }

    /// <summary>Highest master peaks (0..1) since the previous call.</summary>
    public (float Left, float Right) TakePeaks()
    {
        lock (_peakLock)
        {
            var peaks = (_peakLeft, _peakRight);
            _peakLeft = 0;
            _peakRight = 0;
            return peaks;
        }
    }

    private void OnStreamVolume(object? sender, StreamVolumeEventArgs e)
    {
        lock (_peakLock)
        {
            _peakLeft = Math.Max(_peakLeft, e.MaxSampleValues[0]);
            _peakRight = Math.Max(_peakRight, e.MaxSampleValues[1]);
        }
    }

    public void Dispose()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;

        foreach (var deck in _mixer.MixerInputs.OfType<PlayoutDeck>().ToList())
            deck.Dispose();
        _mixer.RemoveAllMixerInputs();
    }
}
