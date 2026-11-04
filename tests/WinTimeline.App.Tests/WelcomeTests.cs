using System.IO;
using WinTimeline.Model;

namespace WinTimeline.App.Tests;

/// <summary>WT68: recent documents and what the welcome page says.</summary>
public sealed class WelcomeTests
{
    [Fact]
    public void ARecentDocumentGoesFirstOnceAndTheOldestFallsOff()
    {
        AppSettings settings = new();
        for (int i = 0; i < 12; i++)
        {
            settings = settings.WithRecent($@"D:\cuts\{i}.otio");
        }

        settings = settings.WithRecent(@"D:\CUTS\5.otio");

        Assert.Equal(AppSettings.RecentLimit, settings.RecentFiles!.Count);
        Assert.Equal([@"D:\CUTS\5.otio", @"D:\cuts\11.otio", @"D:\cuts\10.otio"], settings.RecentFiles.Take(3));
        Assert.Single(settings.RecentFiles, p => p.EndsWith("5.otio", StringComparison.OrdinalIgnoreCase));
        Assert.Same(settings, settings.WithRecent(@"D:\CUTS\5.otio"));
    }

    [Fact]
    public void SettingsWithNoRecentFilesAreWrittenAsBefore()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("wt-recent-").FullName, "settings.json");
        SettingsStore store = SettingsStore.Load(path);

        store.Update(s => s with { Adapter = "warp" });
        Assert.DoesNotContain("recent_files", File.ReadAllText(path), StringComparison.Ordinal);

        store.Update(s => s.WithRecent(@"D:\a.otio"));
        Assert.Contains("recent_files", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal([@"D:\a.otio"], SettingsStore.Load(path).Current.RecentFiles!);
    }

    [Fact]
    public void DroppedMediaSaysWhatItMadeAndWhatWasNotMedia()
    {
        Timeline timeline = TimelineBuilder.FromMedia("Untitled", 25, [new MediaClip("bars", "file:///bars.mp4", 3, 25, true)]);

        Assert.Equal("Created a timeline from 1 file: 2 clips on 2 tracks.", MainWindow.CreatedText(1, timeline, []));
        Assert.Equal("Created a timeline from 1 file: 2 clips on 2 tracks. Not media: notes.txt is not media.", MainWindow.CreatedText(1, timeline, ["notes.txt is not media"]));
    }
}
