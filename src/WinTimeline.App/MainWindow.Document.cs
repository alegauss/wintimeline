using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WinTimeline.Diagnostics;
using WinTimeline.Model;
using WinTimeline.Model.Adapters;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.App;

/// <summary>
/// WT13: the open document — File > Open, Save and Save As, and <c>--open</c> at start. The
/// title names the file; the status bar says what was opened or saved, or why it was not.
/// </summary>
public partial class MainWindow
{
    // An .otiod is a folder: it is opened by picking the content.otio inside it.
    private const string OpenFilter = "Timelines (*.otio;*.otioz;*.edl)|*.otio;*.otioz;*.edl|OpenTimelineIO (*.otio;*.otioz)|*.otio;*.otioz|CMX 3600 EDL (*.edl)|*.edl|All files (*.*)|*.*";
    private const string SaveFilter = "OpenTimelineIO (*.otio)|*.otio|OpenTimelineIO bundle with media (*.otioz)|*.otioz";

    // WT14: the bundle the document came from, which knows where its media is.
    private OtioBundle? _bundle;

    /// <summary>Where extracted bundle media is kept, a folder per bundle.</summary>
    public static string BundleCache => Path.Combine(SettingsStore.DataFolder, "bundles");

    /// <summary>
    /// The bundle a path names, or null: an .otioz or .otiod itself, or the content.otio inside an
    /// .otiod, which is what a file dialog can pick.
    /// </summary>
    /// <param name="path">A picked path.</param>
    /// <returns>The bundle's path, or null.</returns>
    public static string? BundlePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (OtioBundle.IsBundle(path))
        {
            return path;
        }

