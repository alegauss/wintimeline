using System.IO;
using System.Text.Json.Nodes;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT17: a session that dies is rebuilt from its journal up to the last committed command. A
/// "crash" here is a journal disposed without <see cref="RecoveryJournal.Discard"/>, which is all
/// a killed process leaves: the files it flushed, and its handle closed by Windows.
/// </summary>
public sealed class RecoveryTests : IDisposable
{
    private static readonly string Corpus = Path.Combine(Repository.Root, "tests", "fixtures", "otio", "corpus");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wt17-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static Timeline Load(string name) => Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Corpus, name)));

    [Fact]
    public void KilledMidDragTheDocumentComesBackWithEveryStepTheDragHadMade()
    {
        Timeline timeline = Load("simple_cut.otio");
        History history = new();
        RecoveryJournal journal = RecoveryJournal.Start(_folder, timeline, history, "simple_cut.otio", @"D:\cuts\simple_cut.otio");
        Clip clip = timeline.FindClips().First();
        TimeRange start = clip.SourceRange!.Value;

        IDisposable drag = history.BeginGesture();
        for (int frames = 1; frames <= 4; frames++)
        {
            history.Execute(new SetSourceRange(clip, new TimeRange(start.StartTime, new RationalTime(frames, 24))));
        }

        string atTheCrash = OtioWriter.Write(timeline);
        journal.Dispose(); // the process dies here, mid-drag
        GC.KeepAlive(drag);

        RecoveryCandidate candidate = Assert.Single(RecoveryJournal.Pending(_folder));
        Assert.Equal(("simple_cut.otio", @"D:\cuts\simple_cut.otio", 4), (candidate.Name, candidate.SourcePath, candidate.Edits));
        RecoveredDocument recovered = RecoveryJournal.Recover(_folder, candidate.Id);
        Assert.Equal(atTheCrash, OtioWriter.Write(recovered.Timeline));
        Assert.False(recovered.FromAutosave);

        // The drag was one step, so one undo takes the clip back to where it started.
        recovered.History.Undo();
        Assert.Equal(start, recovered.Timeline.FindClips().First().SourceRange);
    }

    [Fact]
    public void ATransactionStillOpenWhenTheSessionDiedIsNotRecovered()
    {
        Timeline timeline = Load("simple_cut.otio");
        History history = new();
        RecoveryJournal journal = RecoveryJournal.Start(_folder, timeline, history, "cut.otio", null);
        Track track = Assert.IsType<Track>(timeline.Tracks.Children[0]);
        history.Execute(new Rename(track.Children[0], "kept"));
        string committed = OtioWriter.Write(timeline);

        History.TransactionScope ripple = history.BeginTransaction("Ripple delete");
        history.Execute(new RemoveChild(track, 1));
        history.Execute(new RemoveChild(track, 1));
        journal.Dispose(); // dies inside the transaction
        GC.KeepAlive(ripple);

        RecoveredDocument recovered = RecoveryJournal.Recover(_folder, Assert.Single(RecoveryJournal.Pending(_folder)).Id);
        Assert.Equal(committed, OtioWriter.Write(recovered.Timeline));
    }

    [Fact]
    public void RandomSessionsWithUndoAndRedoRecoverToTheByte()
    {
        Random random = new(17);
        string[] files = [.. Directory.GetFiles(Corpus, "*.otio").Order(StringComparer.Ordinal)];
        for (int session = 0; session < 300; session++)
        {
            Timeline timeline = Assert.IsType<Timeline>(OtioReader.ReadFile(files[random.Next(files.Length)]));
            History history = new();
            RecoveryJournal journal = RecoveryJournal.Start(_folder, timeline, history, "s" + session, null);
            for (int step = random.Next(1, 20); step > 0; step--)
            {
                switch (random.Next(6))
                {
                    case 0:
                        history.Undo();
                        break;
                    case 1:
                        history.Redo();
                        break;
                    default:
                        CommandTests.RandomEdit(random, timeline, history, []);
                        break;
                }
            }

            string live = OtioWriter.Write(timeline);
            journal.Dispose();

            if (RecoveryJournal.Pending(_folder).SingleOrDefault(c => c.Name == "s" + session) is { } candidate)
            {
                Assert.True(live == OtioWriter.Write(RecoveryJournal.Recover(_folder, candidate.Id).Timeline), $"session {session} recovered to a different document");
                RecoveryJournal.Remove(_folder, candidate.Id);
            }
        }
    }

    [Fact]
    public void ASavedOrCleanlyClosedSessionLeavesNothingToOffer()
    {
        Timeline timeline = Load("simple_cut.otio");
        History history = new();
        RecoveryJournal saved = RecoveryJournal.Start(_folder, timeline, history, "saved.otio", null);
        history.Execute(new Rename(timeline.FindClips().First(), "x"));
        saved.MarkSaved();
        saved.Dispose();

        Timeline other = Load("transition.otio");
        History otherHistory = new();
        RecoveryJournal closed = RecoveryJournal.Start(_folder, other, otherHistory, "closed.otio", null);
        otherHistory.Execute(new Rename(other.FindClips().First(), "y"));
        closed.Discard();

        Assert.Empty(RecoveryJournal.Pending(_folder));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void ALiveSessionIsNotOfferedToAnother()
    {
        Timeline timeline = Load("simple_cut.otio");
        History history = new();
        using RecoveryJournal running = RecoveryJournal.Start(_folder, timeline, history, "running.otio", null);
        history.Execute(new Rename(timeline.FindClips().First(), "x"));

        Assert.Empty(RecoveryJournal.Pending(_folder));
    }

    [Fact]
    public void ALineTornByTheCrashEndsTheReplayAndABrokenJournalFallsBackToTheAutosave()
    {
        Timeline timeline = Load("simple_cut.otio");
        History history = new();
        RecoveryJournal journal = RecoveryJournal.Start(_folder, timeline, history, "torn.otio", null);
        Clip clip = timeline.FindClips().First();
        history.Execute(new Rename(clip, "first"));
        string afterFirst = OtioWriter.Write(timeline);
        journal.Autosave();
        journal.Dispose();
        File.AppendAllText(Path.Combine(_folder, journal.Id + ".journal.jsonl"), "{\"kind\":\"execute\",\"command\":{\"op\":\"ren");

        Assert.Equal(afterFirst, OtioWriter.Write(RecoveryJournal.Recover(_folder, journal.Id).Timeline));

        File.Delete(Path.Combine(_folder, journal.Id + ".base.otio"));
        RecoveredDocument fallback = RecoveryJournal.Recover(_folder, journal.Id);
        Assert.True(fallback.FromAutosave);
        Assert.Equal(afterFirst, OtioWriter.Write(fallback.Timeline));
    }

    [Fact]
    public void TheJournalHasALineForEverythingTheHistoryDid()
    {
        Timeline timeline = Load("simple_cut.otio");
        History history = new();
        RecoveryJournal journal = RecoveryJournal.Start(_folder, timeline, history, "kinds.otio", null);
        Clip clip = timeline.FindClips().First();
        using (history.BeginTransaction("both"))
        {
            history.Execute(new Rename(clip, "a"));
        }

        history.Undo();
        history.Redo();
        journal.MarkSaved();
        journal.Dispose();

        string[] kinds = [.. File.ReadAllLines(Path.Combine(_folder, journal.Id + ".journal.jsonl")).Select(l => (string)JsonNode.Parse(l)!["kind"]!)];
        Assert.Equal(["begin_transaction", "execute", "commit", "undo", "redo", "saved"], kinds);
    }
}
