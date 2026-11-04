using System.Windows;
using System.Windows.Controls;
using WinTimeline.Model;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Validation;

namespace WinTimeline.App;

/// <summary>
/// WT19: the open timeline's problems, most serious first; a fix runs through the document's
/// history, so it undoes like any edit.
/// </summary>
public partial class ProblemsWindow : Window
{
    private readonly Timeline _timeline;
    private readonly History _history;
    private IReadOnlyList<Problem> _problems = [];

    /// <summary>Opens the panel over a timeline.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <param name="history">Its history, which fixes go through.</param>
    public ProblemsWindow(Timeline timeline, History history)
    {
        _timeline = timeline;
        _history = history;
        InitializeComponent();
        Refresh();
    }

    /// <summary>How a problem is listed, e.g. "Error at 0/1: Transition "x" reaches further than a and b last."</summary>
    /// <param name="timeline">The timeline it is in.</param>
    /// <param name="problem">The problem.</param>
    /// <returns>The line.</returns>
    public static string Line(Timeline timeline, Problem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return $"{problem.Severity} at {Paths.Of(timeline, problem.Target)}: {problem.Message}{(problem.Fix is null ? string.Empty : " (fixable)")}";
    }

    /// <summary>What the status bar adds after an open, e.g. " 2 problems: View > Problems."</summary>
    /// <param name="count">How many problems.</param>
    /// <returns>The sentence, or empty.</returns>
    public static string CountText(int count) =>
        count == 0 ? string.Empty : $" {count} {(count == 1 ? "problem" : "problems")}: View > Problems.";

    /// <summary>What the panel says after fixing.</summary>
    /// <param name="fixedCount">How many were fixed.</param>
    /// <param name="left">How many are left.</param>
    /// <returns>The sentence.</returns>
    public static string FixedText(int fixedCount, int left) =>
        $"Fixed {fixedCount} {(fixedCount == 1 ? "problem" : "problems")}. "
        + (left == 0 ? "Nothing else is wrong." : $"{left} {(left == 1 ? "needs" : "need")} a person.");

    /// <summary>The problems, most serious first, then in timeline order.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <returns>The problems.</returns>
    public static IReadOnlyList<Problem> Sorted(Timeline timeline) =>
        [.. Validator.Check(timeline).Select((p, i) => (p, i)).OrderByDescending(x => x.p.Severity).ThenBy(x => x.i).Select(x => x.p)];

    private void Refresh()
    {
        _problems = Sorted(_timeline);
        ProblemList.ItemsSource = _problems.Select(p => Line(_timeline, p)).ToList();
        if (_problems.Count > 0)
        {
            ProblemList.SelectedIndex = 0;
        }

        FixAllButton.IsEnabled = _problems.Any(p => p.Fix is not null);
        OnSelection(this, null!);
    }

    private void OnSelection(object sender, SelectionChangedEventArgs e) =>
        FixButton.IsEnabled = ProblemList.SelectedIndex >= 0 && ProblemList.SelectedIndex < _problems.Count && _problems[ProblemList.SelectedIndex].Fix is not null;

    private void OnFix(object sender, RoutedEventArgs e)
    {
        if (ProblemList.SelectedIndex < 0 || _problems[ProblemList.SelectedIndex].Fix is not { } fix)
        {
            return;
        }

        _history.Execute(fix);
        Refresh();
        ProblemsStatus.Text = FixedText(1, _problems.Count);
    }

    private void OnFixAll(object sender, RoutedEventArgs e)
    {
        int count = 0;
        using (_history.BeginTransaction("Fix problems"))
        {
            // Re-checked after each fix: one fix can change what the next one should do.
            while (Validator.Check(_timeline).FirstOrDefault(p => p.Fix is not null) is { Fix: { } fix } && count < 1000)
            {
                _history.Execute(fix);
                count++;
            }
        }

        Refresh();
        ProblemsStatus.Text = FixedText(count, _problems.Count);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
