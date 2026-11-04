using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Adapters;

/// <summary>An FCP 7 XML file the reader cannot make sense of, where the reference adapter refuses it too.</summary>
public sealed class FcpXmlException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FcpXmlException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    public FcpXmlException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    /// <param name="inner">The cause.</param>
    public FcpXmlException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Reads Final Cut Pro 7 XML (xmeml) into the model: a port of the reader in OpenTimelineIO's own
/// otio-fcp-adapter 1.0.0 (fcp_xml.py, read_from_string and FCP7XMLParser), step for step, so what is
/// read here is written by <see cref="Serialization.OtioWriter"/> exactly as the reference writes
/// what it reads. Sequences become timelines (several in one file, a collection named
/// "Sequences"), video and audio tracks become tracks (an exploded stereo pair one track), clip
/// and generator items clips, transition items dissolves, filters effects and markers markers;
/// the XML the reader does not interpret is kept under the <c>fcp_xml</c> metadata key, element
/// by element, attributes prefixed with <c>@</c>. Values inherit down the element tree as the
/// format specifies (a clip without a rate takes its track's), and an element with an <c>id</c>
/// stands for the first element read with that id.
/// </summary>
public static class FcpXmlReader
{
    private const string MetaNamespace = "fcp_xml";

    private static readonly HashSet<string> TrackItemTags = new(StringComparer.Ordinal) { "clipitem", "generatoritem", "transitionitem" };
    private static readonly HashSet<string> SequenceParsedTags = new(StringComparer.Ordinal) { "name", "media", "marker", "duration" };
    private static readonly HashSet<string> TrackTags = new(StringComparer.Ordinal) { "track" };
    private static readonly HashSet<string> MarkerParsedTags = new(StringComparer.Ordinal) { "in", "out", "name" };
    private static readonly HashSet<string> FileParsedTags = new(StringComparer.Ordinal) { "duration", "name", "pathurl", "mediaSource" };
    private static readonly HashSet<string> GeneratorParsedTags = new(StringComparer.Ordinal) { "name", "effectid" };
    private static readonly HashSet<string> NameTag = new(StringComparer.Ordinal) { "name" };
    private static readonly HashSet<string> ItemParsedTags = new(StringComparer.Ordinal)
    {
        "name", "start", "end", "in", "out", "duration", "file", "marker", "effect", "rate", "sequence",
    };

    /// <summary>Reads an FCP 7 XML document.</summary>
    /// <param name="xml">The document's text.</param>
    /// <returns>The <see cref="Timeline"/> of its one sequence, or a <see cref="SerializableCollection"/> of the timelines of several.</returns>
    /// <exception cref="FcpXmlException">The document is not XML, has no sequence, or has one the reference cannot read either.</exception>
    public static ModelObject Read(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        XElement root;
        try
        {
            XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using StringReader text = new(xml);
            using XmlReader reader = XmlReader.Create(text, settings);
            root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root ?? throw new FcpXmlException("no element found");
        }
        catch (XmlException ex)
        {
            throw new FcpXmlException(ex.Message, ex);
        }

        List<ModelObject> sequences;
        try
        {
            sequences = [.. new Parser().TopLevelSequences(root)];
        }
        catch (TimecodeException ex)
        {
            throw new FcpXmlException(ex.Message, ex);
        }
        catch (TimelineRangeException ex)
        {
            throw new FcpXmlException(ex.Message, ex);
        }

        return sequences.Count switch
        {
            1 => sequences[0],
            > 1 => new SerializableCollection(Fields(("name", "Sequences"), ("children", sequences))),
            _ => throw new FcpXmlException("No top-level sequences found"),
        };
    }

    private static Metadata Fields(params (string Key, object? Value)[] fields)
    {
        Metadata metadata = new();
        foreach ((string key, object? value) in fields)
        {
            metadata.Set(key, value);
        }

        return metadata;
    }

    // The fcp_xml namespace around a dictionary, or nothing when it is empty.
    private static Metadata Namespaced(Metadata md) => md.Count > 0 ? Fields((MetaNamespace, md)) : new Metadata();