        string? folder = Path.GetDirectoryName(path);
        return string.Equals(Path.GetFileName(path), "content.otio", StringComparison.OrdinalIgnoreCase) && folder is not null && OtioBundle.IsBundle(folder)
            ? folder
            : null;
    }

    /// <summary>The open document, or null.</summary>
    public ModelObject? Document { get; private set; }

    /// <summary>WT16: the open document's undo history; every edit goes through it.</summary>
    public History History { get; private set; } = new();

    /// <summary>Where the open document lives, or null when none is open.</summary>
    public string? DocumentPath { get; private set; }

    /// <summary>What the status bar says about an opened document, e.g. "Opened cut.otio: 4 clips on 1 track."</summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="document">What it held.</param>
    /// <returns>The sentence.</returns>
    public static string OpenedText(string fileName, ModelObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document is not Timeline timeline)
        {
            return $"Opened {fileName}: a {document.Schema}, not a single timeline.";
        }

        int clips = timeline.FindClips().Count();
        int tracks = timeline.Tracks.Children.Count;
        return $"Opened {fileName}: {clips} {(clips == 1 ? "clip" : "clips")} on {tracks} {(tracks == 1 ? "track" : "tracks")}.";
    }

    /// <summary>Opens a file as the document, saying in the status bar how it went.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Whether it opened.</returns>
    public bool OpenDocument(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path = BundlePath(path) ?? path;
        string name = Path.GetFileName(path);
        try
        {
            OtioBundle? bundle = OtioBundle.IsBundle(path) ? OtioBundle.Open(path, BundleCache) : null;

            // WT15: anything else through the adapter its extension names; .otio when none does.
            ITimelineAdapter adapter = TimelineAdapters.ForPath(path) ?? TimelineAdapters.All[0];
            ModelObject document = bundle?.Content ?? adapter.Read(path, new AdapterOptions());
            _bundle = bundle;
            Document = document;
            History = new History();

            // An imported file (an EDL) is not written back over: Save asks where the .otio goes.
            DocumentPath = bundle is not null || adapter.CanWrite ? Path.GetFullPath(path) : null;
            StartJournal(name);
            Title = $"{name} — WinTimeline";
            StatusText.Text = OpenedText(name, document) + MissingText(document)
                + (document is Timeline opened ? ProblemsWindow.CountText(Model.Validation.Validator.Check(opened).Count) : string.Empty);
            Log.Write(LogLevel.Info, LogCategory.App, "Document opened.", Fields(path, document));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or EdlParseException)
        {
            string reason = ex is FileNotFoundException or DirectoryNotFoundException ? "it was not found" : ex.Message;
            StatusText.Text = $"{name} could not be opened: {reason}";
            Log.Write(LogLevel.Warning, LogCategory.App, "Document could not be opened.", new Dictionary<string, string>(StringComparer.Ordinal) { ["file"] = path }, ex);
            return false;
        }
    }

    /// <summary>Saves the document to a path, saying in the status bar how it went.</summary>
    /// <param name="path">Where.</param>
    /// <returns>Whether it saved.</returns>
    public bool SaveDocument(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Document is null)
        {
            return false;
        }

        string name = Path.GetFileName(path);
        try
        {
            if (OtioBundle.IsBundle(path))
            {
                SaveBundle(path);
            }
            else
            {
                OtioFile.Save(Document, path);
            }

            DocumentPath = Path.GetFullPath(path);
            _journal?.MarkSaved();
            Title = $"{name} — WinTimeline";
            StatusText.Text = $"Saved {name}.";
            Log.Write(LogLevel.Info, LogCategory.App, "Document saved.", Fields(path, Document));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BundleException)
        {
            StatusText.Text = $"{name} could not be saved: {ex.Message}";
            Log.Write(LogLevel.Error, LogCategory.App, "Document could not be saved.", new Dictionary<string, string>(StringComparer.Ordinal) { ["file"] = path }, ex);
            return false;
        }
    }

    /// <summary>
    /// Writes the document as a bundle with its media: media not on disk becomes a missing
    /// reference that says why, rather than failing the save. An .otioz that exists is replaced
    /// in one step, keeping the previous as .bak, as a .otio save does.
    /// </summary>
    private void SaveBundle(string path)
    {
        if (!File.Exists(path))
        {
            OtioBundle.Write(Document!, path, MediaReferencePolicy.MissingIfNotFile, _bundle is { } b ? b.MediaPath : null);
            return;
        }

        string fresh = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileNameWithoutExtension(path) + ".new" + Path.GetExtension(path));
        File.Delete(fresh);
        OtioBundle.Write(Document!, fresh, MediaReferencePolicy.MissingIfNotFile, _bundle is { } open ? open.MediaPath : null);
        File.Replace(fresh, path, path + ".bak", ignoreMetadataErrors: true);
    }

    /// <summary>WT18: what the status bar adds when media is missing, e.g. " Media is missing for 2 clips: File > Relink media."</summary>
    /// <param name="document">The opened document.</param>
    /// <returns>The sentence, or empty.</returns>
    private string MissingText(ModelObject document)
    {
        string? folder = DocumentPath is { } p ? Path.GetDirectoryName(p) : null;
        int missing = document is Timeline t && _bundle is null ? Model.Media.MediaLinks.Missing(t, folder).Count : 0;
        return missing == 0 ? string.Empty : $" Media is missing for {missing} {(missing == 1 ? "clip" : "clips")}: File > Relink media.";
    }

    private void OnRelink(object sender, RoutedEventArgs e)
    {
        if (Document is not Timeline timeline)
        {
            StatusText.Text = "Open a timeline to relink its media.";
            return;
        }

        new RelinkWindow(timeline, History, DocumentPath is { } p ? Path.GetDirectoryName(p) : null) { Owner = this }.ShowDialog();
    }

    private void OnProblems(object sender, RoutedEventArgs e)
    {
        if (Document is not Timeline timeline)
        {
            StatusText.Text = "Open a timeline to check it for problems.";
            return;
        }

        new ProblemsWindow(timeline, History) { Owner = this }.ShowDialog();
    }

    private void BindDocumentCommands()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => AskToOpen()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => History.Undo(), (_, e) => e.CanExecute = History.CanUndo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => History.Redo(), (_, e) => e.CanExecute = History.CanRedo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save(), (_, e) => e.CanExecute = Document is not null));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => AskToSaveAs(), (_, e) => e.CanExecute = Document is not null));
        if (Options.Open is { } path)
        {
            OpenDocument(path);
        }

        if (Options.RunCommands is { } commands)
        {
            RunCommands(commands);
        }

        OfferRecovery();
    }

    private void AskToOpen()
    {
        OpenFileDialog dialog = new() { Title = "Open timeline", Filter = OpenFilter };
        if (dialog.ShowDialog(this) == true)
        {
            OpenDocument(dialog.FileName);
        }
    }

    private void Save()
    {
        if (DocumentPath is { } path)
        {
            SaveDocument(path);
        }
        else
        {
            AskToSaveAs();
        }
    }

    private void AskToSaveAs()
    {
        SaveFileDialog dialog = new()
        {
            Title = "Save timeline as",
            Filter = SaveFilter,
            DefaultExt = ".otio",
            FileName = DocumentPath is { } path ? Path.GetFileName(path) : "timeline.otio",
        };
        if (dialog.ShowDialog(this) == true)
        {
            SaveDocument(dialog.FileName);
        }
    }

    private static Dictionary<string, string> Fields(string path, ModelObject document) => new(StringComparer.Ordinal)
    {
        ["file"] = Path.GetFileName(path),
        ["schema"] = document.Schema,
        ["clips"] = document is Timeline t ? t.FindClips().Count().ToString(System.Globalization.CultureInfo.InvariantCulture) : "0",
    };
}
