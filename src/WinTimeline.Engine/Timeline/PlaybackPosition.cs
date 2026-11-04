namespace WinTimeline.Engine.Timeline;

/// <summary>
/// WT51: the current frame, one per shown timeline, that the timeline's playhead, the viewer and
/// the transport all read and set, so a step, a jump or a scrub reaches every one of them at once.
/// Positions are whole frames: a time between frames is the frame it falls in.
/// </summary>
public sealed class PlaybackPosition
{
    /// <summary>Raised after the frame changes, with the frame.</summary>
    public event EventHandler<long>? Changed;

    /// <summary>The frame rate positions are counted in.</summary>
    public double Rate { get; private set; } = 24;

    /// <summary>The last frame, or -1 for an empty timeline.</summary>
    public long LastFrame { get; private set; } = -1;

    /// <summary>The current frame, from the timeline's start.</summary>
    public long Frame { get; private set; }

    /// <summary>The current frame as seconds.</summary>
    public double Seconds => Frame / Rate;

    /// <summary>Starts over for a timeline: its rate and length, at its first frame.</summary>
    /// <param name="rate">Its frame rate.</param>
    /// <param name="duration">Its length in seconds.</param>
    public void Reset(double rate, double duration)
    {
        Rate = rate > 0 ? rate : 24;
        LastFrame = (long)Math.Ceiling(duration * Rate) - 1;
        Go(0);
    }

    /// <summary>Follows a timeline whose length changed, keeping the frame if it is still on it.</summary>
    /// <param name="duration">Its new length in seconds.</param>
    public void Lengthen(double duration)
    {
        LastFrame = (long)Math.Ceiling(duration * Rate) - 1;
        if (Frame > Math.Max(0, LastFrame))
        {
            Go(LastFrame);
        }
    }

    /// <summary>Goes to a frame, held within the timeline.</summary>
    /// <param name="frame">The frame.</param>
    public void Go(long frame)
    {
        long held = Math.Clamp(frame, 0, Math.Max(0, LastFrame));
        Frame = held;
        Changed?.Invoke(this, held);
    }

    /// <summary>Goes to the frame a time falls in.</summary>
    /// <param name="seconds">The time.</param>
    public void GoTo(double seconds) => Go((long)Math.Floor((seconds * Rate) + 1e-6));

    /// <summary>Steps by frames, back when negative.</summary>
    /// <param name="frames">How many.</param>
    public void Step(long frames) => Go(Frame + frames);

    /// <summary>Jumps to the next edit point after the current frame, or the previous one before it.</summary>
    /// <param name="editPoints">The edit points, in seconds, sorted.</param>
    /// <param name="forward">Whether to go forward.</param>
    public void Jump(IReadOnlyList<double> editPoints, bool forward)
    {
        ArgumentNullException.ThrowIfNull(editPoints);
        IEnumerable<long> frames = editPoints.Select(s => (long)Math.Round(s * Rate)).Distinct();
        long? to = forward ? frames.Where(f => f > Frame).Cast<long?>().FirstOrDefault() : frames.Where(f => f < Frame).Cast<long?>().LastOrDefault();
        Go(to ?? (forward ? LastFrame : 0));
    }
}

/// <summary>WT51: how the timeline's view keeps the playhead in sight.</summary>
public enum PlayheadFollow
{
    /// <summary>Jumps a view on when the playhead leaves it.</summary>
    Page,

    /// <summary>Keeps the playhead centred.</summary>
    Smooth,

    /// <summary>Never moves the view.</summary>
    Off,
}

/// <summary>Where cuts are, for the playhead's Up and Down.</summary>
public static class EditPoints
{
    /// <summary>Every clip, gap and transition boundary on a timeline's lanes, sorted, once each.</summary>
    /// <param name="layout">The timeline's layout.</param>
    /// <returns>The edit points, in seconds.</returns>
    public static IReadOnlyList<double> Of(TimelineLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return [.. layout.Lanes.SelectMany(l => l.Items).Where(i => i.Kind != LaneItemKind.Transition).SelectMany(i => new[] { i.Start, i.End }).Select(s => Math.Round(s, 9)).Distinct().Order()];
    }
}