    // ElementTree's tag: the local name, or {namespace}local.
    private static string Tag(XElement element) => element.Name.ToString();

    // ElementTree's .text: the character data before the first child element, or null when there
    // is none. Comments are not kept by its parser, so text on either side of one runs together.
    private static string? Text(XElement element)
    {
        string? text = null;
        foreach (XNode node in element.Nodes())
        {
            if (node is XElement)
            {
                break;
            }

            if (node is XText t)
            {
                text = (text ?? string.Empty) + t.Value;
            }
        }

        return text;
    }

    private static string RequiredText(XElement? element, string what) =>
        (element is null ? null : Text(element)) ?? throw new FcpXmlException($"{what} has no text");

    private static XElement Required(XElement element, string child) =>
        element.Element(child) ?? throw new FcpXmlException($"'NoneType' object has no attribute 'text' ({child} missing under {Identification(element)})");

    // Python's int() of a string.
    private static long PythonInt(XElement element)
    {
        string text = RequiredText(element, Tag(element));
        return long.TryParse(text.Trim().Replace("_", string.Empty, StringComparison.Ordinal), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)
            ? value
            : throw new FcpXmlException($"invalid literal for int() with base 10: '{text}'");
    }

    // Python's float() of a string.
    private static double PythonFloat(XElement element)
    {
        string text = RequiredText(element, Tag(element));
        string trimmed = text.Trim().Replace("_", string.Empty, StringComparison.Ordinal);
        string lower = trimmed.ToLowerInvariant().TrimStart('+');
        bool negative = lower.StartsWith('-');
        switch (negative ? lower[1..] : lower)
        {
            case "inf" or "infinity":
                return negative ? double.NegativeInfinity : double.PositiveInfinity;
            case "nan":
                return double.NaN;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new FcpXmlException($"could not convert string to float: '{text}'");
    }

    // text.lower() == "true": no character outside ASCII lowercases to those letters.
    private static bool BoolValue(XElement element) => string.Equals(RequiredText(element, Tag(element)), "true", StringComparison.OrdinalIgnoreCase);

    private static string NameFromElement(XElement element) =>
        element.Element("name") is { } name ? Text(name) ?? string.Empty : string.Empty;

    private static string Identification(XElement element) =>
        element.Attribute("id") is { } id ? $"tag: {Tag(element)} id: {id.Value}" : $"tag: {Tag(element)}";

    // A RationalTime at a rate the context may not have: the reference fails there too.
    private static RationalTime Time(double value, double? rate) =>
        new(value, rate ?? throw new FcpXmlException("no rate is given for a time"));

    private static double? OtioRate(double timebase, bool? ntsc) => ntsc is true ? timebase * 1000.0 / 1001 : timebase;

    private static double? RateFromContext(Context context)
    {
        XElement? timebase = context.Get("rate", "timebase");
        XElement? ntsc = context.Get("rate", "ntsc");
        return timebase is null ? null : OtioRate(PythonFloat(timebase), ntsc is null ? null : BoolValue(ntsc));
    }

    private static RationalTime TimeFromTimecodeElement(XElement timecode, Context context)
    {
        Context local = context.Pushing(timecode);
        double? rate = RateFromContext(local);
        if (timecode.Element("frame") is { } frame)
        {
            return Time(PythonInt(frame), rate);
        }

        XElement tcString = timecode.Element("string") ?? throw new FcpXmlException("Timecode element missing required elements");
        return RationalTime.FromTimecode(RequiredText(tcString, "string"), rate ?? throw new FcpXmlException("no rate is given for a timecode"));
    }

    private static bool IsPrimaryAudioChannel(XElement track)
    {
        string index = track.Attribute("currentExplodedTrackIndex")?.Value ?? "0";
        string count = track.Attribute("totalExplodedTrackCount")?.Value ?? "1";
        return index == "0" || count == "1";
    }

    private static RationalTime TransitionCutPoint(XElement transition, Context context)
    {
        string? alignment = Text(Required(transition, "alignment"));
        long start = PythonInt(Required(transition, "start"));
        long end = PythonInt(Required(transition, "end"));
        double? rate = RateFromContext(context.Pushing(transition));

        // int((start + end) / 2): a true division, then truncated toward zero.
        long value = alignment switch
        {
            "end" or "end-black" => end,
            "start" or "start-black" => start,
            _ => (long)((start + end) / 2.0),
        };

        return Time(value, rate);
    }

    // _xml_tree_to_dict: attributes as "@name", children by tag, a repeated tag as a list, and
    // a timecode's frame and string left out, as they go stale.
    private static Metadata TreeToDict(XElement node, HashSet<string>? ignoreTags = null)
    {
        bool isTimecode = Tag(node) == "timecode";
        Metadata dict = new();
        foreach (XAttribute attribute in node.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                dict.Set("@" + attribute.Name.ToString(), attribute.Value);
            }
        }

        HashSet<string> encountered = new(StringComparer.Ordinal);
        HashSet<string> lists = new(StringComparer.Ordinal);
        foreach (XElement child in node.Elements())
        {
            string tag = Tag(child);
            if ((ignoreTags is not null && ignoreTags.Contains(tag)) || (isTimecode && (tag is "frame" or "string")))
            {
                continue;
            }

            object? value = child.HasElements ? TreeToDict(child) : Text(child);
            if (lists.Contains(tag))
            {
                ((List<object?>)dict[tag]!).Add(value);
            }
            else if (encountered.Contains(tag))
            {
                dict.Set(tag, new List<object?> { dict[tag], value });
                lists.Add(tag);
            }
            else
            {
                dict.Set(tag, value);
                encountered.Add(tag);
            }
        }

        return dict;
    }

