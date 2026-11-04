using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using WinTimeline.App.Notices;
using WinTimeline.Engine.Jobs;

namespace WinTimeline.App;

/// <summary>
/// WT69: the stack of notices under the menu, and the window's one job scheduler, whose
/// failures and completions become notices. At most a few are shown; the oldest goes first.
/// </summary>
public partial class MainWindow
{
    private const int MostNotices = 4;
    private int _noticeCount;

    /// <summary>The window's background jobs; the blocks that need long work register their kinds here.</summary>
    public JobScheduler Jobs { get; private set; } = null!;

    /// <summary>The kinds of job this build can run, by name.</summary>
    private Dictionary<string, JobWork> JobKinds { get; } = new(StringComparer.Ordinal);

    /// <summary>Shows a notice.</summary>
    /// <param name="notice">What to show.</param>
    public void Notify(Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        while (NoticeArea.Children.Count >= MostNotices)
        {
            NoticeArea.Children.RemoveAt(0);
        }

        Border bar = Bar(notice);
        NoticeArea.Children.Add(bar);
        if (notice.Transient)
        {
            DispatcherTimer timer = new() { Interval = Notice.Lifetime };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                NoticeArea.Children.Remove(bar);
            };
            timer.Start();
        }
    }

    /// <summary>Opens Explorer on a file's folder with the file selected.</summary>
    /// <param name="path">The file.</param>
    private static void ShowInExplorer(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    /// <summary>Keeps the status bar's playhead timecode with the panel's playhead.</summary>
    private void ShowPlayhead() =>
        PlayheadStatus.Text = ActiveTimeline is null ? string.Empty : TimelinePanel.TimecodeAt(TimelinePanel.PlayheadSeconds);

    private void StartJobs()
    {
        TimelinePanel.PlayheadMoved += (_, _) => ShowPlayhead();
        Jobs = new JobScheduler(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["cpu"] = Math.Max(1, Environment.ProcessorCount - 2),
                ["gpu-encoder"] = 2,
                ["decode"] = 4,
                ["network"] = 2,
            },
            kind => JobKinds.GetValueOrDefault(kind));
        Jobs.Changed += (_, job) =>
        {
            if (JobNotices.For(job, Jobs) is { } notice)
            {
                Dispatcher.BeginInvoke(() => Notify(notice));
            }
        };
        Closed += async (_, _) => await Jobs.DisposeAsync().ConfigureAwait(false);
    }

    private Border Bar(Notice notice)
    {
        int number = _noticeCount++;
        (string background, string glyph) = notice.Severity switch
        {
            NoticeSeverity.Error => ("SystemFillColorCriticalBackgroundBrush", ""),
            NoticeSeverity.Warning => ("SystemFillColorCautionBackgroundBrush", ""),
            NoticeSeverity.Success => ("SystemFillColorSuccessBackgroundBrush", ""),
            _ => ("SystemFillColorAttentionBackgroundBrush", ""),
        };

        Border bar = new() { Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 0, 0, 1) };
        bar.SetResourceReference(Border.BackgroundProperty, background);
        AutomationProperties.SetAutomationId(bar, $"Notice{number}");

        StackPanel buttons = new() { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        foreach (NoticeAction action in notice.Actions)
        {
            Button button = new() { Content = action.Label, Margin = new Thickness(8, 0, 0, 0) };
            AutomationProperties.SetAutomationId(button, $"Notice{number}{action.Label.Replace(" ", string.Empty, StringComparison.Ordinal)}");
            button.Click += (_, _) =>
            {
                NoticeArea.Children.Remove(bar);
                action.Run();
            };
            buttons.Children.Add(button);
        }

        Button dismiss = new() { Content = "", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetAutomationId(dismiss, $"Notice{number}Dismiss");
        AutomationProperties.SetName(dismiss, "Dismiss");
        dismiss.Click += (_, _) => NoticeArea.Children.Remove(bar);
        buttons.Children.Add(dismiss);

        TextBlock icon = new() { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        AutomationProperties.SetName(icon, notice.Severity.ToString());

        TextBlock title = new() { Text = notice.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(title, $"Notice{number}Title");
        TextBlock message = new() { Text = notice.Message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        AutomationProperties.SetAutomationId(message, $"Notice{number}Message");
        StackPanel text = new() { Orientation = Orientation.Horizontal };
        text.Children.Add(title);
        text.Children.Add(message);

        DockPanel row = new();
        row.Children.Add(buttons);
        row.Children.Add(icon);
        row.Children.Add(text);
        bar.Child = row;
        return bar;
    }
}
