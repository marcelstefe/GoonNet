namespace GoonNet;

/// <summary>One point of an item's volume line: gain (0..1) at a time in the item.</summary>
public readonly record struct VolumePoint(double Seconds, float Gain);

public static class VolumeEnvelope
{
    /// <summary>
    /// Gain at <paramref name="seconds"/>: linear between points, held flat before the first
    /// and after the last point. No points = full volume.
    /// </summary>
    public static float GainAt(IReadOnlyList<VolumePoint> points, double seconds)
    {
        int n = points.Count;
        if (n == 0) return 1f;
        if (seconds <= points[0].Seconds) return points[0].Gain;
        if (seconds >= points[n - 1].Seconds) return points[n - 1].Gain;

        for (int i = 1; i < n; i++)
        {
            var b = points[i];
            if (seconds > b.Seconds) continue;
            var a = points[i - 1];
            var span = b.Seconds - a.Seconds;
            var f = span > 0 ? (seconds - a.Seconds) / span : 1;
            return (float)(a.Gain + (b.Gain - a.Gain) * f);
        }
        return points[n - 1].Gain;
    }
}
