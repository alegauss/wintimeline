using WinTimeline.Diagnostics;
using WinTimeline.Model.Adapters;

namespace WinTimeline.App;

/// <summary>
/// WT157: looks, once and off the UI thread, for a Python whose OpenTimelineIO reads formats the
/// app does not (AAF and the rest), and adds them to File > Open. On a desk without one, nothing
/// is added and the menu offers nothing it cannot open.
/// </summary>
public partial class MainWindow
{
    private Task? _bridge;

    private void StartBridge()
    {
        string? configured = Settings.Current.Python;
        _bridge = Task.Run(() =>
        {
            PythonBridge? bridge = PythonBridge.Find(configured);
            TimelineAdapters.UseBridge(bridge);
            Log.Write(LogLevel.Info, LogCategory.App, bridge is null ? "No Python with OpenTimelineIO was found." : "OpenTimelineIO's Python adapters are bridged.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["python"] = bridge?.Python ?? configured ?? "(PATH)",
                    ["adapters"] = bridge is null ? string.Empty : string.Join(", ", bridge.Adapters.Select(a => a.Name)),
                });
        });
    }

    /// <summary>Waits, briefly, for the search, so a file passed with --open at start is not refused before it ends.</summary>
    private void AwaitBridge()
    {
        if (_bridge is null)
        {
            StartBridge();
        }

        _bridge!.Wait(TimeSpan.FromSeconds(30));
    }
}
