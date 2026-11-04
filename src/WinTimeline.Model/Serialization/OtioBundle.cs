using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace WinTimeline.Model.Serialization;

/// <summary>What a bundle writer does with a clip whose media is not a file on disk.</summary>
public enum MediaReferencePolicy
{
    /// <summary>Refuse the whole bundle.</summary>
    ErrorIfNotFile,

    /// <summary>Replace that clip's reference with a missing one that says why.</summary>
    MissingIfNotFile,

    /// <summary>Bundle no media at all: every reference becomes a missing one.</summary>
    AllMissing,
}

/// <summary>A bundle could not be written: a media file is not on disk, or two share a name.</summary>
public sealed class BundleException : Exception
{
    /// <summary>Creates the exception.</summary>
    public BundleException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    public BundleException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    /// <param name="inner">The cause.</param>
    public BundleException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>An opened bundle: its timeline, and where its media can be read from.</summary>
public sealed class OtioBundle
{
    /// <summary>The folder bundled media sits in, and the URL prefix references to it carry.</summary>
    public const string MediaFolder = "media";

    private const string ContentFile = "content.otio";
    private const string VersionFile = "version.txt";
    private const string BundleVersion = "1.0.0";

    private readonly string? _zip;
    private readonly string _root;

    private OtioBundle(ModelObject content, string root, string? zip, IReadOnlyList<string> media)
    {
        Content = content;
        _root = root;
        _zip = zip;
        MediaEntries = media;
    }

    /// <summary>The timeline the bundle holds.</summary>
    public ModelObject Content { get; }

    /// <summary>The media files in the bundle, as paths relative to its root, e.g. "media/a.mov".</summary>
    public IReadOnlyList<string> MediaEntries { get; }

    /// <summary>Whether the path names a bundle, by its extension.</summary>
    /// <param name="path">A path.</param>
    /// <returns>Whether it is .otioz or .otiod.</returns>
    public static bool IsBundle(string path) =>
        path.EndsWith(".otioz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".otiod", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Opens an .otioz (a zip) or .otiod (a folder) as OpenTimelineIO 0.18.1 writes them. Nothing
    /// is unpacked: an .otioz's media are extracted one file at a time, when asked for, by
    /// <see cref="MediaPath"/>.
    /// </summary>
    /// <param name="path">The bundle.</param>
    /// <param name="cacheRoot">Where .otioz media is extracted to; a folder per bundle goes under it.</param>
    /// <returns>The bundle.</returns>
    /// <exception cref="InvalidDataException">It is not a bundle, or its content is not OpenTimelineIO.</exception>
    public static OtioBundle Open(string path, string cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(cacheRoot);
        if (Directory.Exists(path))
        {
            string content = Path.Combine(path, ContentFile);
            if (!File.Exists(content))
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} has no {ContentFile}");
            }

            string mediaFolder = Path.Combine(path, MediaFolder);
            string[] media = Directory.Exists(mediaFolder)
                ? [.. Directory.GetFiles(mediaFolder).Select(f => MediaFolder + "/" + Path.GetFileName(f)).Order(StringComparer.Ordinal)]
                : [];
            return new OtioBundle(OtioReader.ReadFile(content), Path.GetFullPath(path), null, media);
        }

