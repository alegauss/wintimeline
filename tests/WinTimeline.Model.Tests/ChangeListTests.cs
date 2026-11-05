using System.Diagnostics;
using System.IO;
using WinTimeline.Model.Adapters;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Compare;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>WT162: a comparison handed on as a change-list EDL both readers take back as its five events, and as five markers.</summary>
public sealed class ChangeListTests
{
    private static readonly string Venv = Path.Combine(Repository.Root, ".cache", "venv", "Scripts", "python.exe");

    private static Timeline Cut(string name) =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", name)));

    [Fact]
    public void TheChangeListIsOneEventPerChangeThatOurReaderTakesBack()
    {
        Timeline after = Cut("cut_v2.otio");
        TimelineDiff diff = TimelineDiff.Compare(Cut("cut_v1.otio"), after);

        string edl = ChangeList.Edl(diff, after);

        Assert.Equal(5, diff.Changes.Count);
        Assert.Equal(5, edl.Split('\n').Count(l => l.Length > 3 && char.IsDigit(l[0])));
        Assert.Equal(diff.Changes.Select(c => "* CHANGE: " + c.Text).Order(), edl.Split('\n').Where(l => l.StartsWith("* CHANGE:", StringComparison.Ordinal)).Order());
        Assert.Equal(5, CmxEdlReader.Read(edl, 24, ignoreTimecodeMismatch: true).FindClips().Count());
    }

    [Fact]
    public void TheReferenceAdapterReadsTheChangeListBackAsFiveEvents()
    {
        Assert.SkipUnless(File.Exists(Venv), "the reference venv (.cache\\venv) is not on this desk");
        Timeline after = Cut("cut_v2.otio");
        string edl = ChangeList.Edl(TimelineDiff.Compare(Cut("cut_v1.otio"), after), after);

        ProcessStartInfo start = new(Venv, ["-I", "-c", "import sys, opentimelineio as o; t = o.adapters.read_from_string(sys.stdin.read(), 'cmx_3600', rate=24); print(len(list(t.find_clips())))"])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process python = Process.Start(start)!;
        python.StandardInput.Write(edl);
        python.StandardInput.Close();
        string read = python.StandardOutput.ReadToEnd().Trim(), error = python.StandardError.ReadToEnd();
        python.WaitForExit();

        Assert.True(python.ExitCode == 0, error);
        Assert.Equal("5", read);
    }

    [Fact]
    public void EachChangeIsAMarkerOnTheLaterVersionInItsKindsColourAndOneUndoTakesThemOff()
    {
        Timeline after = Cut("cut_v2.otio");
        TimelineDiff diff = TimelineDiff.Compare(Cut("cut_v1.otio"), after);
        History history = new();
        string before = OtioWriter.Write(after);

        using (history.BeginTransaction("Mark changes"))
        {
            Assert.Equal(5, ChangeList.Mark(history, diff, after));
        }

        Assert.Equal(diff.Changes.Select(c => (c.Kind.ToString(), ChangeList.ColorOf(c.Kind), c.Text)), after.Tracks.Markers.Select(m => (m.Name, m.Color, m.Comment)));
        history.Undo();
        Assert.Equal(before, OtioWriter.Write(after));
    }
}
