using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Adapters;

/// <summary>An FCPXML file the reader cannot make sense of, where the reference adapter refuses it too.</summary>
public sealed class FcpxXmlException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FcpxXmlException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    public FcpxXmlException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    /// <param name="inner">The cause.</param>
    public FcpxXmlException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Reads Final Cut Pro X XML (FCPXML 1.8 to 1.11) into the model: a port of the reader in
/// OpenTimelineIO's otio-fcpx-xml-adapter 1.0.0 (fcpx_xml.py, read_from_string and FcpxXml), step
/// for step, quirks included, so what is read here is written by
/// <see cref="Serialization.OtioWriter"/> exactly as the reference writes what it reads. A library
/// reads as its first event, an event as a collection of its projects' timelines, a project as a
/// timeline, and a file of loose clips as a collection of them. In a sequence every video, audio,
/// asset-clip and ref-clip element becomes a clip (a ref-clip a stack, read from its compound
/// clip's own sequence), timed by the clip element around it; elements are laid onto one track per
/// lane, ordered by offset, with gaps where they do not touch. Rational times ("3003/3000s") count
/// whole frames at the format's frame rate truncated to an integer, as the reference counts them, so
/// 29.97 reads as 29. Markers are markers (completed green, to do red, others purple); an asset's
/// event clip lends its note, keywords and metadata to the reference's <c>fcpx</c> metadata.
/// Transitions, titles and effects are not read, as the reference does not read them.
/// </summary>
public static class FcpxXmlReader
{
    private const string MetaNamespace = "fcpx";

    // The reference has no constant for it; OpenTimelineIO's MarkerColor.PURPLE.
    private const string Purple = "PURPLE";

    private static readonly HashSet<string> ComposableElements = new(StringComparer.Ordinal) { "video", "audio", "ref-clip", "asset-clip" };
    private static readonly HashSet<string> TimingElements = new(StringComparer.Ordinal) { "clip", "asset-clip", "ref-clip" };

