namespace WinTimeline.Engine.Timeline;

/// <summary>WT57: what a heat lane on the overview strip counts.</summary>
public enum HeatKind
{
    /// <summary>Cuts on every lane: where the editing is dense.</summary>
    Cuts,

    /// <summary>Problems the validator found on items.</summary>
    Problems,

    /// <summary>Markers on the timeline, its tracks and its items.</summary>
    Markers,
}

/// <summary>
/// WT57: one heat lane: the times of what it counts, and how many fall in each of a number of
/// equal bins across the programme, scaled so the busiest bin is 1. Silence and AI suggestions
/// join as kinds when the blocks that find them land.
/// </summary>
public sealed class HeatLane
{
    private readonly double[] _times;

    private HeatLane(HeatKind kind, double[] times, float[] bins)
    {
        Kind = kind;
        _times = times;
        Bins = bins;
    }

    /// <summary>What it counts.</summary>
    public HeatKind Kind { get; }

    /// <summary>How hot each bin is, 0 to 1, left to right.</summary>
    public IReadOnlyList<float> Bins { get; }

    /// <summary>How many things it counts.</summary>
    public int Count => _times.Length;

    /// <summary>A lane of times across a programme.</summary>
    /// <param name="kind">What they are.</param>
    /// <param name="times">Where each one is, in seconds.</param>
    /// <param name="duration">The programme's length.</param>
    /// <param name="bins">How many bins across it, at least 1.</param>
    /// <returns>The lane.</returns>
    public static HeatLane Of(HeatKind kind, IEnumerable<double> times, double duration, int bins)
    {
        ArgumentNullException.ThrowIfNull(times);
        bins = Math.Max(1, bins);
        double[] sorted = [.. times.Where(t => t >= 0 && t <= duration).Order()];
        int[] counts = new int[bins];
        foreach (double t in sorted)
        {
            counts[Math.Min(bins - 1, (int)(t / Math.Max(duration, 1e-9) * bins))]++;
        }

        int most = Math.Max(1, counts.Max());
        return new HeatLane(kind, sorted, [.. counts.Select(c => (float)c / most)]);
    }

    /// <summary>The counted thing nearest a time, within a reach; null when none is that near.</summary>
    /// <param name="seconds">Where the click is.</param>
    /// <param name="reach">How far, in seconds, a hot spot may be.</param>
    /// <returns>Its time, or null.</returns>
    public double? Nearest(double seconds, double reach)
    {
        int at = Array.BinarySearch(_times, seconds);
        at = at < 0 ? ~at : at;
        double? best = null;
        for (int i = Math.Max(0, at - 1); i <= Math.Min(_times.Length - 1, at); i++)
        {
            if (Math.Abs(_times[i] - seconds) <= reach && (best is null || Math.Abs(_times[i] - seconds) < Math.Abs(best.Value - seconds)))
            {
                best = _times[i];
            }
        }

        return best;
    }
}
