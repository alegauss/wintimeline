using System.IO;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT161: a file of several timelines journaled reel by reel, versions added and taken away
/// included, and recovered after a crash to the same document with each reel's own undo steps.
/// </summary>
public sealed class CollectionRecoveryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wintimeline-recovery-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static SerializableCollection Reels() =>
        Assert.IsType<SerializableCollection>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "collections", "reels.otio")));

    [Fact]
    public void EditsToEveryReelAndANewVersionAreRecoveredWithEachReelsOwnUndo()
    {
        SerializableCollection document = Reels();
        IReadOnlyList<Timeline> reels = DocumentTimelines.Of(document);
        Dictionary<Timeline, History> histories = new(ReferenceEqualityComparer.Instance);
        foreach (Timeline reel in reels)
        {
            histories[reel] = new History();
        }
        RecoveryJournal journal = RecoveryJournal.Start(_folder, document, "reels.otio", null);
        foreach ((Timeline reel, History history) in histories)
        {
            journal.Watch(reel, history);
        }

        histories[reels[0]].Execute(new Rename(reels[0].FindClips().First(), "renamed in reel1"));
        histories[reels[2]].Execute(new Rename(reels[2].FindClips().First(), "renamed in reel3"));
        histories[reels[1]].Execute(DuplicateTimeline.Command(reels[1]));
        Timeline version = DocumentTimelines.Of(document)[2];
        History versionHistory = new();
        journal.Watch(version, versionHistory);
        versionHistory.Execute(new Rename(version.FindClips().First(), "renamed in the version"));
        string atTheCrash = OtioWriter.Write(document);
        journal.Dispose();

        RecoveredDocument recovered = RecoveryJournal.Recover(_folder, Assert.Single(RecoveryJournal.Pending(_folder)).Id);

        Assert.Equal(atTheCrash, OtioWriter.Write(recovered.Document));
        IReadOnlyList<Timeline> back = DocumentTimelines.Of(recovered.Document);
        Assert.Equal(["reel1", "reel2", "reel2 v002", "reel3"], back.Select(t => t.Name));
        Assert.True(recovered.Histories[back[3]].Undo());
        Assert.Equal("r3c1", back[3].FindClips().First().Name);
        Assert.True(recovered.Histories[back[2]].Undo());
        Assert.True(recovered.Histories[back[1]].Undo());
        Assert.Equal(["reel1", "reel2", "reel3"], DocumentTimelines.Of(recovered.Document).Select(t => t.Name));
    }

    [Fact]
    public void AReelWhoseHistoryWasMadeLaterIsJournaledFromThenOn()
    {
        SerializableCollection document = Reels();
        IReadOnlyList<Timeline> reels = DocumentTimelines.Of(document);
        RecoveryJournal journal = RecoveryJournal.Start(_folder, document, "reels.otio", null);
        History reel2 = new();
        journal.Watch(reels[1], reel2);
        reel2.Execute(new Rename(reels[1].FindClips().First(), "shown second"));
        string atTheCrash = OtioWriter.Write(document);
        journal.Dispose();

        RecoveredDocument recovered = RecoveryJournal.Recover(_folder, Assert.Single(RecoveryJournal.Pending(_folder)).Id);

        Assert.Equal(atTheCrash, OtioWriter.Write(recovered.Document));
        Assert.Equal(reels.Count, recovered.Histories.Count);
    }
}
