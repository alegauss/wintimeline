using WinTimeline.Model;

namespace WinTimeline.Engine.Timeline;

/// <summary>WT53: what a snap landed on.</summary>
public enum SnapKind
{
    /// <summary>A cut: where an item starts or ends on any lane.</summary>
    EditPoint,

    /// <summary>A marker on an item or a track.</summary>
    Marker,

    /// <summary>The playhead.</summary>
    Playhead,

    /// <summary>The in or out point.</summary>
    InOut,
}

/// <summary>A place a drag can snap to.</summary>
/// <param name="Seconds">Where, from the timeline's start.</param>
/// <param name="Kind">What it is.</param>
public sealed record SnapTarget(double Seconds, SnapKind Kind);

/// <summary>Where a dragged time ends up: the target it snapped to, or where it was when nothing was near.</summary>
/// <param name="Seconds">The time to use.</param>
/// <param name="Target">The target, or null.</param>
/// <param name="Frames">How far the snap moved it, in frames; 0 without a target.</param>
public sealed record Snapped(double Seconds, SnapTarget? Target, long Frames);

/// <summary>
/// WT53: the places a drag snaps to on a timeline, its cuts and markers, kept sorted so the
/// nearest is a binary search per mouse move. The playhead and in/out move too often to keep, so
/// they are passed with each question.
/// </summary>
public sealed class SnapTargets
{
    /// <summary>How near, in pixels, a target has to be to win.</summary>
    public const double TolerancePixels = 8;

    private readonly double[] _seconds;
    private readonly SnapKind[] _kinds;

    private SnapTargets(List<SnapTarget> targets)
    {
        targets.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
        _seconds = [.. targets.Select(t => t.Seconds)];
        _kinds = [.. targets.Select(t => t.Kind)];
    }

    /// <summary>How many there are.</summary>
    public int Count => _seconds.Length;

    /// <summary>A layout's cuts on every lane, and the markers on its items and tracks.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The targets.</returns>
    public static SnapTargets Of(TimelineLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        List<SnapTarget> targets = [.. EditPoints.Of(layout).Select(s => new SnapTarget(s, SnapKind.EditPoint))];
        targets.AddRange(TimelineMarkers.Of(layout).Select(m => new SnapTarget(m.Start, SnapKind.Marker)));
        return new SnapTargets(targets);
    }

    /// <summary>Snaps a time to the nearest target within <see cref="TolerancePixels"/> at a zoom.</summary>
    /// <param name="seconds">Where the drag is.</param>
    /// <param name="pixelsPerSecond">The view's zoom, which turns the pixel tolerance into time.</param>
    /// <param name="rate">The frame rate the move is counted in.</param>
    /// <param name="moving">The playhead and in/out where they are not what is being dragged.</param>
    /// <returns>Where it lands.</returns>
    public Snapped Snap(double seconds, double pixelsPerSecond, double rate, params IEnumerable<SnapTarget> moving)
    {
        ArgumentNullException.ThrowIfNull(moving);
        double reach = TolerancePixels / Math.Max(pixelsPerSecond, 1e-9);
        SnapTarget? best = null;
        int at = Array.BinarySearch(_seconds, seconds);
        at = at < 0 ? ~at : at;
        for (int i = Math.Max(0, at - 1); i <= Math.Min(_seconds.Length - 1, at); i++)
        {
            best = Nearer(best, new SnapTarget(_seconds[i], _kinds[i]), seconds, reach);
        }

        foreach (SnapTarget target in moving)
        {
            best = Nearer(best, target, seconds, reach);
        }

        return best is null
            ? new Snapped(seconds, null, 0)
            : new Snapped(best.Seconds, best, (long)Math.Round((best.Seconds - seconds) * rate));
    }

    private static SnapTarget? Nearer(SnapTarget? best, SnapTarget candidate, double seconds, double reach)
    {
        double distance = Math.Abs(candidate.Seconds - seconds);
        return distance > reach || (best is not null && Math.Abs(best.Seconds - seconds) <= distance) ? best : candidate;
    }
}
