using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinTimeline.Engine.Timeline;

namespace WinTimeline.App;

/// <summary>
/// WT49: the ruler's unit, from View > Ruler or the timeline's context menu, kept with the project
/// (WT22) so a feature cut reopens in the unit it was worked in.
/// </summary>
public partial class MainWindow
{
    private void BindRuler()
    {
        Timeline.ContextRequested += (_, _) => RulerContextMenu().IsOpen = true;
        ShowRulerUnit();
    }

    /// <summary>Sets the ruler's unit and ticks the menu items that name it.</summary>
    /// <param name="unit">The unit.</param>
    public void SetRulerUnit(RulerUnit unit)
    {
        TimelinePanel.Unit = unit;
        ShowRulerUnit();
    }

    private void OnRulerUnit(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } && Enum.TryParse(name, out RulerUnit unit))
        {
            SetRulerUnit(unit);
        }
    }

    private void ShowRulerUnit()
    {
        foreach (MenuItem item in new[] { RulerTimecode, RulerFrames, RulerFeet, RulerSeconds })
        {
            item.IsChecked = item.Tag is string name && name == TimelinePanel.Unit.ToString();
        }
    }

    private ContextMenu RulerContextMenu()
    {
        ContextMenu menu = new() { Placement = PlacementMode.MousePoint };
        AutomationProperties.SetAutomationId(menu, "TimelineContextMenu");
        foreach ((string header, RulerUnit unit) in new[]
        {
            (UiText.RulerTimecodeMenu, RulerUnit.Timecode),
            (UiText.RulerFramesMenu, RulerUnit.Frames),
            (UiText.RulerFeetMenu, RulerUnit.FeetFrames),
            (UiText.RulerSecondsMenu, RulerUnit.Seconds),
        })
        {
            MenuItem item = new() { Header = header, IsCheckable = true, IsChecked = TimelinePanel.Unit == unit };
            AutomationProperties.SetAutomationId(item, $"RulerContext{unit}");
            item.Click += (_, _) => SetRulerUnit(unit);
            menu.Items.Add(item);
        }

        return menu;
    }
}
