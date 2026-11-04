using System.IO;
using System.Text.Json.Nodes;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Tests;

/// <summary>WT16: every edit runs through one pipeline, undoes to the byte, and has a JSON form.</summary>
public sealed class CommandTests
{
    private const int Sequences = 10_000;

    private static readonly string[] Corpus =
        [.. Directory.GetFiles(Path.Combine(Repository.Root, "tests", "fixtures", "otio", "corpus"), "*.otio").Order(StringComparer.Ordinal)];

    private static Timeline Load(string path) => Assert.IsType<Timeline>(OtioReader.ReadFile(path));

    [Fact]
    public void TenThousandRandomSequencesUndoToTheOriginalAndRedoToTheEnd()
    {
        Random random = new(16);
        Dictionary<string, string> originals = Corpus.ToDictionary(f => f, f => OtioWriter.Write(Load(f)), StringComparer.Ordinal);

        for (int sequence = 0; sequence < Sequences; sequence++)
        {
            string file = Corpus[random.Next(Corpus.Length)];
            Timeline timeline = Assert.IsType<Timeline>(OtioReader.ReadString(originals[file]));
            History history = new();
            List<JsonObject> recorded = [];
            int steps = random.Next(1, 16);
            for (int step = 0; step < steps; step++)
            {
                RandomEdit(random, timeline, history, recorded);
            }

            string edited = OtioWriter.Write(timeline);
            while (history.Undo())
            {
            }

            Assert.True(originals[file] == OtioWriter.Write(timeline), $"sequence {sequence} on {Path.GetFileName(file)} did not undo to the original");

            while (history.Redo())
            {
            }

            Assert.True(edited == OtioWriter.Write(timeline), $"sequence {sequence} on {Path.GetFileName(file)} did not redo to its end");

            // The same edits, sent as JSON to a fresh copy, end in the same place.
            if (sequence % 10 == 0)
            {
                Timeline replay = Assert.IsType<Timeline>(OtioReader.ReadString(originals[file]));
                History replayed = new();
                foreach (JsonObject json in recorded)
                {
                    replayed.Execute(CommandJson.Parse(replay, json));
                }

                Assert.True(edited == OtioWriter.Write(replay), $"sequence {sequence} replayed from JSON ended elsewhere");
            }
        }
    }

