using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WPFSoundVisualizationLib;

namespace GoonNet;

/// <summary>
/// Playback + waveform + peak-metering source for the Import File preview.
/// Implements IWaveformPlayer so it can drive WPFSVL's WaveformTimeline.
/// </summary>
public sealed class WaveformPreviewPlayer : IWaveformPlayer, IDisposable
{
    private const int SamplesPerPixel = 2000;

    private AudioFileReader? _reader;
    private WaveOutEvent? _output;
    private MeteringSampleProvider? _meter;
    private readonly DispatcherTimer _positionTimer;

    private float[] _waveformData = Array.Empty<float>();
    private double _channelLength;
    private double _channelPosition;
    private TimeSpan _selectionBegin;
    private TimeSpan _selectionEnd;
    private bool _isPlaying;
    private float _peakLeft;
    private float _peakRight;
    public string? LastError { get; set; }

    public WaveformPreviewPlayer()
    {
        _positionTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _positionTimer.Tick += (_, _) =>
        {
            if (_reader != null)
                ChannelPosition = _reader.CurrentTime.TotalSeconds;
        };
    }

    public float[] WaveformData
    {
        get => _waveformData;
        private set { _waveformData = value; OnPropertyChanged(); }
    }

    public double ChannelLength
    {
        get => _channelLength;
        private set { _channelLength = value; OnPropertyChanged(); }
    }

    public double ChannelPosition
    {
        get => _channelPosition;
        set
        {
            if (Math.Abs(_channelPosition - value) < 0.0001) return;
            _channelPosition = value;
            if (_reader != null && Math.Abs(_reader.CurrentTime.TotalSeconds - value) > 0.15)
            {
                try { _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(value, 0, _channelLength)); }
                catch { /* ignore seek errors */ }
            }
            OnPropertyChanged();
        }
    }

    public TimeSpan SelectionBegin
    {
        get => _selectionBegin;
        set { _selectionBegin = value; OnPropertyChanged(); }
    }

    public TimeSpan SelectionEnd
    {
        get => _selectionEnd;
        set { _selectionEnd = value; OnPropertyChanged(); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set { _isPlaying = value; OnPropertyChanged(); }
    }

    public float PeakLeft
    {
        get => _peakLeft;
        private set { _peakLeft = value; OnPropertyChanged(); }
    }

    public float PeakRight
    {
        get => _peakRight;
        private set { _peakRight = value; OnPropertyChanged(); }
    }

    public bool GetFFTData(float[] fftDataBuffer) => false;
    public int GetFFTFrequencyIndex(int frequency) => 0;

    public void Load(string filePath)
    {
        Stop();
        DisposeReaderAndOutput();

        // Scan waveform on a separate reader so playback starts from a fresh stream.
        using (var scanReader = new AudioFileReader(filePath))
        {
            ChannelLength = scanReader.TotalTime.TotalSeconds;
            BuildWaveformCache(scanReader);
        }

        _reader = new AudioFileReader(filePath);
        _channelPosition = 0;
        OnPropertyChanged(nameof(ChannelPosition));

        _meter = new MeteringSampleProvider(_reader, _reader.WaveFormat.SampleRate / 60);
        _meter.StreamVolume += OnStreamVolume;

        _output = new WaveOutEvent();
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(new SampleToWaveProvider16(_meter));
    }

    private void BuildWaveformCache(AudioFileReader reader)
    {
        ISampleProvider sp = reader;
        int channels = reader.WaveFormat.Channels;
        var buffer = new float[SamplesPerPixel * channels];
        var peaks = new List<float>();

        int read;
        while ((read = sp.Read(buffer.AsSpan())) > 0)
        {
            float lMin = 0f, lMax = 0f, rMin = 0f, rMax = 0f;
            if (channels >= 2)
            {
                for (int i = 0; i + 1 < read; i += channels)
                {
                    float l = buffer[i], r = buffer[i + 1];
                    if (l < lMin) lMin = l;
                    if (l > lMax) lMax = l;
                    if (r < rMin) rMin = r;
                    if (r > rMax) rMax = r;
                }
            }
            else
            {
                for (int i = 0; i < read; i++)
                {
                    float s = buffer[i];
                    if (s < lMin) lMin = s;
                    if (s > lMax) lMax = s;
                }
                rMin = lMin; rMax = lMax;
            }
            peaks.Add(lMax); peaks.Add(lMin);
            peaks.Add(rMax); peaks.Add(rMin);
        }
        WaveformData = peaks.ToArray();
    }

    public void Play()
    {
        if (_output == null || _reader == null) return;
        if (_output.PlaybackState == PlaybackState.Playing) return;

        // If we previously played to the end, rewind.
        if (_reader.Position >= _reader.Length)
            _reader.Position = 0;

        try
        {
            _output.Play();
            IsPlaying = true;
            _positionTimer.Start();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public void Pause()
    {
        if (_output == null) return;
        if (_output.PlaybackState == PlaybackState.Playing)
        {
            _output.Pause();
            IsPlaying = false;
            _positionTimer.Stop();
        }
    }

    public void Stop()
    {
        _output?.Stop();
        if (_reader != null) _reader.Position = 0;
        IsPlaying = false;
        _positionTimer.Stop();
        PeakLeft = 0;
        PeakRight = 0;
        if (_reader != null) ChannelPosition = 0;
    }

    public void Seek(double seconds)
    {
        if (_reader == null) return;
        try
        {
            _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, _channelLength));
            _channelPosition = _reader.CurrentTime.TotalSeconds;
            OnPropertyChanged(nameof(ChannelPosition));
        }
        catch { /* ignore seek errors */ }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            LastError = e.Exception.Message;
        IsPlaying = false;
        _positionTimer.Stop();
        PeakLeft = 0;
        PeakRight = 0;
    }

    private void OnStreamVolume(object? sender, StreamVolumeEventArgs e)
    {
        if (e.MaxSampleValues.Length > 0) PeakLeft = e.MaxSampleValues[0];
        if (e.MaxSampleValues.Length > 1) PeakRight = e.MaxSampleValues[1];
        else PeakRight = PeakLeft;
    }

    private void DisposeReaderAndOutput()
    {
        if (_output != null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Dispose();
            _output = null;
        }
        if (_meter != null)
        {
            _meter.StreamVolume -= OnStreamVolume;
            _meter = null;
        }
        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        DisposeReaderAndOutput();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}
