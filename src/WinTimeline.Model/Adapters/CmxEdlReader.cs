using System.Globalization;
using System.Text.RegularExpressions;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Adapters;

/// <summary>An EDL the reader cannot make sense of, where the reference adapter refuses it too.</summary>
public sealed class EdlParseException : Exception
{
    /// <summary>Creates the exception.</summary>
    public EdlParseException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    public EdlParseException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    /// <param name="inner">The cause.</param>
    public EdlParseException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Reads CMX 3600 EDLs into the model (WT15): a port of the reader in OpenTimelineIO's own
/// otio-cmx3600-adapter (cmx_3600.py, EDLParser, ClipHandler and CommentHandler), step for step,
/// so a timeline read here is written by <see cref="OtioWriter"/> exactly as the reference writes
/// the one it reads. Channels become tracks (V, A1, A2, ...; B and AA expand), sparse record
/// timecodes become gaps, D and W events become transitions, M2 and FREEZE FRAME become speed
/// effects, LOC comments markers, ASC_SOP/ASC_SAT a <c>cdl</c> metadata entry, and anything else
/// in a comment is kept under <c>cmx_3600.comments</c>.
/// </summary>
public sealed partial class CmxEdlReader
{
    private static readonly Dictionary<string, string[]> ChannelMap = new(StringComparer.Ordinal)
    {
        ["A"] = ["A1"],
        ["AA"] = ["A1", "A2"],
        ["B"] = ["V", "A1"],
        ["A2/V"] = ["V", "A2"],
        ["AA/V"] = ["V", "A1", "A2"],
    };

    // The reference's CommentHandler.comment_id_map, in its order: FROM CLIP NAME before FROM CLIP.
    private static readonly (Regex Pattern, string Kind)[] CommentKinds =
    [
        (Comment("FROM CLIP NAME"), "clip_name"),
        (Comment("TO CLIP NAME"), "dest_clip_name"),
        (Comment("FROM CLIP"), "media_reference"),
        (Comment("FROM FILE"), "media_reference"),
        (Comment("LOC"), "locators"),
        (Comment("ASC_SOP"), "asc_sop"),
        (Comment("ASC_SAT"), "asc_sat"),
        (Comment("M2"), "motion_effect"),
        (Comment(@"\* FREEZE FRAME"), "freeze_frame"),
        (Comment(@"\* OTIO REFERENCE [a-zA-Z]+"), "media_reference"),
    ];

    private static readonly string[] MarkerColors = ["PINK", "RED", "ORANGE", "YELLOW", "GREEN", "CYAN", "BLUE", "PURPLE", "MAGENTA", "BLACK", "WHITE"];

    private readonly Timeline _timeline;
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private readonly bool _ignoreTimecodeMismatch;

    private CmxEdlReader(bool ignoreTimecodeMismatch)
    {
        _ignoreTimecodeMismatch = ignoreTimecodeMismatch;
        _timeline = new Timeline(Fields(("tracks", new Stack(Fields(("name", "tracks"))))));
    }

    /// <summary>Reads an EDL.</summary>
    /// <param name="edl">The EDL's text.</param>
    /// <param name="rate">The frame rate its timecodes count in; EDLs do not say. 24 unless given.</param>
    /// <param name="ignoreTimecodeMismatch">Place clips by their source timecode where the record timecode disagrees, rather than refusing the EDL.</param>
    /// <returns>The timeline.</returns>
    /// <exception cref="EdlParseException">The EDL is malformed, or its timecodes disagree and mismatches are not ignored.</exception>
    public static Timeline Read(string edl, double rate = 24, bool ignoreTimecodeMismatch = false)
    {
        ArgumentNullException.ThrowIfNull(edl);
        CmxEdlReader reader = new(ignoreTimecodeMismatch);
        try
        {
            reader.Parse(edl, rate);
        }
        catch (TimecodeException ex)
        {
            throw new EdlParseException(ex.Message, ex);
        }
        catch (TimelineRangeException ex)
        {
            throw new EdlParseException(ex.Message, ex);
        }

        return reader._timeline;
    }

