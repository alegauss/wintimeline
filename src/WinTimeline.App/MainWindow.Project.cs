using System.IO;
using System.Windows;
using WinTimeline.Diagnostics;
using WinTimeline.Model.Project;

namespace WinTimeline.App;

/// <summary>
/// WT22: the editor's state about the open document, kept in the sidecar beside it: read on
/// open, written on every save. The layout is what the window carries of it today; bins,
/// proxies and analysis come back the same way as the blocks that make them arrive.
/// </summary>
public partial class MainWindow
{
    private ProjectState _project = ProjectState.Empty;

    /// <summary>What the status bar adds after an open that found a sidecar, e.g. " Restored the project from cut.otio.wtproj."</summary>
    /// <param name="state">What was found.</param>
    /// <param name="documentPath">The document.</param>
    /// <returns>The sentence, or empty.</returns>
    public static string RestoredText(ProjectState state, string documentPath)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.IsEmpty ? string.Empty : UiText.RestoredProject(Path.GetFileName(ProjectSidecar.FolderFor(documentPath)));
    }

    /// <summary>Reads the sidecar beside the open document and applies its layout.</summary>
    /// <returns>What the status bar adds.</returns>
    private string RestoreProject()
    {
        _project = ProjectState.Empty;
        if (DocumentPath is not { } path)
        {
            return string.Empty;
        }

        try
        {
            _project = ProjectSidecar.Load(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Write(LogLevel.Warning, LogCategory.App, "Project sidecar could not be read.", new Dictionary<string, string>(StringComparer.Ordinal) { ["file"] = path }, ex);
            return UiText.ProjectNotRestored(ex.Message);
        }

        WorkspaceLayout layout = _project.Layout ?? WorkspaceLayout.Default;
        SetRulerUnit(Enum.TryParse(layout.RulerUnit, out Engine.Timeline.RulerUnit unit) ? unit : Engine.Timeline.RulerUnit.Timecode);
        if (layout.ActiveTimeline is { } name && Timelines.FirstOrDefault(t => t.Name == name) is { } shown)
        {
            Activate(shown);
            RebuildTabs();
        }

        if (DockArea.RowDefinitions.Count == 3)
        {
            double share = Math.Clamp(layout.ViewerShare, 0.1, 0.9);
            DockArea.RowDefinitions[0].Height = new GridLength(share, GridUnitType.Star);
            DockArea.RowDefinitions[2].Height = new GridLength(1 - share, GridUnitType.Star);
        }

        return RestoredText(_project, path);
    }

    /// <summary>Writes the sidecar beside a document just saved, with the window's layout as it is now.</summary>
    /// <param name="path">Where the document was saved.</param>
    private void KeepProject(string path)
    {
        _project = _project with { Layout = CurrentLayout() };
        try
        {
            ProjectSidecar.Save(path, _project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(LogLevel.Warning, LogCategory.App, "Project sidecar could not be written.", new Dictionary<string, string>(StringComparer.Ordinal) { ["file"] = path }, ex);
        }
    }

    private WorkspaceLayout CurrentLayout()
    {
        double share = WorkspaceLayout.Default.ViewerShare;
        // The star weights, which the splitter rewrites in proportion, rather than pixel heights
        // that a window too short for the minimums would skew away from the default.
        if (DockArea.RowDefinitions.Count == 3 && DockArea.RowDefinitions[0].Height.IsStar && DockArea.RowDefinitions[2].Height.IsStar)
        {
            double viewer = DockArea.RowDefinitions[0].Height.Value;
            double timeline = DockArea.RowDefinitions[2].Height.Value;
            if (viewer + timeline > 0)
            {
                share = Math.Round(viewer / (viewer + timeline), 2);
            }
        }

        string? active = ActiveTimeline is { } shown && Timelines.Count > 1 && !ReferenceEquals(shown, Timelines[0]) ? shown.Name : null;
        string? unit = TimelinePanel.Unit == Engine.Timeline.RulerUnit.Timecode ? null : TimelinePanel.Unit.ToString();
        return new WorkspaceLayout(share, active, unit);
    }
}
