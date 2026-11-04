using System.Globalization;
using WinTimeline.Model.Time;

namespace WinTimeline.Engine.Timeline;

/// <summary>What the ruler labels time in.</summary>
public enum RulerUnit
{
    /// <summary>SMPTE timecode from the timeline's global start, drop-frame where the rate is.</summary>
    Timecode,

    /// <summary>Frames from the start.</summary>
    Frames,

    /// <summary>35 mm feet and frames, 16 frames to the foot.</summary>
    FeetFrames,

    /// <summary>Seconds from the start.</summary>
    Seconds,
}

/// <summary>One tick on the ruler.</summary>
/// <param name="Frame">The frame it marks, from the timeline's start.</param>
/// <param name="Seconds">Where that is, in seconds.</param>
/// <param name="Label">Its label, or empty for a minor tick.</param>
public sealed record RulerTick(long Frame, double Seconds, string Label)
{
    /// <summary>Whether it carries a label.</summary>
    public bool Major => Label.Length > 0;
}

/// <summary>
/// WT49: the ticks the ruler draws for a view: a step from one frame to an hour, the smallest
/// whose labels stay a readable distance apart, always on a multiple of the step from frame 0 so
/// a label reads the same at every zoom, with four minor ticks between labels where frames allow.
/// Timecode labels are the model's port of the reference library, drop-frame where the rate is.
/// </summary>
public static class Ruler
{
    private static readonly int[] FrameSteps = [1, 2, 5, 10];
    private static readonly int[] SecondSteps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];

    /// <summary>The step between labels, in frames, for a zoom.</summary>
    /// <param name="pixelsPerSecond">The zoom.</param>
    /// <param name="rate">The timeline's frame rate.</param>
    /// <param name="spacing">How far apart labels must be, in pixels.</param>
    /// <returns>The step.</returns>
    public static long Step(double pixelsPerSecond, double rate, double spacing)
    {
        int nominal = (int)Math.Round(rate);
        IEnumerable<long> steps = FrameSteps.Select(f => (long)f).Concat(SecondSteps.Select(s => (long)s * nominal));
        return steps.FirstOrDefault(step => step / rate * pixelsPerSecond >= spacing, (long)SecondSteps[^1] * nominal);
    }

    /// <summary>The ticks for a view.</summary>
    /// <param name="view">The view.</param>
    /// <param name="rate">The timeline's frame rate.</param>
    /// <param name="origin">The timeline's global start time.</param>
    /// <param name="unit">What the labels are in.</param>
    /// <param name="spacing">How far apart labels must be, in pixels.</param>
    /// <returns>The ticks, left to right.</returns>
    public static IReadOnlyList<RulerTick> Ticks(TimelineViewport view, double rate, RationalTime origin, RulerUnit unit, double spacing)
    {
        ArgumentNullException.ThrowIfNull(view);
        long step = Step(view.PixelsPerSecond, rate, spacing);
        long minor = step % 5 == 0 ? step / 5 : step;
        long first = (long)Math.Floor(Math.Max(0, view.Start) * rate / minor) * minor;
        long last = (long)Math.Ceiling(view.End * rate);
        List<RulerTick> ticks = [];
        for (long frame = first; frame <= last; frame += minor)
        {
            ticks.Add(new RulerTick(frame, frame / rate, frame % step == 0 ? Label(frame, rate, origin, unit) : string.Empty));
        }

        return ticks;
    }

    /// <summary>A frame as the ruler labels it.</summary>
    /// <param name="frame">Frames from the start.</param>
    /// <param name="rate">The frame rate.</param>
    /// <param name="origin">The timeline's global start time.</param>
    /// <param name="unit">What the label is in.</param>
    /// <returns>The label.</returns>
    public static string Label(long frame, double rate, RationalTime origin, RulerUnit unit) => unit switch
    {
        RulerUnit.Frames => frame.ToString(CultureInfo.InvariantCulture),
        RulerUnit.FeetFrames => string.Create(CultureInfo.InvariantCulture, $"{frame / 16}+{frame % 16:00}"),
        RulerUnit.Seconds => string.Create(CultureInfo.InvariantCulture, $"{frame / rate:0.###} s"),
        _ => Timecode(frame, rate, origin),
    };

    private static string Timecode(long frame, double rate, RationalTime origin)
    {
        try
        {
            return (origin.RescaledTo(rate) + new RationalTime(frame, rate)).ToTimecode(rate);
        }
        catch (TimecodeException)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{frame / rate:0.###} s");
        }
    }
}
