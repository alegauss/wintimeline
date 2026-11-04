using System.IO;
using System.Text.Json;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Validation;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT19: every deliberately broken timeline in tests/fixtures/broken (made by make_broken.py with
/// the reference library) produces exactly its expected problems, and each fix is an undoable command.
/// </summary>
public sealed class ValidatorTests
{
    private static readonly string Fixtures = Path.Combine(Repository.Root, "tests", "fixtures", "broken");

    public static TheoryData<string> Broken =>
        [.. JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json"))).RootElement.EnumerateObject().Select(p => p.Name)];

    private static Timeline Load(string name) => Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Fixtures, name + ".otio")));

    private static string[] Codes(Timeline timeline) =>
        [.. Validator.Check(timeline).Select(p => $"{p.Code}@{Paths.Of(timeline, p.Target)}")];

    [Theory]
    [MemberData(nameof(Broken))]
    public void EachBrokenTimelineReportsExactlyItsProblems(string name)
    {
        using JsonDocument expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")));
        string[] wanted = [.. expected.RootElement.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];

        Assert.Equal(wanted, Codes(Load(name)));
    }

    [Theory]
    [InlineData("long-transition")]
    [InlineData("outside-media")]
    public void AFixIsAnUndoableCommandThatRemovesItsProblem(string name)
    {
        Timeline timeline = Load(name);
        string before = OtioWriter.Write(timeline);
        Problem problem = Assert.Single(Validator.Check(timeline));
        History history = new();

        history.Execute(Assert.IsAssignableFrom<IEditCommand>(problem.Fix));

        Assert.Empty(Validator.Check(timeline));
        history.Undo();
        Assert.Equal(before, OtioWriter.Write(timeline));
    }

    [Fact]
    public void TheMessagesNameWhatIsWrongAndWhere()
    {
        Problem transition = Assert.Single(Validator.Check(Load("long-transition")));
        Problem zero = Validator.Check(Load("zero-duration"))[0];
        Problem rate = Assert.Single(Validator.Check(Load("rate-mismatch")));

        Assert.Equal((Severity.Error, "Transition \"x\" reaches further than a and b last."), (transition.Severity, transition.Message));
        Assert.Equal("Clip \"zero\" lasts 0 frames, so it shows nothing.", zero.Message);
        Assert.Equal((Severity.Info, "Clip \"pal\" is at 25 fps in a 24 fps timeline."), (rate.Severity, rate.Message));
    }

    [Fact]
    public void NothingInTheReferencesOwnCorpusIsAnError()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(Repository.Root, "tests", "fixtures", "otio", "corpus"), "*.otio"))
        {
            Timeline timeline = Assert.IsType<Timeline>(OtioReader.ReadFile(file));

            Assert.DoesNotContain(Validator.Check(timeline), p => p.Severity == Severity.Error && p.Code != "range-uncomputable");
        }
    }
}
