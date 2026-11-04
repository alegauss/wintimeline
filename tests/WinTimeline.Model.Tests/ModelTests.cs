using System.IO;
using WinTimeline.Model.Serialization;
using WinTimeline.Model.Time;

namespace WinTimeline.Model.Tests;

/// <summary>WT12: what the model keeps, what it computes rather than stores, and what it announces.</summary>
public sealed class ModelTests
{
    private static Timeline Load(string name) =>
        Assert.IsType<Timeline>(OtioReader.ReadFile(Path.Combine(Repository.Root, "tests", "fixtures", "otio", "corpus", name)));

    [Fact]
    public void ParentsAreComputedFromWhatHoldsEachObject()
    {
        Timeline timeline = Load("nested_example.otio");

        foreach (Clip clip in timeline.FindClips())
        {
            Composition? parent = clip.Parent;
            Assert.NotNull(parent);
            Assert.Contains(clip, parent.Children);
        }

        Assert.Same(timeline, timeline.Tracks.Owner);
    }

    [Fact]
    public void AnOlderClipIsUpgradedAndKeepsItsReferenceUnderTheDefaultKey()
    {
        Timeline timeline = Load("simple_cut.otio"); // written as Clip.1

        Clip clip = timeline.FindClips().First();
        Assert.Equal(Clip.DefaultMedia, clip.ActiveMediaReferenceKey);
        ExternalReference media = Assert.IsType<ExternalReference>(clip.MediaReference);
        Assert.Equal("file:///folder/titles.mov", media.TargetUrl);
        Assert.Same(clip, media.Owner);
    }

    [Fact]
    public void MetadataAndUnknownFieldsAndSchemasAreKept()
    {
        Timeline timeline = Assert.IsType<Timeline>(OtioReader.ReadString("""
            {
              "OTIO_SCHEMA": "Timeline.1", "name": "t", "studio_field": 7,
              "metadata": { "zeta": 1, "alpha": { "nested": [1.5, true, null] } },
              "tracks": { "OTIO_SCHEMA": "Stack.1", "children": [
                { "OTIO_SCHEMA": "Track.1", "kind": "Subtitle", "children": [
                  { "OTIO_SCHEMA": "MyStudio.Note.3", "body": "keep me" }
                ] }
              ] }
            }
            """));

        Assert.Equal(["alpha", "zeta"], timeline.Metadata.Keys);
        Assert.Equal(7L, timeline.DynamicFields["studio_field"]);
        Track track = Assert.IsType<Track>(Assert.Single(timeline.Tracks.Children));
        Assert.Equal("Subtitle", track.Kind);
        UnknownSchema note = Assert.IsType<UnknownSchema>(Assert.Single(track.UnknownChildren));
        Assert.Equal(("MyStudio.Note", 3, "keep me"), (note.Schema, note.Version, note.Data["body"]));
    }

    [Fact]
    public void AChangeIsAnnouncedOnceByTheTimelineNamingTheObjectThatChanged()
    {
        Timeline timeline = Load("simple_cut.otio");
        List<ModelChange> changes = [];
        timeline.ModelChanged += (_, change) => changes.Add(change);
        Clip clip = timeline.FindClips().First();
        Track track = Assert.IsType<Track>(clip.Parent);

        clip.SetSourceRange(new TimeRange(new RationalTime(0, 24), new RationalTime(2, 24)));
        track.RemoveAt(1);

        Assert.Equal([new ModelChange(clip, "source_range"), new ModelChange(track, "children")], changes);
        Assert.Equal(new RationalTime(2, 24), clip.Duration);
    }

    [Fact]
    public void ACorruptFileIsRefusedAsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => OtioReader.ReadString("{ \"OTIO_SCHEMA\": \"Timeline.1\", "));
        Assert.Throws<InvalidDataException>(() => OtioReader.ReadString("[1, 2]"));
    }

    [Fact]
    public void AnImageSequenceNamesItsFrames()
    {
        ImageSequenceReference sequence = Assert.IsType<ImageSequenceReference>(OtioReader.ReadString("""
            { "OTIO_SCHEMA": "ImageSequenceReference.1", "target_url_base": "file:///shots/", "name_prefix": "sh010.",
              "name_suffix": ".exr", "start_frame": 1001, "frame_step": 1, "rate": 24.0, "frame_zero_padding": 4,
              "missing_frame_policy": "hold" }
            """));

        Assert.Equal("file:///shots/sh010.1001.exr", sequence.TargetUrlForImageNumber(0));
        Assert.Equal("file:///shots/sh010.1011.exr", sequence.TargetUrlForImageNumber(10));
        Assert.Equal(MissingFramePolicy.Hold, sequence.MissingFramePolicy);
    }
}
