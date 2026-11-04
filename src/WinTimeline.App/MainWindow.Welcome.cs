using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shell;
using WinTimeline.Diagnostics;
using WinTimeline.Media;
using WinTimeline.Model;

namespace WinTimeline.App;

/// <summary>
/// WT68: what an empty window shows: a drop zone, Open and New, and the recent documents. A
/// dropped .otio, bundle or EDL opens; dropped video and audio files become a new timeline of
/// those clips. Recent documents are kept in the settings and given to the Windows jump list.
/// </summary>
public partial class MainWindow
{
    private static readonly string[] DocumentExtensions = [".otio", ".otioz", ".otiod", ".edl"];

    /// <summary>What the status bar says after media was dropped, e.g. "Created a timeline from 2 files: 2 clips on 2 tracks."</summary>
    /// <param name="files">How many files became clips.</param>
    /// <param name="timeline">The new timeline.</param>
    /// <param name="refused">The files that were not media, with why.</param>
    /// <returns>The sentence.</returns>
    public static string CreatedText(int files, Timeline timeline, IReadOnlyList<string> refused)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(refused);
        int clips = timeline.FindClips().Count();
        string made = $"Created a timeline from {files} {(files == 1 ? "file" : "files")}: {clips} {(clips == 1 ? "clip" : "clips")} on {timeline.Tracks.Children.Count} tracks.";
        return refused.Count == 0 ? made : $"{made} Not media: {string.Join("; ", refused)}.";
    }

    /// <summary>Shows the welcome page while no document is open, and the surfaces once one is.</summary>
    private void ShowWelcome()
    {
        bool empty = Document is null;
        WelcomePanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        DockArea.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (!empty)
        {
            return;
        }

        RecentList.Items.Clear();
        IReadOnlyList<string> recent = Settings.Current.RecentFiles ?? [];
        NoRecent.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < recent.Count; i++)
        {
            string path = recent[i];
            bool there = File.Exists(path) || Directory.Exists(path);
            Button open = new()
            {
                Content = $"{Path.GetFileName(path)}    {Path.GetDirectoryName(path)}{(there ? string.Empty : "  (not found)")}",
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4),
                IsEnabled = there,
            };
            AutomationProperties.SetAutomationId(open, $"Recent{i}");
            AutomationProperties.SetName(open, Path.GetFileName(path));
            open.Click += (_, _) => OpenDocument(path);
            RecentList.Items.Add(open);
        }
    }

    /// <summary>Puts a document first among the recent ones, in the settings and the jump list.</summary>
    /// <param name="path">The document's full path.</param>
    private void Remember(string path)
    {
        Settings.Update(s => s.WithRecent(path));
        try
        {
            // Lists only for a type registered to this app (Block N); harmless before that.
            JumpList.AddToRecentCategory(path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Log.Write(LogLevel.Debug, LogCategory.App, "The jump list did not take a recent file.", null, ex);
        }
    }

    private void OnNewTimeline(object sender, RoutedEventArgs e) => ShowNew(TimelineBuilder.Empty("Untitled", 24), "Created an empty timeline: 0 clips on 2 tracks.");

    private void OnWelcomeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWelcomeDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped && dropped.Length > 0)
        {
            DropFiles(dropped);
        }
    }

    /// <summary>Opens a dropped document, or makes a timeline of dropped media.</summary>
    /// <param name="paths">What was dropped.</param>
    public void DropFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.FirstOrDefault(p => DocumentExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)) is { } document)
        {
            OpenDocument(document);
            return;
        }

        List<MediaClip> clips = [];
        List<string> refused = [];
        foreach (string path in paths.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                MediaInfo info = MediaProbe.Probe(path);
                clips.Add(new MediaClip(Path.GetFileNameWithoutExtension(path), new Uri(Path.GetFullPath(path)).AbsoluteUri, info.Duration.TotalSeconds, info.FrameRate, info.HasAudio));
            }
            catch (MediaProbeException ex)
            {
                refused.Add(ex.Message);
            }
        }

        if (clips.Count == 0)
        {
            StatusText.Text = $"Nothing dropped could be used: {string.Join("; ", refused)}.";
            Notify(new Notices.Notice(Notices.NoticeSeverity.Warning, "Nothing dropped could be used", string.Join("; ", refused), []));
            return;
        }

        double rate = clips.FirstOrDefault(c => c.Rate is not null)?.Rate ?? 24;
        Timeline timeline = TimelineBuilder.FromMedia("Untitled", rate, clips);
        ShowNew(timeline, CreatedText(clips.Count, timeline, refused));
    }

    /// <summary>Makes a new, unsaved timeline the document.</summary>
    private void ShowNew(Timeline timeline, string status)
    {
        _bundle = null;
        Document = timeline;
        DocumentPath = null;
        LoadTimelines(timeline);
        _project = Model.Project.ProjectState.Empty;
        Title = "Untitled — WinTimeline";
        ShowWelcome();
        StatusText.Text = status;
        Log.Write(LogLevel.Info, LogCategory.App, "New timeline created.", new Dictionary<string, string>(StringComparer.Ordinal) { ["clips"] = timeline.FindClips().Count().ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }
}
