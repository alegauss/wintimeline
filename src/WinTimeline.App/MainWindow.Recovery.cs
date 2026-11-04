using System.IO;
using System.Windows;

namespace WinTimeline.App;

/// <summary>
/// WT7: the bar a start shows after the last run crashed, offering the diagnostics bundle that
/// carries the dump. Recovering the document itself arrives with autosave (Block B).
/// </summary>
public partial class MainWindow
{
    /// <summary>What the bar says about a crashed run.</summary>
    /// <param name="previous">What the last run left.</param>
    /// <returns>The sentence.</returns>
    public static string RecoveryMessage(PreviousCrash previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        string when = previous.Started.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
        return UiText.CrashedLastTime(when, previous.Dump is { } dump ? Path.GetFileName(dump) : null);
    }

    private void ShowRecovery(PreviousCrash? previous)
    {
        if (previous is null)
        {
            return;
        }

        RecoveryText.Text = RecoveryMessage(previous);
        RecoveryBar.Visibility = Visibility.Visible;
    }

    private void OnRecoveryDismiss(object sender, RoutedEventArgs e) => RecoveryBar.Visibility = Visibility.Collapsed;
}
