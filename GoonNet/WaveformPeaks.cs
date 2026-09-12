using NAudio.Wave;

namespace GoonNet;

/// <summary>Peak level (highest |sample| over all channels) per ~10 ms bin, for drawing waveforms.</summary>
public sealed class WaveformPeaks
{
    private WaveformPeaks(float[] bins, double secondsPerBin, double lengthSeconds)
    {
        Bins = bins;
        SecondsPerBin = secondsPerBin;
        LengthSeconds = lengthSeconds;
    }

    public float[] Bins { get; }
    public double SecondsPerBin { get; }
    public double LengthSeconds { get; }

    /// <summary>Highest peak between two times in the file (seconds); 0 outside the file.</summary>
    public float Max(double from, double to)
    {
        if (to <= 0 || from >= LengthSeconds || Bins.Length == 0) return 0;
        int i0 = Math.Max(0, (int)(from / SecondsPerBin));
        int i1 = Math.Min(Bins.Length, Math.Max(i0 + 1, (int)Math.Ceiling(to / SecondsPerBin)));
        float max = 0;
        for (int i = i0; i < i1; i++)
            if (Bins[i] > max) max = Bins[i];
        return max;
    }

    /// <summary>Decodes the whole file; call off the UI thread.</summary>
    public static WaveformPeaks Load(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider sp = reader;
        int channels = reader.WaveFormat.Channels;
        int framesPerBin = Math.Max(1, reader.WaveFormat.SampleRate / 100);
        int samplesPerBin = framesPerBin * channels;

        var buffer = new float[samplesPerBin * 32];
        var bins = new List<float>();
        float max = 0;
        int inBin = 0;

        int read;
        while ((read = sp.Read(buffer.AsSpan())) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                float a = Math.Abs(buffer[i]);
                if (a > max) max = a;
                if (++inBin == samplesPerBin)
                {
                    bins.Add(max);
                    max = 0;
                    inBin = 0;
                }
            }
        }
        if (inBin > 0) bins.Add(max);

        return new WaveformPeaks(bins.ToArray(),
            (double)framesPerBin / reader.WaveFormat.SampleRate,
            reader.TotalTime.TotalSeconds);
    }
}