    [Fact]
    public void ATransactionIsOneUndoStepAndACancelledOneLeavesNothing()
    {
        Timeline timeline = Load(Corpus.Single(f => f.EndsWith("simple_cut.otio", StringComparison.Ordinal)));
        string original = OtioWriter.Write(timeline);
        History history = new();
        Track track = Assert.IsType<Track>(timeline.Tracks.Children[0]);

        using (history.BeginTransaction("Ripple delete"))
        {
            history.Execute(new RemoveChild(track, 0));
            history.Execute(new Rename(track.Children[0], "first now"));
        }

        Assert.Equal("Ripple delete", history.UndoDescription);
        Assert.Single(history.UndoSteps);
        history.Undo();
        Assert.Equal(original, OtioWriter.Write(timeline));

        History.TransactionScope scope = history.BeginTransaction("Abandoned");
        history.Execute(new RemoveChild(track, 0));
        scope.Cancel();
        Assert.Equal(original, OtioWriter.Write(timeline));
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void ADragIsOneStepHoweverManyTrimsItSends()
    {
        Timeline timeline = Load(Corpus.Single(f => f.EndsWith("simple_cut.otio", StringComparison.Ordinal)));
        Clip clip = timeline.FindClips().First();
        TimeRange before = clip.SourceRange!.Value;
        History history = new();

        using (history.BeginGesture())
        {
            for (int frames = 1; frames <= 5; frames++)
            {
                history.Execute(new SetSourceRange(clip, new TimeRange(before.StartTime, new RationalTime(frames, 24))));
            }
        }

        history.Execute(new SetSourceRange(clip, new TimeRange(before.StartTime, new RationalTime(9, 24))));

        Assert.Equal(2, history.UndoSteps.Count());
        history.Undo();
        Assert.Equal(new RationalTime(5, 24), clip.Duration);
        history.Undo();
        Assert.Equal(before, clip.SourceRange);
    }

    [Fact]
    public void PastItsBudgetTheHistoryForgetsTheOldestStepsNotTheNewest()
    {
        Timeline timeline = Load(Corpus.Single(f => f.EndsWith("simple_cut.otio", StringComparison.Ordinal)));
        Clip clip = timeline.FindClips().First();
        History history = new(budgetBytes: 64 * 3);

        for (int i = 0; i < 10; i++)
        {
            history.Execute(new Rename(clip, $"name {i}"));
        }

        Assert.True(history.RetainedBytes <= history.BudgetBytes);
        Assert.InRange(history.UndoSteps.Count(), 1, 3);
        history.Undo();
        Assert.Equal("name 8", clip.Name);
    }

    [Fact]
    public void ANewEditForgetsWhatCouldBeRedoneAndEveryChangeIsAnnounced()
    {
        Timeline timeline = Load(Corpus.Single(f => f.EndsWith("simple_cut.otio", StringComparison.Ordinal)));
        Clip clip = timeline.FindClips().First();
        List<ModelChange> changes = [];
        timeline.ModelChanged += (_, change) => changes.Add(change);
        History history = new();

        history.Execute(new SetEnabled(clip, false));
        history.Undo();
        Assert.True(history.CanRedo);
        history.Execute(new SetMetadata(clip, "rating", 5L));

        Assert.False(history.CanRedo);
        Assert.Equal(["enabled", "enabled", "metadata"], changes.Select(c => c.What));
        Assert.All(changes, c => Assert.Same(clip, c.Target));
    }

    [Fact]
    public void AnUnknownOpOrAPathToNothingIsRefused()
    {
        Timeline timeline = Load(Corpus.Single(f => f.EndsWith("simple_cut.otio", StringComparison.Ordinal)));

        Assert.Throws<ArgumentException>(() => CommandJson.Parse(timeline, new JsonObject { ["op"] = "explode" }));
        Assert.Throws<ArgumentException>(() => CommandJson.Parse(timeline, new JsonObject { ["op"] = "rename", ["target"] = "0/99", ["name"] = "x" }));
    }

    // One random edit, recorded as JSON just before it runs, while its paths still name what it targets.
    internal static void RandomEdit(Random random, Timeline timeline, History history, List<JsonObject> recorded)
    {
        void Run(IEditCommand command)
        {
            recorded.Add(command.ToJson(timeline));
            history.Execute(command);
        }

        List<Composable> all = [];
        Walk(timeline.Tracks, all);
        List<Composition> compositions = [timeline.Tracks, .. all.OfType<Composition>()];
        List<Item> items = [.. all.OfType<Item>()];

        switch (random.Next(10))
        {
            case 0 when items.Count > 0:
                Item item = items[random.Next(items.Count)];
                double rate = item.SourceRange?.Duration.Rate ?? 24;
                Run(new SetSourceRange(item, random.Next(4) == 0 ? null : new TimeRange(new RationalTime(random.Next(100), rate), new RationalTime(random.Next(1, 100), rate))));
                break;
            case 1:
                Composition from = compositions[random.Next(compositions.Count)];
                if (from.Children.Count > 0)
                {
                    Run(new RemoveChild(from, random.Next(from.Children.Count)));
                }

                break;
            case 2:
                Composition into = compositions[random.Next(compositions.Count)];
                Metadata fields = new();
                fields.Set("source_range", new TimeRange(new RationalTime(0, 24), new RationalTime(random.Next(1, 48), 24)));
                fields.Set("name", "gap " + random.Next(1000));
                Run(new InsertChild(into, random.Next(into.Children.Count + 1), new Gap(fields)));
                break;
            case 3:
                Composition source = compositions[random.Next(compositions.Count)];
                Composition target = compositions[random.Next(compositions.Count)];
                if (source.Children.Count > 0)
                {
                    int index = random.Next(source.Children.Count);
                    Composable moving = source.Children[index];
                    if (!Contains(moving, target))
                    {
                        int room = target.Children.Count - (ReferenceEquals(source, target) ? 1 : 0);
                        Run(new MoveChild(source, index, target, random.Next(room + 1)));
                    }
                }

                break;
            case 4 when all.Count > 0:
                Run(new Rename(all[random.Next(all.Count)], "renamed " + random.Next(1000)));
                break;
            case 5 when items.Count > 0:
                Run(new SetEnabled(items[random.Next(items.Count)], random.Next(2) == 0));
                break;
            case 6 when all.Count > 0:
                object? value = random.Next(4) switch { 0 => null, 1 => "note " + random.Next(100), 2 => (long)random.Next(100), _ => random.NextDouble() };
                Run(new SetMetadata(all[random.Next(all.Count)], "key" + random.Next(3), value));
                break;
            case 7 when all.OfType<Transition>().Any():
                Transition[] transitions = [.. all.OfType<Transition>()];
                Run(new SetTransitionOffsets(transitions[random.Next(transitions.Length)], new RationalTime(random.Next(12), 24), new RationalTime(random.Next(12), 24)));
                break;
            case 8 when items.Count > 0:
                Run(new AddMarker(items[random.Next(items.Count)], "m" + random.Next(100), new TimeRange(new RationalTime(random.Next(48), 24), new RationalTime(0, 24))));
                break;
            case 9:
                using (history.BeginTransaction("several"))
                {
                    RandomEdit(random, timeline, history, recorded);
                    RandomEdit(random, timeline, history, recorded);
                }

                break;
        }
    }

    private static void Walk(Composition composition, List<Composable> into)
    {
        foreach (Composable child in composition.Children)
        {
            into.Add(child);
            if (child is Composition inner)
            {
                Walk(inner, into);
            }
        }
    }

    private static bool Contains(Composable ancestor, Composition candidate)
    {
        for (Composable? at = candidate; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
