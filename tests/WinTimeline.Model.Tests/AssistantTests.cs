using System.IO;
using System.Text.Json.Nodes;
using WinTimeline.Model.Commands;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>WT114: an assistant's proposal is checked whole on a copy, and applied as one undo step or not at all.</summary>
public sealed class AssistantTests
{
    private static Timeline Cut() =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "diff", "cut_v1.otio")));

    [Fact]
    public void TheDigestNamesEveryItemByThePathItsCommandsUse()
    {
        string digest = TimelineDigest.Describe(Cut());

        Assert.StartsWith("timeline \"cut v1\" at 24 fps, starting 01:00:00:00\ntrack 0 \"V1\" Video\n", digest, StringComparison.Ordinal);
        Assert.Contains("  0/2 clip \"c3\" 01:00:04:00 to 01:00:05:00 source [0,24]@24\n", digest, StringComparison.Ordinal);
        Assert.Contains("track 1 \"A1\" Audio\n  1/0 clip \"a1\" 01:00:00:00 to 01:00:05:00", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void AValidProposalAppliesAsOneUndoStepTaggedAsTheAssistants()
    {
        Timeline cut = Cut();
        string before = OtioWriter.Write(cut);
        History history = new();

        AssistantProposal proposal = AssistantProposal.Check(cut, """
            { "plan": "Drop c3 and mark the product shot",
              "commands": [
                { "op": "remove_child", "target": "0", "index": 2 },
                { "op": "add_marker", "target": "0/2", "name": "product", "range": [[0, 24], [12, 24]], "color": "RED" },
                { "op": "rename", "target": "0/0", "name": "opening" } ] }
            """);

        Assert.True(proposal.IsValid, string.Join("; ", proposal.Problems));
        Assert.Equal(before, OtioWriter.Write(cut));
        proposal.Apply(history, cut);

        Assert.Equal(["c4", "product"], [cut.Tracks.Children[0] is Track v1 ? v1.Children[2].Name : "", ((Item)((Track)cut.Tracks.Children[0]).Children[2]).Markers[0].Name]);
        Assert.Equal("AI: Drop c3 and mark the product shot", history.UndoDescription);
        Assert.True(history.Undo());
        Assert.Equal(before, OtioWriter.Write(cut));
    }

    [Theory]
    [InlineData("""{ "plan": "x", "commands": [ { "op": "remove_child", "target": "0", "index": 9 } ] }""", "command 1 (remove_child):")]
    [InlineData("""{ "plan": "x", "commands": [ { "op": "rename", "target": "7/1", "name": "y" } ] }""", "command 1 (rename): '7/1' names nothing")]
    [InlineData("""{ "plan": "x", "commands": [ { "op": "relink", "target": "0/0", "file": "C:/x.mov" } ] }""", "command 1 uses 'relink', which the assistant may not")]
    [InlineData("""{ "plan": "x", "commands": [ { "op": "set_source_range", "target": "0/0", "range": [[0, 24], [0, 24]] } ] }""", "command 1 (set_source_range): a trim must leave a positive duration")]
    [InlineData("""Sure! Here are the edits you asked for.""", "the reply is not JSON")]
    [InlineData("""{ "plan": "x" }""", "the reply has no list of commands")]
    [InlineData("""{ "commands": [] }""", "the reply does not say what it plans")]
    public void AnInvalidProposalIsRefusedWithItsReasonAndChangesNothing(string reply, string problem)
    {
        Timeline cut = Cut();
        string before = OtioWriter.Write(cut);

        AssistantProposal proposal = AssistantProposal.Check(cut, reply);

        Assert.False(proposal.IsValid);
        Assert.StartsWith(problem, proposal.Problems[0], StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => proposal.Apply(new History(), cut));
        Assert.Equal(before, OtioWriter.Write(cut));
    }

    [Fact]
    public void OneBadCommandRefusesTheWholeBatchAndLaterCommandsSeeTheEarlierOnes()
    {
        Timeline cut = Cut();

        // After removing 0/5 (c7) the track has five children, so index 5 no longer exists.
        AssistantProposal proposal = AssistantProposal.Check(cut, """
            { "plan": "x", "commands": [
                { "op": "remove_child", "target": "0", "index": 5 },
                { "op": "rename", "target": "0/0", "name": "kept" },
                { "op": "remove_child", "target": "0", "index": 5 } ] }
            """);

        Assert.False(proposal.IsValid);
        Assert.StartsWith("command 3 (remove_child)", Assert.Single(proposal.Problems), StringComparison.Ordinal);
        Assert.Equal("c1", ((Track)cut.Tracks.Children[0]).Children[0].Name);
    }

    [Fact]
    public void TheReplySchemaOffersExactlyTheAssistantsOps()
    {
        JsonObject schema = AssistantTools.Schema();
        string[] ops = [.. schema["properties"]!["commands"]!["items"]!["oneOf"]!.AsArray().Select(o => o!["properties"]!["op"]!["const"]!.GetValue<string>())];

        Assert.Equal(AssistantTools.Ops.Keys.Order(StringComparer.Ordinal), ops.Order(StringComparer.Ordinal));
        Assert.Equal(AssistantProposal.MostCommands, schema["properties"]!["commands"]!["maxItems"]!.GetValue<int>());
    }
}