    private static Regex Comment(string id) => new(@"^\*?\s*" + id + @":?\s*(?<comment_body>.*)", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static Metadata Fields(params (string Key, object? Value)[] fields)
    {
        Metadata metadata = new();
        foreach ((string key, object? value) in fields)
        {
            metadata.Set(key, value);
        }

        return metadata;
    }

    // A deep copy, through the writer and reader proven lossless (WT13): a B channel event puts
    // one clip on V and another on A1.
    private static T Copy<T>(T value)
        where T : ModelObject => (T)OtioReader.ReadString(OtioWriter.Write(value));

    private void Parse(string edl, double rate)
    {
        List<string> lines = [.. edl.Split(["\r\n", "\n", "\r", "\v", "\f", "\u001c", "\u001d", "\u001e", "\u0085", "\u2028", "\u2029"], StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)];
        int at = 0;
        while (at < lines.Count)
        {
            string line = lines[at++];
            if (line.StartsWith("TITLE:", StringComparison.Ordinal))
            {
                _timeline.Name = line.Replace("TITLE:", string.Empty, StringComparison.Ordinal).Trim();
            }
            else if (line.StartsWith("FCM", StringComparison.Ordinal))
            {
                // Tape timecode mode: nothing to read.
            }
            else if (line.StartsWith("SPLIT", StringComparison.Ordinal))
            {
                bool audio = line.Contains("AUDIO DELAY", StringComparison.Ordinal);
                bool video = line.Contains("VIDEO DELAY", StringComparison.Ordinal);
                if (audio && video)
                {
                    throw new EdlParseException("both audio and video delay declared after SPLIT.");
                }

                if (!(audio || video))
                {
                    throw new EdlParseException("either audio or video delay declared after SPLIT.");
                }

                if (at + 1 >= lines.Count)
                {
                    throw new EdlParseException("SPLIT is not followed by two events.");
                }

                string first = lines[at++];
                string second = lines[at++];
                List<string> comments = [];
                while (at < lines.Count && !char.IsAsciiDigit(lines[at][0]))
                {
                    comments.Add(lines[at++]);
                }

                AddClip(first, comments, rate, null);
                AddClip(second, comments, rate, null);
            }
            else if (char.IsDigit(line[0]))
            {
                string? transitionLine = null;
                List<string> comments = [];
                int eventId = EventId(line)!.Value;
                while (at < lines.Count)
                {
                    if (EventId(lines[at]) is not { } next)
                    {
                        comments.Add(lines[at++]);
                    }
                    else if (next == eventId)
                    {
                        if (transitionLine is not null)
                        {
                            throw new EdlParseException($"Invalid transition {lines[at]}");
                        }

                        transitionLine = lines[at++];
                    }
                    else
                    {
                        break;
                    }
                }

                AddClip(line, comments, rate, transitionLine);
            }
            else
            {
                throw new EdlParseException("Unknown event type");
            }
        }

        foreach (Track track in _timeline.Tracks.Children.OfType<Track>())
        {
            if (track.SourceRange == track.AvailableRange())
            {
                track.SourceRange = null;
            }
        }
    }

    private static int? EventId(string line)
    {
        Match m = LeadingDigits().Match(line);
        return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    }

    private void AddClip(string line, List<string> commentLines, double rate, string? transitionLine)
    {
        Comments comments = new(commentLines);
        ClipHandler handler = new(line, comments.Handled, rate, transitionLine);
        Clip clip = handler.Clip;
        Transition? transition = handler.Transition;

        if (comments.Unhandled.Count > 0)
        {
            Metadata cmx = CmxMetadata(clip);
            List<object?> all = cmx.TryGetValue("comments", out object? existing) && existing is IEnumerable<object?> list ? [.. list] : [];
            all.AddRange(comments.Unhandled);
            cmx.Set("comments", all);
        }

        if (handler.Reel is { } reel && reel != "AX")
        {
            CmxMetadata(clip).Set("reel", reel);
        }

        double edlRate = handler.EdlRate;
        RationalTime recordIn = RationalTime.FromTimecode(handler.RecordTcIn, edlRate);
        RationalTime recordOut = RationalTime.FromTimecode(handler.RecordTcOut, edlRate);
        RationalTime sourceDuration = clip.Duration;
        RationalTime recordDuration = recordOut - recordIn;
        if (recordDuration != sourceDuration)
        {
            comments.Handled.TryGetValue("motion_effect", out object? motion);
            comments.Handled.TryGetValue("freeze_frame", out object? freeze);
            if (motion is not null || freeze is not null)
            {
                clip.SourceRange = new TimeRange(clip.SourceRange!.Value.StartTime, recordDuration);
                if (freeze is not null)
                {
                    clip.AddEffect(new LinearTimeWarp(Fields(("effect_name", "FreezeFrame"), ("time_scalar", 0.0)), "FreezeFrame"));
                    if (clip.Name.EndsWith(" FF", StringComparison.Ordinal))
                    {
                        clip.Name = clip.Name[..^3];
                    }
                }
                else
                {
                    double fps = PythonFloat(SpeedEffect().Match((string)motion!).Groups["speed"].Value);
                    clip.AddEffect(new LinearTimeWarp(Fields(("effect_name", "LinearTimeWarp"), ("time_scalar", fps / rate)), "LinearTimeWarp"));
                }
            }
            else if (_ignoreTimecodeMismatch)
            {
                recordOut = recordIn + sourceDuration;
            }
            else
            {
                throw new EdlParseException($"Source and record duration don't match: {sourceDuration} != {recordDuration} for clip {clip.Name}");
            }
        }

        string[] names = ChannelMap.TryGetValue(handler.ChannelCode, out string[]? mapped) ? mapped : [handler.ChannelCode];
        List<Track> tracks = [.. names.Select(TrackFor)];
        foreach (Track track in tracks)
        {
            Transition? trackTransition = transition;
            Clip trackClip = clip;
            if (tracks.Count > 1)
            {
                trackClip = Copy(clip);
                if (transition is not null)
                {
                    trackTransition = Copy(transition);
                }
            }

            if (track.SourceRange is null)
            {
                RationalTime zero = new(0, edlRate);
                track.SourceRange = new TimeRange(zero - recordIn, zero);
            }

            RationalTime trackEnd = track.Duration - track.SourceRange!.Value.StartTime;
            if (recordIn < trackEnd)
            {
                if (_ignoreTimecodeMismatch)
                {
                    recordIn = trackEnd;
                    recordOut = recordIn + recordDuration;
                }
                else
                {
                    throw new EdlParseException($"Overlapping record in value: {handler.RecordTcIn} for clip {trackClip.Name}");
                }
            }

            if (recordIn > trackEnd && track.Children.Count > 0)
            {
                Gap gap = new(Fields(("source_range", new TimeRange(new RationalTime(0, edlRate), recordIn - trackEnd))));
                track.Insert(track.Children.Count, gap);
                Extend(track, gap.Duration);
            }

            if (trackTransition is not null)
            {
                if (track.Children.Count < 1)
                {
                    throw new EdlParseException("Transitions can't be at the very beginning of a track");
                }

                track.Insert(track.Children.Count, trackTransition);
            }

            track.Insert(track.Children.Count, trackClip);
            Extend(track, trackClip.Duration);
        }
    }

    private static void Extend(Track track, RationalTime duration) =>
        track.SourceRange = track.SourceRange!.Value.DurationExtendedBy(duration);

    private static Metadata CmxMetadata(Clip clip)
    {
        if (clip.Metadata.TryGetValue("cmx_3600", out object? existing) && existing is Metadata cmx)
        {
            return cmx;
        }

        Metadata created = new();
        clip.Metadata.Set("cmx_3600", created);
        return created;
    }

    private Track TrackFor(string name)
    {
        if (!_tracks.TryGetValue(name, out Track? track))
        {
            string kind = name.StartsWith('A') ? TrackKind.Audio : TrackKind.Video;
            track = new Track(Fields(("name", name), ("kind", kind)));
            _tracks[name] = track;
            _timeline.Tracks.Insert(_timeline.Tracks.Children.Count, track);
        }

        return track;
    }

    // Python's float(): what the reference does to the numbers it reads from comments.
    private static double PythonFloat(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new EdlParseException($"could not convert string to float: '{text}'");

    [GeneratedRegex(@"^\d+")]
    private static partial Regex LeadingDigits();

    [GeneratedRegex(@"(?<name>.*?)\s*(?<speed>-?[0-9\.]*)\s*(?<tc>[0-9:]{11})$")]
    private static partial Regex SpeedEffect();

    [GeneratedRegex(@"^(\d\d:\d\d:\d\d:\d\d)\s+(\w*)(\s+|$)(.*)")]
    private static partial Regex Locator();

    [GeneratedRegex(@"^\(([-+]?[\d.]+),? ([-+]?[\d.]+),? ([-+]?[\d.]+),?\)\s*\(([-+]?[\d.]+),? ([-+]?[\d.]+),? ([-+]?[\d.]+),?\)\s*\(([-+]?[\d.]+),? ([-+]?[\d.]+),? ([-+]?[\d.]+),?\)")]
    private static partial Regex AscSop();

    [GeneratedRegex(@".*\.(?<range>\[(?<start>[0-9]+)-(?<end>[0-9]+)\])\.\w+$")]
    private static partial Regex ImageSequencePattern();

    /// <summary>The reference's CommentHandler: known comments by kind, the rest kept as text.</summary>
    private sealed class Comments
    {
        public Comments(IEnumerable<string> lines)
        {
            foreach (string comment in lines)
            {
                bool matched = false;
                foreach ((Regex pattern, string kind) in CommentKinds)
                {
                    Match m = pattern.Match(comment);
                    if (!m.Success)
                    {
                        continue;
                    }

                    string body = m.Groups["comment_body"].Value.Trim();
                    if (kind == "locators")
                    {
                        if (!Handled.TryGetValue(kind, out object? list))
                        {
                            Handled[kind] = list = new List<string>();
                        }

                        ((List<string>)list!).Add(body);
                    }
                    else
                    {
                        Handled[kind] = body;
                    }

                    matched = true;
                    break;
                }

                if (!matched)
                {
                    string stripped = comment.TrimStart('*').Trim();
                    if (stripped.Length > 0)
                    {
                        Unhandled.Add(stripped);
                    }
                }
            }
        }

        public Dictionary<string, object?> Handled { get; } = new(StringComparer.Ordinal);

        public List<string> Unhandled { get; } = [];
    }

    /// <summary>The reference's ClipHandler: one event line (and its transition line) as a clip.</summary>
    private sealed class ClipHandler
    {
        private string? _clipNum;
        private string? _transitionId;
        private string? _transitionData;
        private string? _transitionType;
        private string _sourceTcIn = string.Empty;
        private string _sourceTcOut = string.Empty;

        public ClipHandler(string line, Dictionary<string, object?> comments, double rate, string? transitionLine)
        {
            EdlRate = rate;
            ParseLine(line);
            if (transitionLine is not null)
            {
                ParseLine(transitionLine);
            }

            Clip = MakeClip(comments);
            if (transitionLine is not null)
            {
                Transition = MakeTransition(comments);
            }
        }

        public string? Reel { get; private set; }

        public string ChannelCode { get; private set; } = string.Empty;

        public double EdlRate { get; }

        public string RecordTcIn { get; private set; } = string.Empty;

        public string RecordTcOut { get; private set; } = string.Empty;

        public Clip Clip { get; }

        public Transition? Transition { get; }

        private static string BaseName(string path)
        {
            int slash = path.LastIndexOfAny(['/', '\\']);
            return slash < 0 ? path : path[(slash + 1)..];
        }

        // Python's os.path.splitext on a base name: a leading dot is not an extension.
        private static string WithoutExtension(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot > 0 && name[..dot].Trim('.').Length > 0 ? name[..dot] : name;
        }

        private void ParseLine(string line)
        {
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 9)
            {
                (_transitionId, Reel, ChannelCode, _transitionType, _transitionData) = (fields[0], fields[1], fields[2], fields[3], fields[4]);
                (_sourceTcIn, _sourceTcOut, RecordTcIn, RecordTcOut) = (fields[5], fields[6], fields[7], fields[8]);
            }
            else if (fields.Length == 8)
            {
                (_clipNum, Reel, ChannelCode) = (fields[0], fields[1], fields[2]);
                (_sourceTcIn, _sourceTcOut, RecordTcIn, RecordTcOut) = (fields[4], fields[5], fields[6], fields[7]);
                if (fields[3] != "C")
                {
                    throw new EdlParseException($"incorrect edit type {fields[3]} in form statement: {line}");
                }
            }
            else
            {
                throw new EdlParseException($"incorrect number of fields [{fields.Length}] in form statement: {line}");
            }

            // Frame numbers, not only timecode, are accepted.
            _sourceTcIn = AsTimecode(_sourceTcIn);
            _sourceTcOut = AsTimecode(_sourceTcOut);
            RecordTcIn = AsTimecode(RecordTcIn);
            RecordTcOut = AsTimecode(RecordTcOut);
        }

        private string AsTimecode(string value) =>
            value.Contains(':', StringComparison.Ordinal)
                ? value
                : new RationalTime(int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), EdlRate).ToTimecode(EdlRate);

        private Clip MakeClip(Dictionary<string, object?> comments)
        {
            Clip clip = new(new Metadata()) { Name = _clipNum ?? "None" };
            MediaReference reference;
            if (Reel is "BL" or "BLACK")
            {
                reference = new GeneratorReference(Fields(("generator_kind", "black")));
            }
            else if (Reel == "BARS")
            {
                reference = new GeneratorReference(Fields(("generator_kind", "SMPTEBars")));
            }
            else if (comments.TryGetValue("media_reference", out object? url) && url is string target)
            {
                reference = ImageSequencePattern().IsMatch(target) ? ImageSequence(target) : new ExternalReference(Fields(("target_url", target)));
            }
            else
            {
                reference = new MissingReference(new Metadata());
            }

            reference.Owner = clip;
            clip.MediaReferences.Set(Clip.DefaultMedia, reference);

            if (comments.TryGetValue("clip_name", out object? name))
            {
                clip.Name = (string)name!;
            }
            else if (reference is ExternalReference external)
            {
                clip.Name = WithoutExtension(BaseName(external.TargetUrl));
            }

            string? sop = comments.GetValueOrDefault("asc_sop") as string;
            string? sat = comments.GetValueOrDefault("asc_sat") as string;
            if (!string.IsNullOrEmpty(sop) || !string.IsNullOrEmpty(sat))
            {
                List<object?> slope = [1L, 1L, 1L];
                List<object?> offset = [0L, 0L, 0L];
                List<object?> power = [1L, 1L, 1L];
                double saturation = 1.0;
                if (!string.IsNullOrEmpty(sop))
                {
                    Match m = AscSop().Match(sop);
                    if (!m.Success)
                    {
                        throw new EdlParseException($"Invalid ASC_SOP found: {sop}");
                    }

                    double[] f = [.. Enumerable.Range(1, 9).Select(i => PythonFloat(m.Groups[i].Value))];
                    slope = [f[0], f[1], f[2]];
                    offset = [f[3], f[4], f[5]];
                    power = [f[6], f[7], f[8]];
                }

                if (!string.IsNullOrEmpty(sat))
                {
                    saturation = PythonFloat(sat);
                }

                clip.Metadata.Set("cdl", Fields(("asc_sat", saturation), ("asc_sop", Fields(("slope", slope), ("offset", offset), ("power", power)))));
            }

            if (comments.TryGetValue("dest_clip_name", out object? destination))
            {
                clip.Name = (string)destination!;
            }

            if (comments.TryGetValue("locators", out object? locators))
            {
                foreach (string locator in (List<string>)locators!)
                {
                    Match m = Locator().Match(locator);
                    if (!m.Success)
                    {
                        continue;
                    }

                    string color = m.Groups[2].Value;
                    Marker marker = new(Fields(
                        ("name", m.Groups[4].Value),
                        ("color", MarkerColors.Contains(color.ToUpperInvariant(), StringComparer.Ordinal) ? color.ToUpperInvariant() : MarkerColor.Red),
                        ("marked_range", new TimeRange(RationalTime.FromTimecode(m.Groups[1].Value, EdlRate), new RationalTime())),
                        ("metadata", Fields(("cmx_3600", Fields(("color", color)))))));
                    clip.AddMarker(marker);
                }
            }

            clip.SourceRange = TimeRange.FromStartEndTime(RationalTime.FromTimecode(_sourceTcIn, EdlRate), RationalTime.FromTimecode(_sourceTcOut, EdlRate));

            if (!comments.ContainsKey("clip_name") && !comments.ContainsKey("dest_clip_name") && reference is ImageSequenceReference sequence)
            {
                (int first, int last) = sequence.FrameRangeForTimeRange(clip.TrimmedRange());
                clip.Name = WithoutExtension(BaseName(sequence.AbstractTargetUrl($"[{first}-{last}]")));
            }

            return clip;
        }

        private ImageSequenceReference ImageSequence(string target)
        {
            Match m = ImageSequencePattern().Match(target);
            int slash = target.LastIndexOfAny(['/', '\\']);
            string path = slash < 0 ? string.Empty : target[..slash];
            string name = slash < 0 ? target : target[(slash + 1)..];
            string range = m.Groups["range"].Value;
            int split = name.IndexOf(range, StringComparison.Ordinal);
            string start = m.Groups["start"].Value;
            return new ImageSequenceReference(Fields(
                ("target_url_base", path),
                ("name_prefix", name[..split]),
                ("name_suffix", name[(split + range.Length)..]),
                ("rate", EdlRate),
                ("start_frame", long.Parse(start, CultureInfo.InvariantCulture)),
                ("frame_zero_padding", (long)start.Length),
                ("available_range", TimeRange.FromStartEndTime(RationalTime.FromTimecode(_sourceTcIn, EdlRate), RationalTime.FromTimecode(_sourceTcOut, EdlRate)))));
        }

        private Transition MakeTransition(Dictionary<string, object?> comments)
        {
            if (_transitionId != _clipNum)
            {
                throw new EdlParseException($"transition and event id mismatch: {_transitionId} vs {_clipNum}");
            }

            string type;
            if (Regex.IsMatch(_transitionType ?? string.Empty, @"^W(\d{3})", RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                type = "SMPTE_Wipe";
            }
            else if (_transitionType == "D")
            {
                type = Model.Transition.SmpteDissolve;
            }
            else
            {
                throw new EdlParseException($"Transition type '{_transitionType}' not supported by the CMX EDL reader currently.");
            }

            RationalTime duration = new(PythonFloat(_transitionData!), Clip.SourceRange!.Value.Duration.Rate);
            string name = $"{type} to {Clip.Name}";
            if (comments.TryGetValue("dest_clip_name", out object? to) && comments.TryGetValue("clip_name", out object? from))
            {
                name = $"{type} from {from} to {to}";
            }

            return new Transition(Fields(
                ("name", name),
                ("transition_type", type),
                ("metadata", Fields(("cmx_3600", Fields(("transition", _transitionType), ("transition_duration", duration.Value))))),
                ("in_offset", new RationalTime(0, duration.Rate)),
                ("out_offset", duration)));
        }
    }
}
