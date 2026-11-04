using System.IO;
using System.Text;
using WinTimeline.Model.Adapters;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// Every sample of OpenTimelineIO's own FCPXML adapter reads into what that adapter reads,
/// written byte for byte as the reference writes it — or is refused, with the reference's
/// message, where the reference refuses it (tests/fixtures/fcpx, made by make_expected.py).
/// </summary>
public sealed class FcpxXmlTests
{
    private static readonly string Fixtures = Path.Combine(Repository.Root, "tests", "fixtures", "fcpx");

    public static TheoryData<string> Samples
    {
        get
        {
            TheoryData<string> rows = [];
            foreach (string xml in Directory.GetFiles(Path.Combine(Fixtures, "samples"), "*.fcpxml").Order(StringComparer.Ordinal))
            {
                rows.Add(Path.GetFileNameWithoutExtension(xml));
            }

            return rows;
        }
    }

    [Fact]
    public void EverySampleHasTheReferencesAnswer()
    {
        Assert.Equal(5, Samples.Count);
        foreach (string xml in Directory.GetFiles(Path.Combine(Fixtures, "samples"), "*.fcpxml"))
        {
            string name = Path.GetFileNameWithoutExtension(xml);
            string stem = Path.Combine(Fixtures, "expected", name);
            Assert.True(File.Exists(stem + ".otio") ^ File.Exists(stem + ".error.txt"), name);
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EverySampleReadsAsTheReferenceReadsIt(string name)
    {
        string text = File.ReadAllText(Path.Combine(Fixtures, "samples", name + ".fcpxml"), Encoding.UTF8);
        string stem = Path.Combine(Fixtures, "expected", name);

        if (File.Exists(stem + ".error.txt"))
        {
            // "<ExceptionType>: <message>": whatever Python raised, this reader raises its own
            // refusal with the same message.
            string expected = File.ReadAllText(stem + ".error.txt", Encoding.UTF8);
            int colon = expected.IndexOf(": ", StringComparison.Ordinal);
            FcpxXmlException error = Assert.Throws<FcpxXmlException>(() => FcpxXmlReader.Read(text));
            Assert.Equal(expected[(colon + 2)..], error.Message);
            return;
        }

        ModelObject read = FcpxXmlReader.Read(text);

        Assert.Equal(File.ReadAllText(stem + ".otio", Encoding.UTF8), OtioWriter.Write(read));
    }

    [Fact]
    public void ARateIsTruncatedToWholeFramesAsTheReferenceTruncatesIt()
    {
        // 1001/30000s is 29.97 frames a second, which the reference counts as 29.
        const string Xml = """
            <fcpxml version="1.9">
              <resources>
                <format id="r1" frameDuration="1001/30000s"/>
                <asset id="r2" name="A" src="file:///a.mov" start="0s" duration="10s" format="r1" hasVideo="1"/>
              </resources>
              <project name="P"><sequence format="r1"><spine>
                <asset-clip name="A" ref="r2" offset="0s" duration="2s"/>
              </spine></sequence></project>
            </fcpxml>
            """;

        Timeline timeline = Assert.IsType<Timeline>(FcpxXmlReader.Read(Xml));
        Clip clip = Assert.IsType<Clip>(Assert.Single(Assert.Single(timeline.Tracks.Children.Cast<Track>()).Children));

        Assert.Equal(29, clip.SourceRange!.Value.Duration.Rate);
        Assert.Equal(58, clip.SourceRange.Value.Duration.Value);
    }

    [Fact]
    public void ConnectedClipsGoOnTheirLanesTrackWithAGapBefore()
    {
        const string Xml = """
            <fcpxml version="1.10">
              <resources>
                <format id="r1" frameDuration="1/25s"/>
                <asset id="r2" name="A" src="file:///a.mov" start="0s" duration="20s" format="r1" hasVideo="1"/>
                <asset id="r3" name="B" src="file:///b.mov" start="0s" duration="20s" format="r1" hasVideo="1"/>
              </resources>
              <project name="P"><sequence format="r1"><spine>
                <asset-clip name="A" ref="r2" offset="0s" duration="10s">
                  <asset-clip name="B" ref="r3" lane="1" offset="4s" duration="2s"/>
                </asset-clip>
              </spine></sequence></project>
            </fcpxml>
            """;

        Timeline timeline = Assert.IsType<Timeline>(FcpxXmlReader.Read(Xml));
        Track[] tracks = [.. timeline.Tracks.Children.Cast<Track>()];

        Assert.Equal(["0", "1"], tracks.Select(t => t.Name));
        Assert.Equal(100, Assert.IsType<Gap>(tracks[1].Children[0]).Duration.Value);
        Assert.Equal("B", tracks[1].Children[1].Name);
    }

    [Fact]
    public void ADocumentWithNothingToReadIsRefused()
    {
        Assert.Throws<FcpxXmlException>(() => FcpxXmlReader.Read("<fcpxml version=\"1.11\"><resources/></fcpxml>"));
    }

    [Fact]
    public void ADocumentThatIsNotXmlIsRefused()
    {
        Assert.Throws<FcpxXmlException>(() => FcpxXmlReader.Read("<fcpxml><project>"));
    }
}
