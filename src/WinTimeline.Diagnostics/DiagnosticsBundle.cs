using System.IO.Compression;

namespace WinTimeline.Diagnostics;

/// <summary>
/// The zip a person attaches to an issue (WT6): the logs, the settings, what each part of the app
/// says about itself (the GPU and driver now, a failing file's probe once media lands), the
/// document only when the person agreed, and the newest crash dump.
/// </summary>
public sealed class DiagnosticsBundle
{
    private readonly List<(string Name, string Text)> _sections = [];
    private readonly List<(string Name, string Path)> _files = [];

    /// <summary>Adds a text entry, e.g. "gpu.json".</summary>
    /// <param name="name">Its name in the zip.</param>
    /// <param name="text">Its content.</param>
    /// <returns>This bundle.</returns>
    public DiagnosticsBundle Section(string name, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(text);
        _sections.Add((name, text));
        return this;
    }

    /// <summary>Adds a file when it exists; a missing one is noted in manifest.txt rather than failing the bundle.</summary>
    /// <param name="name">Its name in the zip.</param>
    /// <param name="path">The file.</param>
    /// <returns>This bundle.</returns>
    public DiagnosticsBundle File(string name, string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (path is not null)
        {
            _files.Add((name, path));
        }

        return this;
    }

    /// <summary>Adds every file of a log under logs/.</summary>
    /// <param name="log">The log.</param>
    /// <returns>This bundle.</returns>
    public DiagnosticsBundle Logs(Log log)
    {
        ArgumentNullException.ThrowIfNull(log);
        foreach (string file in log.Files)
        {
            _files.Add(("logs/" + Path.GetFileName(file), file));
        }

        return this;
    }

    /// <summary>Adds the newest .dmp in a folder as crash.dmp, when there is one.</summary>
    /// <param name="folder">Where crash dumps are written.</param>
    /// <returns>This bundle.</returns>
    public DiagnosticsBundle NewestDump(string folder)
    {
        if (Directory.Exists(folder))
        {
            string? newest = new DirectoryInfo(folder).GetFiles("*.dmp").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
            File("crash.dmp", newest);
        }

        return this;
    }

    /// <summary>Writes the zip, replacing any file at the path.</summary>
    /// <param name="zipPath">Where.</param>
    /// <returns>The names written, in order, manifest.txt last.</returns>
    public IReadOnlyList<string> Save(string zipPath)
    {
        ArgumentNullException.ThrowIfNull(zipPath);
        string temporary = zipPath + ".tmp";
        List<string> written = [];
        List<string> manifest = [$"WinTimeline diagnostics, {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"];
        using (ZipArchive zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            foreach ((string name, string text) in _sections)
            {
                using StreamWriter writer = new(zip.CreateEntry(name, CompressionLevel.Optimal).Open());
                writer.Write(text);
                written.Add(name);
                manifest.Add($"{name}: written");
            }

            foreach ((string name, string path) in _files)
            {
                if (!System.IO.File.Exists(path))
                {
                    manifest.Add($"{name}: not present ({path})");
                    continue;
                }

                // Shared read: the log being written by this very app is one of them.
                using FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using Stream target = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                source.CopyTo(target);
                written.Add(name);
                manifest.Add($"{name}: from {path}");
            }

            using StreamWriter list = new(zip.CreateEntry("manifest.txt").Open());
            list.Write(string.Join(Environment.NewLine, manifest));
            written.Add("manifest.txt");
        }

        System.IO.File.Move(temporary, zipPath, overwrite: true);
        return written;
    }
}
