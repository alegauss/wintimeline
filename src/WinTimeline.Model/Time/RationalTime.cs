using System.Globalization;

namespace WinTimeline.Model.Time;

/// <summary>Whether a timecode is drop-frame: decided by the rate, or forced either way.</summary>
public enum DropFrame
{
    /// <summary>Drop-frame at 29.97 and 59.94, non-drop otherwise.</summary>
    InferFromRate,

    /// <summary>Drop-frame; refused at a rate that has none.</summary>
    ForceYes,

    /// <summary>Non-drop, whatever the rate.</summary>
    ForceNo,
}

/// <summary>
/// A point in time as a value at a rate, with OpenTimelineIO's exact semantics (opentime 0.18.1):
/// arithmetic rescales the lower-rate side to the higher rate, comparison is by seconds, and
/// equality rescales the left side to the right's rate. Ported operation by operation, in the
/// same floating-point order, so a computed value agrees with the reference to the bit (WT12).
/// </summary>
/// <param name="Value">The count of units.</param>
/// <param name="Rate">Units per second.</param>
public readonly record struct RationalTime(double Value, double Rate) : IComparable<RationalTime>
{
    // opentime's smpte_timecode_rates: 11 slots, ten filled, so the last is 0.0, which
    // nearest_smpte_timecode_rate can return for a rate near zero, exactly as the reference does.
    private static readonly double[] SmpteRates =
    [
        24000.0 / 1001.0, 24.0, 25.0, 30000.0 / 1001.0, 30.0, 48000.0 / 1001.0, 48.0, 50.0, 60000.0 / 1001.0, 60.0, 0.0,
    ];

    private static readonly double[] DropFrameRates = [30000.0 / 1001.0, 60000.0 / 1001.0];

    /// <summary>OpenTimelineIO's default time: zero at rate 1.</summary>
    public RationalTime()
        : this(0, 1)
    {
    }

    /// <summary>Whether the value or rate is NaN, or the rate is not positive.</summary>
    public bool IsInvalid => double.IsNaN(Rate) || double.IsNaN(Value) || Rate <= 0;

    /// <summary>The time in seconds.</summary>
    public double Seconds => Value / Rate;

    /// <summary>The value this time has at another rate.</summary>
    /// <param name="newRate">The rate.</param>
    /// <returns>The value.</returns>
    public double ValueRescaledTo(double newRate) => newRate == Rate ? Value : Value * newRate / Rate;

    /// <summary>This time at another rate.</summary>
    /// <param name="newRate">The rate.</param>
    /// <returns>The time.</returns>
    public RationalTime RescaledTo(double newRate) => new(ValueRescaledTo(newRate), newRate);

    /// <summary>This time at another time's rate.</summary>
    /// <param name="other">The time whose rate to take.</param>
    /// <returns>The time.</returns>
    public RationalTime RescaledTo(RationalTime other) => RescaledTo(other.Rate);

    /// <summary>The value rounded down, at the same rate.</summary>
    /// <returns>The time.</returns>
    public RationalTime Floor() => new(Math.Floor(Value), Rate);

    /// <summary>Whether two times are within a delta of each other at this time's rate.</summary>
    /// <param name="other">The other time.</param>
    /// <param name="delta">The allowed difference, in this time's units.</param>
    /// <returns>Whether they are almost equal.</returns>
    public bool AlmostEqual(RationalTime other, double delta = 0) => Math.Abs(ValueRescaledTo(other.Rate) - other.Value) <= delta;

    /// <summary>OpenTimelineIO's addition: the lower-rate side is rescaled to the higher rate.</summary>
    /// <param name="left">A time.</param>
    /// <param name="right">A time.</param>
    /// <returns>The sum.</returns>
    public static RationalTime operator +(RationalTime left, RationalTime right) =>
        left.Rate < right.Rate
            ? new RationalTime(left.ValueRescaledTo(right.Rate) + right.Value, right.Rate)
            : new RationalTime(right.ValueRescaledTo(left.Rate) + left.Value, left.Rate);

    /// <summary>OpenTimelineIO's subtraction, rescaled as <see cref="op_Addition"/>.</summary>
    /// <param name="left">A time.</param>
    /// <param name="right">A time.</param>
    /// <returns>The difference.</returns>
    public static RationalTime operator -(RationalTime left, RationalTime right) =>
        left.Rate < right.Rate
            ? new RationalTime(left.ValueRescaledTo(right.Rate) - right.Value, right.Rate)
            : new RationalTime(left.Value - right.ValueRescaledTo(left.Rate), left.Rate);

    /// <summary>The negated time.</summary>
    /// <param name="time">A time.</param>
    /// <returns>Its negation.</returns>
    public static RationalTime operator -(RationalTime time) => new(-time.Value, time.Rate);

    /// <summary>Compared by seconds.</summary>
    /// <param name="left">A time.</param>
    /// <param name="right">A time.</param>
    /// <returns>Whether left is later.</returns>
    public static bool operator >(RationalTime left, RationalTime right) => left.Value / left.Rate > right.Value / right.Rate;

    /// <summary>Compared by seconds.</summary>
    /// <param name="left">A time.</param>
    /// <param name="right">A time.</param>
    /// <returns>Whether left is no earlier.</returns>
    public static bool operator >=(RationalTime left, RationalTime right) => left.Value / left.Rate >= right.Value / right.Rate;

    /// <summary>Compared by seconds.</summary>
    /// <param name="left">A time.</param>
    /// <param name="right">A time.</param>
    /// <returns>Whether left is earlier.</returns>
    public static bool operator <(RationalTime left, RationalTime right) => !(left >= right);

    /// <summary>Compared by seconds.</summary>
    /// <param name="left">A time.</param>
    /// <param name="right">A time.</param>
    /// <returns>Whether left is no later.</returns>
    public static bool operator <=(RationalTime left, RationalTime right) => !(left > right);

    /// <summary>The later of two times; the first when they are equal, as std::max.</summary>
    /// <param name="a">A time.</param>
    /// <param name="b">A time.</param>
    /// <returns>The later.</returns>
    public static RationalTime Max(RationalTime a, RationalTime b) => a < b ? b : a;

    /// <summary>The earlier of two times; the first when they are equal, as std::min.</summary>
    /// <param name="a">A time.</param>
    /// <param name="b">A time.</param>
    /// <returns>The earlier.</returns>
    public static RationalTime Min(RationalTime a, RationalTime b) => b < a ? b : a;

    /// <summary>Whether a rate is one SMPTE timecode is defined at.</summary>
    /// <param name="rate">The rate.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsSmpteTimecodeRate(double rate) => Array.IndexOf(SmpteRates, rate) >= 0;

    /// <summary>The SMPTE timecode rate nearest a rate, so 29.97 snaps to 30000/1001.</summary>
    /// <param name="rate">The rate.</param>
    /// <returns>The nearest SMPTE rate.</returns>
    public static double NearestSmpteTimecodeRate(double rate)
    {
        double nearest = 0;
        double minimum = double.MaxValue;
        foreach (double smpte in SmpteRates)
        {
            if (smpte == rate)
            {
                return rate;
            }

            double difference = Math.Abs(rate - smpte);
            if (difference >= minimum)
            {
                continue;
            }

            minimum = difference;
            nearest = smpte;
        }

        return nearest;
    }

    /// <summary>
    /// This time as SMPTE timecode at a rate, e.g. "01:00:00:00" or "00:01:00;02". The rate snaps to
    /// the nearest SMPTE rate when within 0.1, and the clock rolls over after 24 hours.
    /// </summary>
    /// <param name="rate">The timecode rate.</param>
    /// <param name="dropFrame">Drop-frame, inferred from the rate unless forced.</param>
    /// <returns>The timecode.</returns>
    /// <exception cref="TimecodeException">The time is negative, the rate has no timecode, or drop-frame was forced at a rate without it.</exception>
    public string ToTimecode(double rate, DropFrame dropFrame = DropFrame.InferFromRate)
    {
        double framesInTargetRate = ValueRescaledTo(rate);
        if (framesInTargetRate < 0)
        {
            throw new TimecodeException("a negative time has no timecode");
        }

        double nearest = NearestSmpteTimecodeRate(rate);
        if (Math.Abs(nearest - rate) > 0.1)
        {
            throw new TimecodeException($"{rate.ToString(CultureInfo.InvariantCulture)} is not a timecode rate");
        }

        rate = nearest;
        bool isDropFrame = Array.IndexOf(DropFrameRates, rate) >= 0;
        if (dropFrame == DropFrame.ForceYes && !isDropFrame)
        {
            throw new TimecodeException($"{rate.ToString(CultureInfo.InvariantCulture)} has no drop-frame timecode");
        }

        if (dropFrame != DropFrame.InferFromRate)
        {
            isDropFrame = dropFrame == DropFrame.ForceYes;
        }

        int dropFrames = 0;
        char divider = ':';
        if (!isDropFrame)
        {
            if (Math.Round(rate, MidpointRounding.AwayFromZero) == 24)
            {
                rate = 24.0;
            }
        }
        else
        {
            if (rate == 30000 / 1001.0)
            {
                dropFrames = 2;
            }
            else if (rate == 60000 / 1001.0)
            {
                dropFrames = 4;
            }

            divider = ';';
        }

        int framesPerHour = (int)Math.Round(rate * 60 * 60, MidpointRounding.AwayFromZero);
        int framesPer24Hours = framesPerHour * 24;
        int framesPer10Minutes = (int)Math.Round(rate * 60 * 10, MidpointRounding.AwayFromZero);
        int framesPerMinute = (int)((Math.Round(rate, MidpointRounding.AwayFromZero) * 60) - dropFrames);

        double value = CMod(framesInTargetRate, framesPer24Hours);
        if (isDropFrame)
        {
            int tenMinuteChunks = (int)Math.Floor(value / framesPer10Minutes);
            int framesOverTenMinutes = (int)CMod(value, framesPer10Minutes);
            if (framesOverTenMinutes > dropFrames)
            {
                // (frames_over_ten_minutes - dropframes) / frames_per_minute is int division in C++.
                value += (dropFrames * 9 * tenMinuteChunks) + (dropFrames * Math.Floor((double)((framesOverTenMinutes - dropFrames) / framesPerMinute)));
            }
            else
            {
                value += dropFrames * 9 * tenMinuteChunks;
            }
        }

        int nominalFps = (int)Math.Ceiling(rate);
        int frames = (int)CMod(value, nominalFps);
        int secondsTotal = (int)Math.Floor(value / nominalFps);
        int seconds = (int)CMod(secondsTotal, 60);
        int minutes = (int)CMod(secondsTotal / 60, 60);
        int hours = secondsTotal / 60 / 60;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}{divider}{frames:00}");
    }

    /// <summary>A SMPTE timecode at a rate as a time; ';' marks drop-frame.</summary>
    /// <param name="timecode">The timecode, "HH:MM:SS:FF" or "HH:MM:SS;FF".</param>
    /// <param name="rate">An exact SMPTE rate.</param>
    /// <returns>The time.</returns>
    /// <exception cref="TimecodeException">The rate has no timecode, the string is malformed, or its frames exceed the rate.</exception>
    public static RationalTime FromTimecode(string timecode, double rate)
    {
        ArgumentNullException.ThrowIfNull(timecode);
        if (!IsSmpteTimecodeRate(rate))
        {
            throw new TimecodeException($"{rate.ToString(CultureInfo.InvariantCulture)} is not a timecode rate");
        }

        bool isDropFrame = Array.IndexOf(DropFrameRates, rate) >= 0;
        if (timecode.Contains(';', StringComparison.Ordinal))
        {
            if (!isDropFrame)
            {
                throw new TimecodeException($"'{timecode}' is drop-frame and {rate.ToString(CultureInfo.InvariantCulture)} is not a drop-frame rate");
            }
        }
        else
        {
            isDropFrame = false;
        }

        int[] fields = new int[4];
        for (int i = 0; i < 4; i++)
        {
            int at = i * 3;
            string field = at < timecode.Length ? timecode.Substring(at, Math.Min(2, timecode.Length - at)) : string.Empty;
            if (!int.TryParse(field, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out fields[i]))
            {
                throw new TimecodeException($"'{timecode}' is not a timecode");
            }
        }

        int nominalFps = (int)Math.Ceiling(rate);
        if (fields[3] >= nominalFps)
        {
            throw new TimecodeException($"'{timecode}' has frames beyond {nominalFps - 1}");
        }

        int dropFrames = !isDropFrame ? 0 : rate == 30000 / 1001.0 ? 2 : 4;
        int totalMinutes = (fields[0] * 60) + fields[1];
        int value = (((totalMinutes * 60) + fields[2]) * nominalFps) + fields[3] - (dropFrames * (totalMinutes - (totalMinutes / 10)));
        return new RationalTime(value, rate);
    }

    /// <summary>A time from seconds at a rate.</summary>
    /// <param name="seconds">Seconds.</param>
    /// <param name="rate">The rate.</param>
    /// <returns>The time.</returns>
    public static RationalTime FromSeconds(double seconds, double rate = 1) => new RationalTime(seconds, 1).RescaledTo(rate);

    /// <summary>OpenTimelineIO's equality: the left side rescaled to the right's rate equals its value.</summary>
    /// <param name="other">The other time.</param>
    /// <returns>Whether they are equal.</returns>
    public bool Equals(RationalTime other) => ValueRescaledTo(other.Rate) == other.Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Seconds.GetHashCode();

    /// <inheritdoc/>
    public int CompareTo(RationalTime other) => this < other ? -1 : this > other ? 1 : 0;

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"RationalTime({Value}, {Rate})");

    // C's fmod: the remainder takes the sign of the dividend.
    private static double CMod(double x, double y) => x % y;
}

/// <summary>A time that has no timecode at the rate asked for, or a string that is not one.</summary>
public sealed class TimecodeException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TimecodeException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    public TimecodeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    /// <param name="inner">The cause.</param>
    public TimecodeException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
