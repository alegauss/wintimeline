using System.Globalization;

namespace WinTimeline.Model.Time;

/// <summary>
/// A start and a duration, with OpenTimelineIO's semantics (opentime 0.18.1): the exclusive end
/// is the duration plus the start rescaled to the duration's rate, and the inclusive end is one
/// frame before it, or its floor when the duration is fractional.
/// </summary>
/// <param name="StartTime">Where it starts.</param>
/// <param name="Duration">How long it is.</param>
public readonly record struct TimeRange(RationalTime StartTime, RationalTime Duration)
{
    /// <summary>OpenTimelineIO's default range: zero at rate 1 for both.</summary>
    public TimeRange()
        : this(new RationalTime(), new RationalTime())
    {
    }

    /// <summary>The first time after the range.</summary>
    public RationalTime EndTimeExclusive => Duration + StartTime.RescaledTo(Duration);

    /// <summary>The last frame inside the range.</summary>
    public RationalTime EndTimeInclusive
    {
        get
        {
            RationalTime end = EndTimeExclusive;
            if ((end - StartTime.RescaledTo(Duration)).Value > 1)
            {
                return Duration.Value != Math.Floor(Duration.Value)
                    ? end.Floor()
                    : end - new RationalTime(1, Duration.Rate);
            }

            return StartTime;
        }
    }

    /// <summary>The range from a start to an exclusive end, at the start's rate.</summary>
    /// <param name="start">The start.</param>
    /// <param name="endExclusive">The first time after it.</param>
    /// <returns>The range.</returns>
    public static TimeRange FromStartEndTime(RationalTime start, RationalTime endExclusive) =>
        new(start, new RationalTime(endExclusive.ValueRescaledTo(start.Rate) - start.Value, start.Rate));

    /// <summary>The range from a start to an inclusive end, one frame longer than <see cref="FromStartEndTime"/>.</summary>
    /// <param name="start">The start.</param>
    /// <param name="endInclusive">The last frame in it.</param>
    /// <returns>The range.</returns>
    public static TimeRange FromStartEndTimeInclusive(RationalTime start, RationalTime endInclusive) =>
        new(start, new RationalTime(endInclusive.ValueRescaledTo(start.Rate) - start.Value + 1, start.Rate));

    /// <summary>The range grown to cover another.</summary>
    /// <param name="other">The other range.</param>
    /// <returns>The covering range.</returns>
    public TimeRange ExtendedBy(TimeRange other)
    {
        RationalTime start = RationalTime.Min(StartTime, other.StartTime);
        RationalTime end = RationalTime.Max(EndTimeExclusive, other.EndTimeExclusive);
        return new TimeRange(start, new RationalTime(end.ValueRescaledTo(start.Rate) - start.Value, start.Rate));
    }

    /// <summary>The range with its duration extended by a time.</summary>
    /// <param name="other">How much longer.</param>
    /// <returns>The range.</returns>
    public TimeRange DurationExtendedBy(RationalTime other) => new(StartTime, Duration + other);

    /// <summary>Whether a time lies in the range, the start included and the end not.</summary>
    /// <param name="time">The time.</param>
    /// <returns>Whether it does.</returns>
    public bool Contains(RationalTime time) => StartTime <= time && time < EndTimeExclusive;

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"TimeRange({StartTime}, {Duration})");
}
