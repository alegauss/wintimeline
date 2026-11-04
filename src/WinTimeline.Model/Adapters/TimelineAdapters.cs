using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Adapters;

/// <summary>Options an adapter may read; each takes what applies to it.</summary>
/// <param name="Rate">The frame rate for formats that do not state one (EDL); 24 unless given.</param>
/// <param name="IgnoreTimecodeMismatch">For EDLs: place clips by source timecode where record timecode disagrees.</param>
/// <param name="CacheFolder">For bundles: where extracted media goes.</param>
public sealed record AdapterOptions(double Rate = 24, bool IgnoreTimecodeMismatch = false, string? CacheFolder = null);

/// <summary>
/// One file format the app reads or writes (WT15). Every format goes through this one interface,
/// so File > Open and Save As list what the registry has without knowing any format.
/// </summary>
public interface ITimelineAdapter
{
    /// <summary>What the format is called in a file dialog, e.g. "CMX 3600 EDL".</summary>
    string Name { get; }

    /// <summary>The file extensions it takes, lower case with the dot, e.g. ".edl".</summary>
    IReadOnlyList<string> Suffixes { get; }

    /// <summary>Whether it reads.</summary>
    bool CanRead { get; }

    /// <summary>Whether it writes.</summary>
    bool CanWrite { get; }

    /// <summary>Reads a file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="options">Options that apply to the format.</param>
    /// <returns>Its top object.</returns>
    ModelObject Read(string path, AdapterOptions options);

    /// <summary>Writes a file.</summary>
    /// <param name="root">What to write.</param>
    /// <param name="path">Where.</param>
    /// <param name="options">Options that apply to the format.</param>
    void Write(ModelObject root, string path, AdapterOptions options);
}

/// <summary>The formats this build knows, and which one a path is.</summary>
public static class TimelineAdapters
{
    /// <summary>Every adapter, the native .otio first.</summary>
    public static IReadOnlyList<ITimelineAdapter> All { get; } = [new OtioJsonAdapter(), new CmxEdlAdapter()];

    /// <summary>The adapter for a path, by its extension, or null when no adapter takes it.</summary>
    /// <param name="path">A file path.</param>
    /// <returns>The adapter, or null.</returns>
    public static ITimelineAdapter? ForPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string suffix = Path.GetExtension(path).ToLowerInvariant();
        return All.FirstOrDefault(a => a.Suffixes.Contains(suffix, StringComparer.Ordinal));
    }

    /// <summary>A file dialog filter of every adapter that can read, or write, e.g. "CMX 3600 EDL (*.edl)|*.edl".</summary>
    /// <param name="writing">Writers rather than readers.</param>
    /// <returns>The filter, one entry per adapter.</returns>
    public static string DialogFilter(bool writing) => string.Join('|', All
        .Where(a => writing ? a.CanWrite : a.CanRead)
        .Select(a => $"{a.Name} ({string.Join(';', a.Suffixes.Select(s => "*" + s))})|{string.Join(';', a.Suffixes.Select(s => "*" + s))}"));

    private sealed class OtioJsonAdapter : ITimelineAdapter
    {
        public string Name => "OpenTimelineIO";

        public IReadOnlyList<string> Suffixes { get; } = [".otio"];

        public bool CanRead => true;

        public bool CanWrite => true;

        public ModelObject Read(string path, AdapterOptions options) => OtioFile.Open(path);

        public void Write(ModelObject root, string path, AdapterOptions options) => OtioFile.Save(root, path);
    }

    private sealed class CmxEdlAdapter : ITimelineAdapter
    {
        public string Name => "CMX 3600 EDL";

        public IReadOnlyList<string> Suffixes { get; } = [".edl"];

        public bool CanRead => true;

        public bool CanWrite => false;

        public ModelObject Read(string path, AdapterOptions options) =>
            CmxEdlReader.Read(File.ReadAllText(path), options.Rate, options.IgnoreTimecodeMismatch);

        public void Write(ModelObject root, string path, AdapterOptions options) =>
            throw new NotSupportedException("Writing EDLs is not supported yet.");
    }
}
