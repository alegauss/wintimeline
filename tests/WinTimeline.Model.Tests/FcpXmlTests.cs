using System.IO;
using System.Text;
using WinTimeline.Model.Adapters;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// Every sample of OpenTimelineIO's own FCP 7 XML adapter reads into what that adapter reads,
/// written byte for byte as the reference writes it — or is refused, with the reference's
/// message, where the reference refuses it (tests/fixtures/fcp, made by make_expected.py).
/// </summary>
public sealed class FcpXmlTests
{
    private static readonly string Fixtures = Path.Combine(Repository.Root, "tests", "fixtures", "fcp");

    public static TheoryData<string> Samples
    {
        get
        {
            TheoryData<string> rows = [];
            foreach (string xml in Directory.GetFiles(Path.Combine(Fixtures, "samples"), "*.xml").Order(StringComparer.Ordinal))
            {
                rows.Add(Path.GetFileNameWithoutExtension(xml));
            }

            return rows;
        }
    }

    [Fact]
    public void EverySampleHasTheReferencesAnswer()
    {
        Assert.Equal(7, Samples.Count);
        foreach (string xml in Directory.GetFiles(Path.Combine(Fixtures, "samples"), "*.xml"))
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
        string text = File.ReadAllText(Path.Combine(Fixtures, "samples", name + ".xml"), Encoding.UTF8);
        string stem = Path.Combine(Fixtures, "expected", name);

        if (File.Exists(stem + ".error.txt"))
        {
            // "<ExceptionType>: <message>": the reference refuses with a ValueError, which this
            // reader raises as its own refusal, with the same message.
            string expected = File.ReadAllText(stem + ".error.txt", Encoding.UTF8);
            int colon = expected.IndexOf(": ", StringComparison.Ordinal);
            Assert.Equal("ValueError", expected[..colon]);
            FcpXmlException error = Assert.Throws<FcpXmlException>(() => FcpXmlReader.Read(text));
            Assert.Equal(expected[(colon + 2)..], error.Message);
            return;
        }

        ModelObject read = FcpXmlReader.Read(text);

        Assert.Equal(File.ReadAllText(stem + ".otio", Encoding.UTF8), OtioWriter.Write(read));
    }

    [Fact]
    public void SeveralSequencesComeBackAsACollection()
    {
        const string Xml = """
            <xmeml version="4"><project><children>
              <sequence id="a"><name>A</name><rate><timebase>24</timebase></rate><media/></sequence>
              <sequence id="b"><name>B</name><rate><timebase>25</timebase></rate><media/></sequence>
            </children></project></xmeml>
            """;

        SerializableCollection collection = Assert.IsType<SerializableCollection>(FcpXmlReader.Read(Xml));

        Assert.Equal("Sequences", collection.Name);
        Assert.Equal(["A", "B"], collection.Children.Cast<Timeline>().Select(t => t.Name));
    }

    [Fact]
    public void ADocumentThatIsNotXmlIsRefused()
    {
        Assert.Throws<FcpXmlException>(() => FcpXmlReader.Read("<xmeml><sequence>"));
    }
}