        try
        {
            using ZipArchive zip = ZipFile.OpenRead(path);
            ZipArchiveEntry entry = zip.GetEntry(ContentFile) ?? throw new InvalidDataException($"{Path.GetFileName(path)} has no {ContentFile}");
            using StreamReader reader = new(entry.Open(), Encoding.UTF8);
            ModelObject read = OtioReader.ReadString(reader.ReadToEnd());
            string[] media = [.. zip.Entries.Where(e => e.FullName.StartsWith(MediaFolder + "/", StringComparison.Ordinal) && e.Name.Length > 0).Select(e => e.FullName)];
            return new OtioBundle(read, Path.Combine(cacheRoot, CacheKey(path)), Path.GetFullPath(path), media);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a bundle: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// The local file a reference's URL names: a bundle-relative URL ("media/a.mov", or the
    /// reference's own Windows spelling "media\\a.mov") is resolved inside the bundle, extracting
    /// it from an .otioz on first use; an absolute file URL is converted to its path.
    /// </summary>
    /// <param name="targetUrl">An ExternalReference's target URL.</param>
    /// <returns>The file's path, or null when the URL names no file the bundle or the disk holds.</returns>
    public string? MediaPath(string targetUrl)
    {
        ArgumentNullException.ThrowIfNull(targetUrl);
        if (FileUrl.ToPath(targetUrl) is { } absolute && Path.IsPathRooted(absolute))
        {
            return absolute;
        }

        string relative = targetUrl.Replace('\\', '/');
        if (!MediaEntries.Contains(relative, StringComparer.Ordinal))
        {
            return null;
        }

        string local = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (_zip is null || File.Exists(local))
        {
            return local;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        using ZipArchive zip = ZipFile.OpenRead(_zip);
        string temporary = local + ".part";
        zip.GetEntry(relative)!.ExtractToFile(temporary, overwrite: true);
        File.Move(temporary, local, overwrite: true);
        return local;
    }

    /// <summary>
    /// Writes a bundle as OpenTimelineIO 0.18.1 does: <c>version.txt</c>, <c>content.otio</c> with
    /// every bundled reference pointing at <c>media/&lt;name&gt;</c>, and the media files (stored,
    /// not compressed, in an .otioz). An existing bundle is never overwritten.
    ///
    /// <para>One difference, on purpose: a reference the policy replaces keeps its name, metadata
    /// and ranges, where the reference's copy loses them; it gains the same two keys,
    /// <c>missing_reference_because</c> and <c>original_target_url</c>.</para>
    /// </summary>
    /// <param name="root">The timeline; it is not changed.</param>
    /// <param name="path">The .otioz file or .otiod folder to create.</param>
    /// <param name="policy">What to do with media that is not a file on disk.</param>
    /// <param name="resolve">
    /// Turns a target URL into a local file, for a timeline whose URLs are relative to somewhere
    /// else — a bundle it was opened from passes its <see cref="MediaPath"/>. Null reads file URLs as paths.
    /// </param>
    /// <exception cref="BundleException">The path exists, a media file is missing under <see cref="MediaReferencePolicy.ErrorIfNotFile"/>, or two media files share a name.</exception>
    public static void Write(ModelObject root, string path, MediaReferencePolicy policy = MediaReferencePolicy.ErrorIfNotFile, Func<string, string?>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new BundleException($"'{path}' exists, will not overwrite.");
        }

        // A deep copy, through the writer and reader that are proven lossless (WT13).
        ModelObject copy = OtioReader.ReadString(OtioWriter.Write(root));
        Dictionary<string, string> media = Prepare(copy, policy, resolve);
        string text = OtioWriter.Write(copy);

        if (path.EndsWith(".otiod", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.Combine(path, MediaFolder));
            File.WriteAllText(Path.Combine(path, VersionFile), BundleVersion);
            File.WriteAllText(Path.Combine(path, ContentFile), text);
            foreach ((string source, string name) in media)
            {
                File.Copy(source, Path.Combine(path, MediaFolder, name));
            }

            return;
        }

        string temporary = path + ".tmp";
        using (ZipArchive zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            WriteEntry(zip, VersionFile, BundleVersion);
            WriteEntry(zip, ContentFile, text);
            foreach ((string source, string name) in media)
            {
                zip.CreateEntryFromFile(source, MediaFolder + "/" + name, CompressionLevel.NoCompression);
            }
        }

        File.Move(temporary, path);
    }

    /// <summary>The bytes the bundled media would take, the reference's dry run.</summary>
    /// <param name="root">The timeline.</param>
    /// <param name="policy">The media policy.</param>
    /// <returns>Total bytes of the media files that would be copied.</returns>
    public static long MediaSize(ModelObject root, MediaReferencePolicy policy = MediaReferencePolicy.ErrorIfNotFile)
    {
        ArgumentNullException.ThrowIfNull(root);
        ModelObject copy = OtioReader.ReadString(OtioWriter.Write(root));
        return Prepare(copy, policy, null).Keys.Sum(f => new FileInfo(f).Length);
    }

    // The reference's _prepped_otio_for_bundle_and_manifest: rewrites the copy in place and
    // answers each media file with the name it gets under media/.
    private static Dictionary<string, string> Prepare(ModelObject copy, MediaReferencePolicy policy, Func<string, string?>? resolve)
    {
        Dictionary<string, List<ExternalReference>> byFile = new(StringComparer.OrdinalIgnoreCase);
        IEnumerable<Clip> clips = copy is Timeline t ? t.FindClips() : copy is Composition c ? c.FindClips() : [];
        foreach (Clip clip in clips)
        {
            if (policy == MediaReferencePolicy.AllMissing)
            {
                MarkMissing(clip, $"{policy} specified as the MediaReferencePolicy");
                continue;
            }

            if (clip.MediaReference is not ExternalReference reference)
            {
                continue;
            }

            string? file = resolve?.Invoke(reference.TargetUrl) ?? FileUrl.ToPath(reference.TargetUrl);
            if (file is null)
            {
                if (policy == MediaReferencePolicy.ErrorIfNotFile)
                {
                    throw new BundleException($"Bundles only take media on disk; got a target_url of '{reference.TargetUrl}'.");
                }

                MarkMissing(clip, "target_url is not a file scheme url (start with url:)");
                continue;
            }

            file = Path.GetFullPath(file);
            if (!File.Exists(file))
            {
                if (policy == MediaReferencePolicy.ErrorIfNotFile)
                {
                    throw new BundleException($"{file} is not a file on disk.");
                }

                MarkMissing(clip, "target_url target is not a file or does not exist");
                continue;
            }

            if (!byFile.TryGetValue(file, out List<ExternalReference>? list))
            {
                byFile[file] = list = [];
            }

            list.Add(reference);
        }

        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> taken = new(StringComparer.Ordinal);
        foreach ((string file, List<ExternalReference> references) in byFile)
        {
            string name = Path.GetFileName(file);
            if (taken.TryGetValue(name, out string? other))
            {
                throw new BundleException($"Bundled media need unique names: '{file}' and '{other}' are both '{name}'.");
            }

            taken[name] = file;
            names[file] = name;
            foreach (ExternalReference reference in references)
            {
                reference.TargetUrl = MediaFolder + "/" + name;
            }
        }

        return names;
    }

    private static void MarkMissing(Clip clip, string because)
    {
        Metadata fields = new();
        if (clip.MediaReference is { } original)
        {
            Metadata kept = new();
            foreach ((string key, object? value) in original.Metadata)
            {
                kept.Set(key, value);
            }

            kept.Set("missing_reference_because", because);
            kept.Set("original_target_url", original is ExternalReference x ? x.TargetUrl : null);
            fields.Set("metadata", kept);
            fields.Set("name", original.Name);
            fields.Set("available_range", original.AvailableRange);
            fields.Set("available_image_bounds", original.AvailableImageBounds);
        }

        MissingReference missing = new(fields) { Owner = clip };
        clip.MediaReferences.Set(clip.ActiveMediaReferenceKey, missing);
    }

    private static void WriteEntry(ZipArchive zip, string name, string text)
    {
        using StreamWriter writer = new(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    // A folder per bundle, named for its path, size and time, so reopening it finds what was
    // already extracted and a changed bundle gets a fresh folder, without hashing gigabytes.
    private static string CacheKey(string path)
    {
        FileInfo info = new(path);
        string identity = string.Create(CultureInfo.InvariantCulture, $"{info.FullName.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
    }
}

/// <summary>file: URLs to paths and back, as OpenTimelineIO's url_utils does it.</summary>
public static class FileUrl
{
    /// <summary>The path a file URL names; a bare relative path is its own path; null for another scheme.</summary>
    /// <param name="url">The URL.</param>
    /// <returns>The path, or null.</returns>
    public static string? ToPath(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : null;
        }

        // A drive letter is not a scheme: "D:\x" and "D:/x" are paths.
        bool scheme = url.IndexOf(':', StringComparison.Ordinal) is > 1 and int colon && url[..colon].All(char.IsAsciiLetterOrDigit);
        return scheme ? null : url;
    }

    /// <summary>A path as a file URL, e.g. file:///D:/footage/a.mov.</summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The URL.</returns>
    public static string FromPath(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;
}
