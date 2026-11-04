using System.Text;

namespace WinTimeline.Model.Serialization;

/// <summary>
/// Opening and saving .otio files (WT13). A save is atomic: the text goes to a temporary file
/// beside the target, is flushed to disk, and replaces the target in one step, which leaves the
/// previous file as <c>.bak</c> until the next successful save. A crash mid-save therefore never
/// leaves half a timeline where the whole one was.
/// </summary>
public static class OtioFile
{
    /// <summary>Opens a .otio file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its top object.</returns>
    /// <exception cref="InvalidDataException">The file is not OpenTimelineIO JSON.</exception>
    public static ModelObject Open(string path) => OtioReader.ReadFile(path);

    /// <summary>Saves an object as a .otio file, atomically, keeping the previous file as .bak.</summary>
    /// <param name="root">What to save.</param>
    /// <param name="path">Where.</param>
    /// <param name="targetVersions">Schema versions to write older objects at, for an older reader.</param>
    public static void Save(ModelObject root, string path, IReadOnlyDictionary<string, int>? targetVersions = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + ".tmp";
        byte[] bytes = Encoding.UTF8.GetBytes(OtioWriter.Write(root, targetVersions));
        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(full))
        {
            File.Replace(temporary, full, full + ".bak", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporary, full);
        }
    }
}
