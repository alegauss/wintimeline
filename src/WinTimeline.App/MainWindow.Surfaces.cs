using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Vortice.Mathematics;
using WinTimeline.Render;

namespace WinTimeline.App;

/// <summary>
/// WT4: the two GPU surfaces in the dock area, the viewer above and the timeline below, split by
/// a draggable bar. Both draw on the app's one device; until the viewer and the timeline blocks
/// arrive, each clears to its own ground.
/// </summary>
public partial class MainWindow
{
    /// <summary>Where the video is shown.</summary>
    public GpuSurface Viewer { get; private set; } = null!;

    /// <summary>Where the timeline is drawn.</summary>
    public GpuSurface Timeline { get; private set; } = null!;

    private void BuildSurfaces()
    {
        Viewer = Surface("ViewerSurface", "Viewer", new Color4(0.02f, 0.02f, 0.02f, 1f));
        Timeline = Surface("TimelineSurface", "Timeline", new Color4(0.11f, 0.11f, 0.13f, 1f));

        DockArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 120 });
        DockArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        DockArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 80 });

        GridSplitter splitter = new()
        {
            Height = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        };
        AutomationProperties.SetAutomationId(splitter, "SurfaceSplitter");

        Grid.SetRow(Viewer, 0);
        Grid.SetRow(splitter, 1);
        Grid.SetRow(Timeline, 2);
        DockArea.Children.Add(Viewer);
        DockArea.Children.Add(splitter);
        DockArea.Children.Add(Timeline);

        // WT47: the timeline surface draws the active timeline.
        TimelinePanel = new TimelinePanel(Timeline, Gpu);
        Closed += (_, _) => TimelinePanel.Dispose();
    }

    /// <summary>What draws the active timeline on <see cref="Timeline"/>.</summary>
    public TimelinePanel TimelinePanel { get; private set; } = null!;

    private GpuSurface Surface(string id, string name, Color4 ground)
    {
        GpuSurface surface = new(Gpu) { Background = ground };
        AutomationProperties.SetAutomationId(surface, id);
        AutomationProperties.SetName(surface, name);
        return surface;
    }
}