    private static Marker MarkerForElement(XElement marker, double? rate)
    {
        RationalTime markerIn = Time(PythonFloat(Required(marker, "in")), rate);
        double outValue = PythonFloat(Required(marker, "out"));
        RationalTime duration = outValue > 0 ? Time(outValue, rate) - markerIn : Time(0, rate);

        return new Marker(Fields(
            ("name", NameFromElement(marker)),
            ("color", MarkerColor.Red),
            ("marked_range", new TimeRange(markerIn, duration)),
            ("metadata", Namespaced(TreeToDict(marker, MarkerParsedTags)))));
    }

    private static List<Marker> MarkersFromElement(XElement element, Context context)
    {
        double? rate = RateFromContext(context.Pushing(element));
        return [.. element.Elements("marker").Select(m => MarkerForElement(m, rate))];
    }

    private static Effect EffectFromFilterElement(XElement filter)
    {
        XElement effect = filter.Element("effect") ?? throw new FcpXmlException($"could not find effect in filter: {Identification(filter)}");
        return new Effect(Fields(
            ("name", NameFromElement(effect)),
            ("metadata", Fields((MetaNamespace, TreeToDict(effect, NameTag))))));
    }

    /// <summary>The reference's _Context: the elements above, nearest last, which values inherit from.</summary>
    private sealed class Context
    {
        private readonly XElement[] _elements;

        public Context(params XElement[] elements) => _elements = elements;

        public Context Pushing(XElement element)
        {
            if (Array.Exists(_elements, e => ReferenceEquals(e, element)))
            {
                throw new FcpXmlException($"element {Identification(element)} already in context");
            }

            return new Context([.. _elements, element]);
        }

