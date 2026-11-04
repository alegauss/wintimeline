using System.Globalization;
using System.Text.RegularExpressions;

namespace WinTimeline.Model.Adapters;

/// <summary>
/// WT158: an EDL states no frame rate, so the person opening one chooses it. These are the rates
/// offered, and the ones an EDL's timecodes fit: a timecode whose frames reach 24 rules out 24
/// fps and everything slower.
/// </summary>
public static partial class EdlRates
{
    /// <summary>The rates offered, in order.</summary>
    public static IReadOnlyList<double> Standard { get; } = [23.976, 24, 25, 29.97, 30, 50, 59.94, 60];

    /// <summary>A rate as the dialog and messages show it, e.g. "29.97".</summary>
    /// <param name="rate">The rate.</param>
    /// <returns>The text.</returns>
    public static string Label(double rate) => rate.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The standard rates every timecode in an EDL fits.</summary>
    /// <param name="edl">The EDL's text.</param>
    /// <returns>The rates, slowest first.</returns>
    public static IReadOnlyList<double> Fitting(string edl)
    {
        ArgumentNullException.ThrowIfNull(edl);
        int highest = Timecode().Matches(edl).Select(m => int.Parse(m.Groups["frames"].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        return [.. Standard.Where(rate => Math.Round(rate) > highest)];
    }

    [GeneratedRegex(@"\b\d{2}:\d{2}:\d{2}[:;](?<frames>\d{2})\b")]
    private static partial Regex Timecode();
}
