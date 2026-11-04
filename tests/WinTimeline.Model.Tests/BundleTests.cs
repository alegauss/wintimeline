using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Tests;

/// <summary>
/// WT14: .otioz and .otiod bundles, against one OpenTimelineIO 0.18.1 wrote from the same
/// timeline (tests/fixtures/otio/bundles, made by make_bundles.py).
/// </summary>
public sealed class BundleTests : IDisposable
{
    private static readonly string Fixtures = Path.Combine(Repository.Root, "tests", "fixtures", "otio", "bundles");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wt14-" + Guid.NewGuid().ToString("N"));

    public BundleTests() => Directory.CreateDirectory(_folder);

    private string Cache => Path.Combine(_folder, "cache");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void TheReferencesBundleOpensWithoutUnpackingAndItsMediaIsExtractedWhenAskedFor()
    {
        OtioBundle bundle = OtioBundle.Open(Path.Combine(Fixtures, "reference.otioz"), Cache);

        Timeline timeline = Assert.IsType<Timeline>(bundle.Content);
        Assert.Equal(["a", "b", "web"], timeline.FindClips().Select(c => c.Name));
        Assert.Equal(["media/shot-a.mov", "media/shot-b.wav"], bundle.MediaEntries);
        Assert.False(Directory.Exists(Cache));

        string? path = bundle.MediaPath("media/shot-a.mov");
        Assert.NotNull(path);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Fixtures, "media", "shot-a.mov")), File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(Cache, "*", SearchOption.AllDirectories));

        // The reference's own Windows spelling of an .otiod's URL resolves to the same file.
        Assert.Equal(path, bundle.MediaPath(@"media\shot-a.mov"));
        Assert.Null(bundle.MediaPath("https://example.com/c.mov"));
        Assert.Null(bundle.MediaPath("media/not-in-the-bundle.mov"));
    }

    [Fact]
    public void ABundleIsWrittenAsTheReferenceWritesIt()
    {
        string path = Path.Combine(_folder, "out.otioz");

        OtioBundle.Write(Source(), path, MediaReferencePolicy.MissingIfNotFile);

        using ZipArchive zip = ZipFile.OpenRead(path);
        Assert.Equal(["version.txt", "content.otio", "media/shot-a.mov", "media/shot-b.wav"], zip.Entries.Select(e => e.FullName));
        Assert.Equal("1.0.0", Read(zip, "version.txt"));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Fixtures, "media", "shot-b.wav")), ReadBytes(zip, "media/shot-b.wav"));

        // Everything as the reference wrote it, but the web clip's missing reference, which here
        // keeps what the original said instead of losing it.
        JsonNode ours = JsonNode.Parse(Read(zip, "content.otio"))!;
        JsonNode theirs = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "reference-content.otio")))!;
        JsonNode? ourMissing = TakeWebReference(ours);
        JsonNode? theirMissing = TakeWebReference(theirs);
        Assert.True(JsonNode.DeepEquals(theirs, ours), "content.otio differs from the reference's beyond the web clip");

        Assert.Equal("MissingReference.1", (string?)ourMissing!["OTIO_SCHEMA"]);
        Assert.Equal((string?)theirMissing!["metadata"]!["missing_reference_because"], (string?)ourMissing["metadata"]!["missing_reference_because"]);
        Assert.Equal("https://example.com/c.mov", (string?)ourMissing["metadata"]!["original_target_url"]);
        Assert.Equal("prores", (string?)ourMissing["metadata"]!["probe"]!["codec"]);
        Assert.NotNull(ourMissing["available_range"]);
    }

    [Fact]
    public void AnOtiodIsAFolderWithTheSameLayoutAndOpensBack()
    {
        string path = Path.Combine(_folder, "out.otiod");

        OtioBundle.Write(Source(), path, MediaReferencePolicy.MissingIfNotFile);
        OtioBundle bundle = OtioBundle.Open(path, Cache);

        Assert.Equal("1.0.0", File.ReadAllText(Path.Combine(path, "version.txt")));
        Assert.Equal(Path.Combine(path, "media", "shot-a.mov"), bundle.MediaPath("media/shot-a.mov"));
        Assert.Equal(3, Assert.IsType<Timeline>(bundle.Content).FindClips().Count());
    }

    [Fact]
    public void ThePoliciesDecideWhatHappensToMediaThatIsNotOnDisk()
    {
        BundleException error = Assert.Throws<BundleException>(() => OtioBundle.Write(Source(), Path.Combine(_folder, "e.otioz")));
        Assert.Contains("https://example.com/c.mov", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_folder, "e.otioz")));

        string none = Path.Combine(_folder, "none.otioz");
        OtioBundle.Write(Source(), none, MediaReferencePolicy.AllMissing);
        using ZipArchive zip = ZipFile.OpenRead(none);
        Assert.Equal(["version.txt", "content.otio"], zip.Entries.Select(e => e.FullName));
        Assert.Contains("AllMissing specified as the MediaReferencePolicy", Read(zip, "content.otio"), StringComparison.Ordinal);

        Assert.Equal(27 + 25, OtioBundle.MediaSize(Source(), MediaReferencePolicy.MissingIfNotFile));
    }

    [Fact]
    public void ABundleIsNeverWrittenOverOneThatExistsNorWithTwoMediaOfOneName()
    {
        string path = Path.Combine(_folder, "taken.otioz");
        File.WriteAllText(path, "already here");
        Assert.Throws<BundleException>(() => OtioBundle.Write(Source(), path, MediaReferencePolicy.MissingIfNotFile));
        Assert.Equal("already here", File.ReadAllText(path));

        string other = Path.Combine(_folder, "other");
        Directory.CreateDirectory(other);
        File.Copy(Path.Combine(Fixtures, "media", "shot-a.mov"), Path.Combine(other, "shot-a.mov"));
        string twice = File.ReadAllText(Path.Combine(Fixtures, "source.otio"), Encoding.UTF8)
            .Replace("{MEDIA}/shot-b.wav", FileUrl.FromPath(Path.Combine(other, "shot-a.mov")), StringComparison.Ordinal)
            .Replace("{MEDIA}", FileUrl.FromPath(Path.Combine(Fixtures, "media")), StringComparison.Ordinal);
        BundleException clash = Assert.Throws<BundleException>(() => OtioBundle.Write(OtioReader.ReadString(twice), Path.Combine(_folder, "clash.otioz"), MediaReferencePolicy.MissingIfNotFile));
        Assert.Contains("unique names", clash.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABundleOpenedFromABundleIsBundledAgainWithItsMedia()
    {
        OtioBundle opened = OtioBundle.Open(Path.Combine(Fixtures, "reference.otioz"), Cache);
        string path = Path.Combine(_folder, "again.otioz");

        OtioBundle.Write(opened.Content, path, MediaReferencePolicy.MissingIfNotFile, opened.MediaPath);

        using ZipArchive zip = ZipFile.OpenRead(path);
        Assert.Equal(["version.txt", "content.otio", "media/shot-a.mov", "media/shot-b.wav"], zip.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void WritingABundleLeavesTheTimelineAsItWas()
    {
        ModelObject source = Source();
        string before = OtioWriter.Write(source);

        OtioBundle.Write(source, Path.Combine(_folder, "x.otioz"), MediaReferencePolicy.MissingIfNotFile);

        Assert.Equal(before, OtioWriter.Write(source));
    }

    [Theory]
    [InlineData("file:///D:/footage/a.mov", @"D:\footage\a.mov")]
    [InlineData("media/a.mov", "media/a.mov")]
    [InlineData(@"D:\footage\a.mov", @"D:\footage\a.mov")]
    [InlineData("https://example.com/a.mov", null)]
    public void FileUrlsBecomePathsAndOtherSchemesDoNot(string url, string? path)
    {
        Assert.Equal(path, FileUrl.ToPath(url));
    }

    // The timeline the reference bundled, with this checkout's media folder in place of {MEDIA}.
    private static ModelObject Source()
    {
        string media = FileUrl.FromPath(Path.Combine(Fixtures, "media"));
        return OtioReader.ReadString(File.ReadAllText(Path.Combine(Fixtures, "source.otio"), Encoding.UTF8).Replace("{MEDIA}", media, StringComparison.Ordinal));
    }

    private static JsonNode? TakeWebReference(JsonNode content)
    {
        JsonObject web = content["tracks"]!["children"]![0]!["children"]![2]!.AsObject();
        JsonObject references = web["media_references"]!.AsObject();
        JsonNode? missing = references["DEFAULT_MEDIA"];
        references.Remove("DEFAULT_MEDIA");
        return missing;
    }

    private static string Read(ZipArchive zip, string name) => Encoding.UTF8.GetString(ReadBytes(zip, name));

    private static byte[] ReadBytes(ZipArchive zip, string name)
    {
        using Stream stream = zip.GetEntry(name)!.Open();
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