        // element.find("./child/grandchild"), from the nearest element up.
        public XElement? Get(string child, string grandchild)
        {
            for (int i = _elements.Length - 1; i >= 0; i--)
            {
                if (_elements[i].Elements(child).Elements(grandchild).FirstOrDefault() is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }

    /// <summary>The reference's FCP7XMLParser: the id table, filled as elements are first met.</summary>
    private sealed class Parser
    {
        private readonly Dictionary<string, XElement> _ids = new(StringComparer.Ordinal);

        // Lazy, as the reference's generators are: which element first claims an id depends on it.
        public IEnumerable<ModelObject> TopLevelSequences(XElement root)
        {
            Context context = new();
            IEnumerable<XElement> top = root.Elements("sequence");
            IEnumerable<XElement> inProjectsAndBins = root.Descendants("children").Elements("sequence");
            foreach (XElement sequence in Derefed(top.Concat(inProjectsAndBins)))
            {
                yield return TimelineForSequence(sequence, context);
            }
        }

        private XElement? Derefed(XElement? element)
        {
            if (element?.Attribute("id") is not { } id)
            {
                return element;
            }

            if (_ids.TryGetValue(id.Value, out XElement? first))
            {
                return first;
            }

            _ids[id.Value] = element;
            return element;
        }

        private IEnumerable<XElement> Derefed(IEnumerable<XElement> elements)
        {
            foreach (XElement element in elements)
            {
                yield return Derefed(element)!;
            }
        }

        private Timeline TimelineForSequence(XElement sequence, Context context)
        {
            Context local = context.Pushing(sequence);
            string name = NameFromElement(sequence);
            Metadata md = TreeToDict(sequence, SequenceParsedTags);

            XElement? timecode = Derefed(sequence.Element("timecode"));
            RationalTime? start = timecode is null ? null : TimeFromTimecodeElement(timecode, local);

            Stack tracks;
            XElement? media = Derefed(sequence.Element("media"));
            if (media is null)
            {
                // timeline.tracks = None leaves the reference an empty stack.
                tracks = new Stack(new Metadata());
            }
            else
            {
                // The media types' own settings go up to the sequence.
                foreach (XElement mediaType in media.Elements())
                {
                    Metadata info = TreeToDict(mediaType, TrackTags);
                    if (info.Count > 0)
                    {
                        if (!md.TryGetValue("media", out object? existing) || existing is not Metadata mediaDict)
                        {
                            mediaDict = new Metadata();
                            md.Set("media", mediaDict);
                        }

                        mediaDict.Set(Tag(mediaType), info);
                    }
                }

                tracks = StackForElement(media, local);
                tracks.Name = name;
            }

            Timeline timeline = new(Fields(
                ("name", name),
                ("global_start_time", start),
                ("metadata", Namespaced(md)),
                ("tracks", tracks)));

            foreach (Marker marker in MarkersFromElement(sequence, context))
            {
                tracks.AddMarker(marker);
            }

            return timeline;
        }

        private Stack StackForElement(XElement element, Context context)
        {
            Context local = context.Pushing(element);
            List<Track> tracks = [];
            foreach (XElement mediaType in Derefed(element.Elements()))
            {
                string? kind = Tag(mediaType).ToLowerInvariant() switch
                {
                    "audio" => TrackKind.Audio,
                    "video" => TrackKind.Video,
                    _ => null,
                };
                if (kind is null)
                {
                    continue;
                }

                foreach (XElement track in Derefed(mediaType.Elements("track")))
                {
                    if (kind == TrackKind.Audio && !IsPrimaryAudioChannel(track))
                    {
                        continue;
                    }

                    tracks.Add(TrackForElement(track, kind, local));
                }
            }

            List<Marker> markers = MarkersFromElement(element, context);
            return new Stack(Fields(("children", tracks), ("markers", markers), ("name", NameFromElement(element))));
        }

        private Track TrackForElement(XElement trackElement, string kind, Context context)
        {
            Context local = context.Pushing(trackElement);
            string name = trackElement.Element("name") is { } nameElement ? Text(nameElement) ?? string.Empty : string.Empty;
            Metadata md = TreeToDict(trackElement, TrackItemTags);

            // 1.0.0 neither names a track by Premiere's MZ.TrackName nor reads its enabled element;
            // both stay in the metadata.
            Track track = new(Fields(("name", name), ("kind", kind), ("metadata", Namespaced(md))));

            double? trackRate = RateFromContext(local);
            RationalTime current = Time(0, trackRate);
            XElement? headTransition = null;
            List<XElement> children = [.. trackElement.Elements()];
            for (int i = 0; i < children.Count; i++)
            {
                if (!TrackItemTags.Contains(Tag(children[i])))
                {
                    continue;
                }

                XElement item = Derefed(children[i])!;

                // Look ahead for the transition out of this item.
                XElement? tailTransition = i + 1 < children.Count && Tag(children[i + 1]) == "transitionitem" ? Derefed(children[i + 1]) : null;

                (Composable trackItem, TimeRange itemRange) = ItemAndTimingForElement(item, headTransition, tailTransition, local);

                if (current < itemRange.StartTime)
                {
                    RationalTime gapDuration = (itemRange.StartTime - current).RescaledTo(trackRate!.Value);
                    track.Insert(track.Children.Count, new Gap(Fields(("source_range", new TimeRange(new RationalTime(0, gapDuration.Rate), gapDuration)))));
                }

                track.Insert(track.Children.Count, trackItem);
                current = itemRange.EndTimeExclusive;

                if (Tag(item) == "transitionitem")
                {
                    headTransition = item;
                }
            }

            return track;
        }

        private (Composable Item, TimeRange Range) ItemAndTimingForElement(XElement item, XElement? headTransition, XElement? tailTransition, Context context)
        {
            double? parentRate = RateFromContext(context);
            long startValue = PythonInt(Required(item, "start"));
            long endValue = PythonInt(Required(item, "end"));

            RationalTime start;
            RationalTime startOffset;
            if (startValue == -1)
            {
                // From the cut point of the transition in; the media's in advances by as much.
                XElement head = headTransition ?? throw new FcpXmlException($"'NoneType' object has no attribute 'find' (no transition before {Identification(item)})");
                start = TransitionCutPoint(head, context);
                double? transitionRate = RateFromContext(context.Pushing(head));
                startOffset = start - Time(PythonInt(Required(head, "start")), transitionRate);
            }
            else
            {
                start = Time(startValue, parentRate);
                startOffset = new RationalTime();
            }

            RationalTime end = endValue == -1
                ? TransitionCutPoint(tailTransition ?? throw new FcpXmlException($"'NoneType' object has no attribute 'find' (no transition after {Identification(item)})"), context)
                : Time(endValue, parentRate);

            TimeRange itemRange = new(start, end - start);
            Metadata md = TreeToDict(item, ItemParsedTags);

            Composable result = Tag(item) switch
            {
                "clipitem" or "generatoritem" => ClipForElement(item, itemRange, startOffset, context),
                "transitionitem" => TransitionForElement(item, context),
                _ => throw new FcpXmlException($"unknown-{Tag(item)}"),
            };

            if (md.Count > 0)
            {
                if (!result.Metadata.TryGetValue(MetaNamespace, out object? existing) || existing is not Metadata fcp)
                {
                    fcp = new Metadata();
                    result.Metadata.Set(MetaNamespace, fcp);
                }

                foreach ((string key, object? value) in md)
                {
                    fcp.Set(key, value);
                }
            }

            return (result, itemRange);
        }

        private Item ClipForElement(XElement clipItem, TimeRange itemRange, RationalTime startOffset, Context context)
        {
            Context local = context.Pushing(clipItem);
            string name = NameFromElement(clipItem);

            XElement? file = Derefed(clipItem.Element("file"));
            XElement? sequence = Derefed(clipItem.Element("sequence"));
            XElement? generatorEffect = Tag(clipItem) == "generatoritem"
                ? clipItem.Elements("effect").FirstOrDefault(e => e.Elements("effecttype").Any(t => string.Concat(t.DescendantNodes().OfType<XText>().Select(x => x.Value)) == "generator"))
                : null;

            RationalTime mediaStartTime = new();
            Item item;
            if (sequence is not null)
            {
                item = StackForElement(sequence, local);
            }
            else if (file is not null || generatorEffect is not null)
            {
                MediaReference reference;
                if (file is not null)
                {
                    reference = MediaReferenceForFileElement(file, local);
                    if (file.Element("timecode") is { } timecode)
                    {
                        mediaStartTime = TimeFromTimecodeElement(timecode, local);
                    }
                }
                else
                {
                    reference = MediaReferenceForEffectElement(generatorEffect!);
                }

                item = new Clip(Fields(("name", name), ("media_references", Fields((Clip.DefaultMedia, reference)))));
            }
            else
            {
                throw new FcpXmlException($"Type of clip item is not supported {Identification(clipItem)}");
            }

            foreach (Marker marker in MarkersFromElement(clipItem, context))
            {
                item.AddMarker(marker);
            }

            // The in time is relative to the media's start, and moves on by a transition's offset.
            double? clipRate = RateFromContext(local);
            RationalTime inTime = Time(PythonFloat(Required(clipItem, "in")), clipRate);
            RationalTime sourceStartTime = inTime + mediaStartTime + startOffset;
            item.SourceRange = new TimeRange(sourceStartTime.RescaledTo(clipRate!.Value), itemRange.Duration.RescaledTo(clipRate.Value));

            foreach (XElement filter in Derefed(clipItem.Elements("filter")))
            {
                item.AddEffect(EffectFromFilterElement(filter));
            }

            return item;
        }

        private static MediaReference MediaReferenceForFileElement(XElement file, Context context)
        {
            Context local = context.Pushing(file);
            double? rate = RateFromContext(local);
            string name = NameFromElement(file);
            Metadata metadata = Namespaced(TreeToDict(file, FileParsedTags));

            string? path = file.Element("pathurl") is { } pathUrl ? Text(pathUrl) : null;
            string? mediaSource = file.Element("mediaSource") is { } source ? Text(source) : null;

            XElement? timecode = file.Element("timecode");
            RationalTime startTime = timecode is not null
                ? TimeFromTimecodeElement(timecode, local).RescaledTo(rate ?? throw new FcpXmlException("no rate is given for a file"))
                : Time(0, rate);

            TimeRange? availableRange = null;
            if (file.Element("duration") is { } duration)
            {
                availableRange = new TimeRange(startTime, Time(PythonFloat(duration), rate));
            }
            else if (timecode is not null)
            {
                availableRange = new TimeRange(startTime, Time(0, rate));
            }

            if (path is not null)
            {
                return new ExternalReference(Fields(("name", name), ("target_url", path), ("available_range", availableRange), ("metadata", metadata)));
            }

            if (mediaSource is not null)
            {
                return new GeneratorReference(Fields(("name", name), ("generator_kind", mediaSource), ("available_range", availableRange), ("metadata", metadata)));
            }

            return new MissingReference(Fields(("name", name), ("available_range", availableRange), ("metadata", metadata)));
        }

        private static GeneratorReference MediaReferenceForEffectElement(XElement effect)
        {
            string kind = effect.Element("effectid") is { } effectId ? Text(effectId) ?? string.Empty : string.Empty;
            return new GeneratorReference(Fields(
                ("name", NameFromElement(effect)),
                ("generator_kind", kind),
                ("metadata", Namespaced(TreeToDict(effect, GeneratorParsedTags)))));
        }

        private static Transition TransitionForElement(XElement item, Context context)
        {
            // Start and end are in the parent's rate (1.0.0; later versions prefer the transition's own).
            double? rate = RateFromContext(context);
            RationalTime start = Time(PythonInt(Required(item, "start")), rate);
            RationalTime end = Time(PythonInt(Required(item, "end")), rate);
            RationalTime cutPoint = TransitionCutPoint(item, context);
            XElement effect = item.Element("effect") ?? throw new FcpXmlException($"'NoneType' object has no attribute 'find' (no effect in {Identification(item)})");

            return new Transition(Fields(
                ("name", NameFromElement(effect)),
                ("transition_type", Transition.SmpteDissolve),
                ("in_offset", cutPoint - start),
                ("out_offset", end - cutPoint)));
        }
    }
}