    /// <summary>Reads an FCPXML document.</summary>
    /// <param name="xml">The document's text.</param>
    /// <returns>The <see cref="Timeline"/> of a project, or a <see cref="SerializableCollection"/> of an event's timelines or of loose clips.</returns>
    /// <exception cref="FcpxXmlException">The document is not XML, holds nothing the reference reads, or has something it cannot read either.</exception>
    public static ModelObject Read(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        XElement root;
        try
        {
            XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using StringReader text = new(xml);
            using XmlReader reader = XmlReader.Create(text, settings);
            root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root ?? throw new FcpxXmlException("no element found");
        }
        catch (XmlException ex)
        {
            throw new FcpxXmlException(ex.Message, ex);
        }

        try
        {
            return new Parser(root).ToOtio();
        }
        catch (TimelineRangeException ex)
        {
            throw new FcpxXmlException(ex.Message, ex);
        }
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

    // ElementTree's tag: the local name, or {namespace}local.
    private static string Tag(XElement element) => element.Name.ToString();

    private static string? Attr(XElement element, string name) => element.Attribute(name)?.Value;

    // ElementTree's .text: the character data before the first child element, or null when there is none.
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

    private static FcpxXmlException NoneHas(string attribute) => new($"'NoneType' object has no attribute '{attribute}'");

    // Python's float() of a string.
    private static double PythonFloat(string text)
    {
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

        return trimmed.Length > 0 && (char.IsAsciiDigit(trimmed[^1]) || trimmed[^1] == '.')
            && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new FcpxXmlException($"could not convert string to float: '{text}'");
    }

    // Python's int() of a string.
    private static long PythonInt(string text) =>
        long.TryParse(text.Trim().Replace("_", string.Empty, StringComparison.Ordinal), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)
            ? value
            : throw new FcpxXmlException($"invalid literal for int() with base 10: '{text}'");

    // Python's float division.
    private static double Divide(double dividend, double divisor) =>
        divisor == 0 ? throw new FcpxXmlException("float division by zero") : dividend / divisor;

    // Python's int() of a float: truncated toward zero, refused for NaN and the infinities.
    private static double PythonIntOf(double value) =>
        double.IsNaN(value) ? throw new FcpxXmlException("cannot convert float NaN to integer")
        : double.IsInfinity(value) ? throw new FcpxXmlException("cannot convert float infinity to integer")
        : Math.Truncate(value);

    // a, b = parts: exactly two, or Python's unpacking error.
    private static (string A, string B) Unpack(string[] parts) => parts.Length switch
    {
        2 => (parts[0], parts[1]),
        < 2 => throw new FcpxXmlException($"not enough values to unpack (expected 2, got {parts.Length})"),
        _ => throw new FcpxXmlException($"too many values to unpack (expected 2, got {parts.Length})"),
    };

    // to_rational_time: whole frames at an integer rate, truncated.
    private static RationalTime ToRationalTime(string? rationalNumber, double fps)
    {
        double frames;
        if (rationalNumber is null || rationalNumber == "0s")
        {
            frames = 0;
        }
        else
        {
            string[] parts = rationalNumber.Split('/');
            frames = parts.Length > 1
                ? PythonIntOf(Divide(PythonFloat(parts[0]), PythonFloat(parts[1].Replace("s", string.Empty, StringComparison.Ordinal))) * fps)
                : PythonIntOf(PythonFloat(parts[0].Replace("s", string.Empty, StringComparison.Ordinal)) * fps);
        }

        return new RationalTime(frames, fps);
    }

    // Python's sorted(key=offset): stable, ordering by RationalTime's less-than alone.
    private sealed class OffsetOrder : IComparer<RationalTime>
    {
        public static readonly OffsetOrder Instance = new();

        public int Compare(RationalTime x, RationalTime y) => x < y ? -1 : y < x ? 1 : 0;
    }

    private sealed record LaneItem(string Track, RationalTime Offset, Item Composable, bool AudioOnly);

    /// <summary>The reference's FcpxXml: the document, searched by id as it is read.</summary>
    private sealed class Parser(XElement root)
    {
        public ModelObject ToOtio()
        {
            if (root.Element("library") is not null)
            {
                // Only the first event of a library, as the reference reads it.
                return FromEvent(root.Elements("library").Elements("event").FirstOrDefault() ?? throw NoneHas("get"));
            }

            if (root.Element("event") is { } evt)
            {
                return FromEvent(evt);
            }

            if (root.Element("project") is { } project)
            {
                return FromProject(project);
            }

            if (root.Element("asset-clip") is not null || root.Element("ref-clip") is not null)
            {
                return FromClips();
            }

            // The reference returns None, which it then cannot write.
            throw new FcpxXmlException("no library, event, project, asset-clip or ref-clip to read");
        }

        private SerializableCollection FromEvent(XElement eventElement)
        {
            List<ModelObject> projects = [.. eventElement.Elements("project").Select(FromProject)];
            return new SerializableCollection(Fields(("name", Attr(eventElement, "name") ?? string.Empty), ("children", projects)));
        }

        private Timeline FromProject(XElement projectElement)
        {
            string name = Attr(projectElement, "name") ?? string.Empty;
            Stack tracks = SequenceToStack(projectElement.Element("sequence") ?? throw NoneHas("iter"), string.Empty, null);
            return new Timeline(Fields(("name", name), ("tracks", tracks)));
        }

        private SerializableCollection FromClips()
        {
            List<ModelObject> children = [];
            foreach (XElement assetClip in root.Elements("asset-clip"))
            {
                children.Add(BuildComposable(assetClip, Attr(assetClip, "format")));
            }

            foreach (XElement refClip in root.Elements("ref-clip"))
            {
                // The reference's default format for a ref-clip is the literal "r1".
                children.Add(BuildComposable(refClip, "r1"));
            }

            return new SerializableCollection(Fields(("children", children)));
        }

        private Stack SequenceToStack(XElement sequenceElement, string name, TimeRange? sourceRange)
        {
            string? format = Attr(sequenceElement, "format");
            List<LaneItem> timelineItems = [];
            foreach (XElement element in sequenceElement.DescendantsAndSelf())
            {
                if (!ComposableElements.Contains(Tag(element)))
                {
                    continue;
                }

                Item composable = BuildComposable(element, format);
                (RationalTime offset, string lane) = OffsetAndLane(element, format);
                timelineItems.Add(new LaneItem(lane, offset, composable, AudioOnly(element)));
            }

            List<Track> tracks = [];
            foreach (string lane in timelineItems.Select(i => i.Track).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                List<LaneItem> sortedItems = [.. timelineItems.Where(i => i.Track == lane).OrderBy(i => i.Offset, OffsetOrder.Instance)];
                Track track = new(Fields(("name", lane), ("kind", sortedItems.All(i => i.AudioOnly) ? TrackKind.Audio : TrackKind.Video)));
                foreach (LaneItem item in sortedItems)
                {
                    double frameDiff = Math.Truncate(item.Offset.Value) - track.Duration.Value;
                    if (frameDiff > 0)
                    {
                        track.Insert(track.Children.Count, CreateGap(0, frameDiff, format));
                    }

                    track.Insert(track.Children.Count, item.Composable);
                }

                tracks.Add(track);
            }

            return new Stack(Fields(("name", name), ("source_range", sourceRange), ("children", tracks)));
        }

        private Item BuildComposable(XElement element, string? defaultFormat)
        {
            XElement timingClip = TimingClip(element);
            TimeRange sourceRange = TimeRangeOf(timingClip, FormatIdForClip(element, defaultFormat));

            Item composable;
            if (Tag(element) != "ref-clip")
            {
                MediaReference reference = ReferenceFromId(Attr(element, "ref"), defaultFormat);
                composable = new Clip(Fields(
                    ("name", Attr(timingClip, "name") ?? string.Empty),
                    ("media_references", Fields((Clip.DefaultMedia, reference))),
                    ("source_range", sourceRange)));
            }
            else
            {
                XElement media = CompoundClipById(Attr(element, "ref")) ?? throw NoneHas("find");
                composable = SequenceToStack(media.Element("sequence") ?? throw NoneHas("iter"), Attr(media, "name") ?? string.Empty, sourceRange);
            }

            // Every marker under the timing clip, its connected clips' included, timed in the default format.
            foreach (XElement marker in timingClip.Descendants("marker"))
            {
                composable.AddMarker(MarkerOf(marker, defaultFormat));
            }

            return composable;
        }

        private Marker MarkerOf(XElement element, string? defaultFormat)
        {
            string? completed = Attr(element, "completed");
            string color = completed switch
            {
                null or "" => Purple,
                "1" => MarkerColor.Green,
                "0" => MarkerColor.Red,
                _ => throw new FcpxXmlException("cannot access local variable 'color' where it is not associated with a value"),
            };

            return new Marker(Fields(
                ("name", Attr(element, "value") ?? string.Empty),
                ("marked_range", TimeRangeOf(element, defaultFormat)),
                ("color", color)));
        }

        private bool AudioOnly(XElement element)
        {
            switch (Tag(element))
            {
                case "audio":
                    return true;
                case "asset-clip":
                    // An element without children is false to Python, so such an asset never counts.
                    XElement? asset = AssetById(Attr(element, "ref"));
                    return asset is not null && asset.HasElements && (Attr(asset, "hasVideo") ?? "0") == "0";
                case "ref-clip":
                    return (Attr(element, "srcEnable") ?? "video") == "audio";
                default:
                    return false;
            }
        }

        private Gap CreateGap(double startFrame, double numberOfFrames, string? defaultFormat)
        {
            double fps = FormatFrameRate(defaultFormat);
            return new Gap(Fields(("source_range", new TimeRange(new RationalTime(startFrame, fps), new RationalTime(numberOfFrames, fps)))));
        }

        private static XElement TimingClip(XElement clip)
        {
            XElement current = clip;
            while (!TimingElements.Contains(Tag(current)))
            {
                current = current.Parent ?? throw NoneHas("tag");
            }

            return current;
        }

        private (RationalTime Offset, string Lane) OffsetAndLane(XElement element, string? defaultFormat)
        {
            string? clipFormatId = FormatIdForClip(element, defaultFormat);
            XElement clip = TimingClip(element);
            XElement? parent = clip.Parent ?? throw NoneHas("get");
            string? parentFormatId = FormatIdForClip(parent, defaultFormat);

            string lane;
            bool spine;
            if (Tag(parent) == "spine" && !string.IsNullOrEmpty(Attr(parent, "lane")))
            {
                lane = Attr(parent, "lane")!;
                parent = parent.Parent;
                spine = true;
            }
            else
            {
                lane = Attr(clip, "lane") ?? "0";
                spine = false;
            }

            double clipOffsetFrames = NumberOfFrames(Attr(clip, "offset"), clipFormatId);
            double parentStartFrames = spine ? 0 : NumberOfFrames(Attr(parent!, "start"), parentFormatId);
            double parentOffsetFrames = NumberOfFrames((parent ?? throw NoneHas("get")).Attribute("offset")?.Value, parentFormatId);

            double frames = clipOffsetFrames - parentStartFrames + parentOffsetFrames;
            return (new RationalTime(frames, FormatFrameRate(clipFormatId)), lane);
        }

        private string? FormatIdForClip(XElement clip, string? defaultFormat)
        {
            string? reference = Attr(clip, "ref");
            if (string.IsNullOrEmpty(reference) || Tag(clip) == "gap")
            {
                return defaultFormat;
            }

            XElement resource = AssetById(reference)
                ?? (CompoundClipById(reference) ?? throw NoneHas("find")).Element("sequence")
                ?? throw NoneHas("get");
            return Attr(resource, "format") ?? defaultFormat;
        }

        private MediaReference ReferenceFromId(string? assetId, string? defaultFormat)
        {
            XElement asset = AssetById(assetId) ?? throw NoneHas("get");
            string? src = Attr(asset, "src");
            if (string.IsNullOrEmpty(src))
            {
                return new MissingReference(new Metadata());
            }

            string? format = Attr(asset, "format") ?? defaultFormat;
            RationalTime start = ToRationalTime(Attr(asset, "start"), FormatFrameRate(format));
            RationalTime duration = ToRationalTime(Attr(asset, "duration"), FormatFrameRate(format));

            // An asset-clip without children is false to Python, so it lends nothing.
            XElement? assetClip = AssetClipByRef(assetId);
            Metadata metadata = assetClip is not null && assetClip.HasElements ? CreateMetadata(assetClip) : new Metadata();

            return new ExternalReference(Fields(
                ("target_url", src),
                ("available_range", new TimeRange(start, duration)),
                ("metadata", Fields((MetaNamespace, metadata)))));
        }

        private static Metadata CreateMetadata(XElement item)
        {
            Metadata metadata = new();
            foreach (XElement element in item.DescendantsAndSelf())
            {
                switch (Tag(element))
                {
                    case "md":
                        ListUnder(metadata, "metadata").Add(Fields((Attr(element, "key") ?? "None", Attr(element, "value"))));
                        break;
                    case "note":
                        metadata.Set("note", Text(element));
                        break;
                    case "keyword":
                        Metadata attributes = new();
                        foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
                        {
                            attributes.Set(attribute.Name.ToString(), attribute.Value);
                        }

                        ListUnder(metadata, "keywords").Add(attributes);
                        break;
                }
            }

            return metadata;
        }

        // dict.setdefault(key, []).
        private static List<object?> ListUnder(Metadata metadata, string key)
        {
            if (metadata.TryGetValue(key, out object? existing) && existing is List<object?> list)
            {
                return list;
            }

            list = [];
            metadata.Set(key, list);
            return list;
        }

        private (double Total, double Rate) FormatFrameDuration(string? formatId)
        {
            XElement format = FormatById(formatId) ?? throw NoneHas("get");
            string frameDuration = Attr(format, "frameDuration") ?? throw NoneHas("split");
            (string total, string rate) = Unpack(frameDuration.Split('/'));
            return (PythonFloat(total), PythonFloat(rate.Replace("s", string.Empty, StringComparison.Ordinal)));
        }

        // The frame rate, truncated to an integer: 1001/30000s is 29.
        private double FormatFrameRate(string? formatId)
        {
            (double total, double rate) = FormatFrameDuration(formatId);
            return PythonIntOf(Divide(rate, total));
        }

        private double NumberOfFrames(string? timeValue, string? formatId)
        {
            if (timeValue is null || timeValue == "0s")
            {
                return 0;
            }

            (double total, double rate) = FormatFrameDuration(formatId);
            string[] parts = timeValue.Split('/');
            if (parts.Length > 1)
            {
                (string a, string b) = Unpack(parts);
                return PythonIntOf(Divide(PythonFloat(a), PythonFloat(b.Replace("s", string.Empty, StringComparison.Ordinal))) * Divide(rate, total));
            }

            return PythonIntOf(PythonInt(parts[0].Replace("s", string.Empty, StringComparison.Ordinal)) * Divide(rate, total));
        }

        private TimeRange TimeRangeOf(XElement element, string? formatId)
        {
            RationalTime start = ToRationalTime(Attr(element, "start") ?? "0s", FormatFrameRate(formatId));
            RationalTime duration = ToRationalTime(Attr(element, "duration"), FormatFrameRate(formatId));
            return new TimeRange(start, duration);
        }

        // ElementTree's find("./resources/<tag>[@id='...']"); an id of None is searched as "None".
        private XElement? ResourceById(string tag, string? id)
        {
            string wanted = id ?? "None";
            return root.Elements("resources").Elements(tag).FirstOrDefault(e => Attr(e, "id") == wanted);
        }

        private XElement? AssetById(string? assetId) => ResourceById("asset", assetId);

        private XElement? FormatById(string? formatId) => ResourceById("format", formatId);

        private XElement? CompoundClipById(string? compoundId) => ResourceById("media", compoundId);

        // The first event's asset-clips, or the document's own when it has no event at its top.
        private XElement? AssetClipByRef(string? assetId)
        {
            string wanted = assetId ?? "None";
            XElement container = root.Element("event") ?? root;
            return container.Elements("asset-clip").FirstOrDefault(e => Attr(e, "ref") == wanted);
        }
    }
}
