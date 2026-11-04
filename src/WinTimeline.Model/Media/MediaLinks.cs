using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Media;

/// <summary>A clip whose media is not where its reference says.</summary>
/// <param name="Clip">The clip.</param>
/// <param name="Reference">Its media reference.</param>
/// <param name="Expected">The local path the reference names, or null when it names no file (a web URL).</param>
public sealed record MissingMedia(Clip Clip, MediaReference Reference, string? Expected);

/// <summary>
/// Finding a timeline's media on this machine (WT18). A reference's URL may be a file URL, a UNC
/// path, a drive path, or a path relative to the document's own folder; an image sequence is
/// found when its first frame is.
/// </summary>
public static class MediaLinks
{
    /// <summary>The local path a reference names, resolving a relative one against the document's folder; null for no file.</summary>
    /// <param name="reference">The reference.</param>
    /// <param name="documentFolder">The folder the document lives in, or null.</param>
    /// <returns>The path, or null.</returns>
    public static string? PathOf(MediaReference reference, string? documentFolder)
    {
        ArgumentNullException.ThrowIfNull(reference);
        string? url = reference switch
        {
            ExternalReference external => external.TargetUrl,
            ImageSequenceReference sequence => sequence.TargetUrlForImageNumber(0),
            _ => null,
        };

        if (url is null || FileUrl.ToPath(url) is not { Length: > 0 } path)
        {
            return null;
        }

        if (!Path.IsPathRooted(path))
        {
            if (documentFolder is null)
            {
                return null;
            }

            path = Path.Combine(documentFolder, path);
        }

        return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>The clips whose referenced file is not on disk, in timeline order. Generators and missing references are not media to find.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="documentFolder">The folder the document lives in, or null.</param>
    /// <returns>The missing media.</returns>
    public static IReadOnlyList<MissingMedia> Missing(Timeline timeline, string? documentFolder)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        List<MissingMedia> missing = [];
        foreach (Clip clip in timeline.FindClips())
        {
            if (clip.MediaReference is not (ExternalReference or ImageSequenceReference) || clip.MediaReference is not { } reference)
            {
                continue;
            }

            string? path = PathOf(reference, documentFolder);
            if (path is null || !File.Exists(path))
            {
                missing.Add(new MissingMedia(clip, reference, path));
            }
        }

        return missing;
    }
}

/// <summary>One folder put in place of another: everything under From is looked for under To.</summary>
/// <param name="From">The folder the references name.</param>
/// <param name="To">The folder the media is in now.</param>
public sealed record FolderSubstitution(string From, string To)
{
    /// <summary>
    /// The rule one choice implies: the longest common tail of the two paths is the part that did
    /// not move, and what comes before it on each side is the substitution. Picking
    /// E:\shoot\day1\A001.mov for D:\media\shoot\day1\A001.mov gives D:\media → E:\.
    /// </summary>
    /// <param name="expected">The path the reference named.</param>
    /// <param name="found">The file the person picked.</param>
    /// <returns>The rule, or null when the file names differ (nothing about the folders can be inferred).</returns>
    public static FolderSubstitution? Infer(string expected, string found)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(found);
        string[] a = Path.GetFullPath(expected).Split(Path.DirectorySeparatorChar);
        string[] b = Path.GetFullPath(found).Split(Path.DirectorySeparatorChar);
        int shared = 0;
        while (shared < a.Length && shared < b.Length && string.Equals(a[^(shared + 1)], b[^(shared + 1)], StringComparison.OrdinalIgnoreCase))
        {
            shared++;
        }

        if (shared == 0)
        {
            return null;
        }

        // The file name and any folders that came along with it are the shared tail; the rest is the rule.
        string from = string.Join(Path.DirectorySeparatorChar, a[..^shared]);
        string to = string.Join(Path.DirectorySeparatorChar, b[..^shared]);
        return new FolderSubstitution(Root(from), Root(to));
    }

    /// <summary>Where a path would be under this rule, or null when it is not under <see cref="From"/>.</summary>
    /// <param name="path">A path the references name.</param>
    /// <returns>The substituted path, or null.</returns>
    public string? Apply(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string from = From.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(from, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(To, path[from.Length..])
            : null;
    }

    // "D:" alone is the current directory on D, not its root.
    private static string Root(string path) => path.EndsWith(':') ? path + Path.DirectorySeparatorChar : path;
}

/// <summary>What one relink choice resolves: every missing clip a substitution, or a search, finds.</summary>
/// <param name="Found">Each clip found, and the file it was found at.</param>
/// <param name="StillMissing">The clips nothing found.</param>
/// <param name="Rule">The folder substitution the choice implied, or null.</param>
public sealed record RelinkPlan(IReadOnlyList<(MissingMedia Missing, string File)> Found, IReadOnlyList<MissingMedia> StillMissing, FolderSubstitution? Rule)
{
    /// <summary>
    /// Plans a relink from one choice: the clip the person located, the file they picked, and the
    /// other missing clips. The choice's folder substitution is tried on every one, then a search
    /// by file name under the picked file's folder and the rule's target, so a renamed media folder
    /// relinks completely from one pick.
    /// </summary>
    /// <param name="missing">Every missing clip.</param>
    /// <param name="located">The one the person located.</param>
    /// <param name="file">The file they picked for it.</param>
    /// <returns>The plan.</returns>
    public static RelinkPlan From(IReadOnlyList<MissingMedia> missing, MissingMedia located, string file)
    {
        ArgumentNullException.ThrowIfNull(missing);
        ArgumentNullException.ThrowIfNull(located);
        ArgumentNullException.ThrowIfNull(file);
        FolderSubstitution? rule = located.Expected is { } expected ? FolderSubstitution.Infer(expected, file) : null;
        string pickedFolder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        Lazy<ILookup<string, string>> byName = new(() => Index([pickedFolder, .. rule is null ? Array.Empty<string>() : [rule.To]]));

        List<(MissingMedia, string)> found = [(located, Path.GetFullPath(file))];
        List<MissingMedia> still = [];
        foreach (MissingMedia other in missing.Where(m => !ReferenceEquals(m, located)))
        {
            string? candidate = other.Expected is { } path && rule?.Apply(path) is { } moved && File.Exists(moved) ? moved : null;
            if (candidate is null && other.Expected is { } name)
            {
                candidate = byName.Value[Path.GetFileName(name)].FirstOrDefault();
            }

            if (candidate is null)
            {
                still.Add(other);
            }
            else
            {
                found.Add((other, candidate));
            }
        }

        return new RelinkPlan(found, still, rule);
    }

    // File names under the folders, a few levels deep, for the search by name.
    private static ILookup<string, string> Index(IEnumerable<string> folders) =>
        folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(f => Directory.EnumerateFiles(f, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true }))
            .ToLookup(Path.GetFileName, StringComparer.OrdinalIgnoreCase)!;
}
