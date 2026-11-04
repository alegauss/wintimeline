using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using WinTimeline.Diagnostics;
using WinTimeline.Render;

namespace WinTimeline.App;

/// <summary>WT6: Help > Create diagnostics bundle, the zip a person attaches to an issue.</summary>
public partial class MainWindow
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>Builds the bundle this run would save: logs, settings, GPU, system and the newest crash dump.</summary>
    /// <returns>The bundle.</returns>
    public DiagnosticsBundle Diagnostics()
    {
        DiagnosticsBundle bundle = new DiagnosticsBundle()
            .Section("system.json", JsonSerializer.Serialize(new
            {
                Version = typeof(App).Assembly.GetName().Version?.ToString(),
                Os = Environment.OSVersion.VersionString,
                Runtime = Environment.Version.ToString(),
                Is64Bit = Environment.Is64BitProcess,
                Options,
            }, IndentedJson))
            .Section("gpu.json", JsonSerializer.Serialize(new
            {
                InUse = Gpu.Adapter,
                FeatureLevel = Gpu.FeatureLevel.ToString(),
                Gpu.IsWarp,
                Gpu.VideoSupport,
                Adapters = GpuAdapters.Enumerate(),
            }, IndentedJson))
            .File("settings.json", Settings.Path)
            .NewestDump(App.CrashFolder);

        // No document exists yet; when one does, it joins the bundle only after the person agrees.
        return Log.Shared is { } log ? bundle.Logs(log) : bundle;
    }

    private void OnDiagnostics(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Title = UiText.DiagnosticsDialogTitle,
            FileName = $"wintimeline-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            Filter = UiText.ZipFilter,
            DefaultExt = ".zip",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            IReadOnlyList<string> written = Diagnostics().Save(dialog.FileName);
            Log.Write(LogLevel.Info, LogCategory.Ui, "Diagnostics bundle saved.",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["entries"] = string.Join(", ", written) });
            StatusText.Text = UiText.DiagnosticsSavedTo(Path.GetFileName(dialog.FileName));
            string saved = dialog.FileName;
            Notify(new Notices.Notice(Notices.NoticeSeverity.Success, UiText.DiagnosticsSaved, Path.GetFileName(saved), [new Notices.NoticeAction(UiText.ShowFile, () => ShowInExplorer(saved))]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(LogLevel.Error, LogCategory.Ui, "Diagnostics bundle could not be saved.", error: ex);
            StatusText.Text = UiText.DiagnosticsNotSavedBecause(ex.Message);
            Notify(new Notices.Notice(Notices.NoticeSeverity.Error, UiText.DiagnosticsNotSaved, ex.Message, [new Notices.NoticeAction(UiText.TryAgain,() => OnDiagnostics(this, new RoutedEventArgs()))]));
        }
    }
}
