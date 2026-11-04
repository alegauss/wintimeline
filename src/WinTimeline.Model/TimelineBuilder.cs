using System.Text.Json.Nodes;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model;

/// <summary>A media file to lay on a new timeline.</summary>
/// <param name="Name">The clip's name, usually the file's.</param>
/// <param name="Url">The media's URL, e.g. file:///D:/footage/A001.mov.</param>
/// <param name="Seconds">How long it plays.</param>
/// <param name="Rate">Its frame rate, or null for sound alone.</param>
/// <param name="HasAudio">Whether it carries sound.</param>
public sealed record MediaClip(string Name, string Url, double Seconds, double? Rate, bool HasAudio);

/// <summary>
/// WT68: new timelines, written in the reference's own schema and read back through
/// <see cref="OtioReader"/>, so a timeline made here is exactly what opening the same JSON makes.
/// </summary>
public static class TimelineBuilder
{
    /// <summary>
    /// A timeline of media files one after another: pictures on V1, sound on A1 beside them, a
    /// gap on V1 under a file of sound alone. Each clip's available range is the whole file.
    /// </summary>
    /// <param name="name">The timeline's name.</param>
    /// <param name="rate">The timeline's rate, which clips without pictures are counted in.</param>
    /// <param name="clips">The files, in order.</param>
    /// <returns>The timeline.</returns>
    public static Timeline FromMedia(string name, double rate, IEnumerable<MediaClip> clips)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(clips);
        JsonArray video = [];
        JsonArray audio = [];
        foreach (MediaClip clip in clips)
        {
            double clipRate = clip.Rate ?? rate;
            double frames = Math.Round(clip.Seconds * clipRate);
            JsonObject range = Range(0, frames, clipRate);
            video.Add(clip.Rate is null ? Gap(Range(0, Math.Round(clip.Seconds * rate), rate)) : Clip(clip, range));
            if (clip.HasAudio)
            {
                audio.Add(Clip(clip, Range(0, frames, clipRate)));
            }
            else
            {
                audio.Add(Gap(Range(0, frames, clipRate)));
            }
        }

        JsonObject timeline = new()
        {
            ["OTIO_SCHEMA"] = "Timeline.1",
            ["name"] = name,
            ["metadata"] = new JsonObject(),
            ["global_start_time"] = Time(0, rate),
            ["tracks"] = new JsonObject
            {
                ["OTIO_SCHEMA"] = "Stack.1",
                ["name"] = "tracks",
                ["children"] = new JsonArray(Track("V1", "Video", video), Track("A1", "Audio", audio)),
            },
        };
        return (Timeline)OtioReader.ReadString(timeline.ToJsonString());
    }

    /// <summary>An empty timeline with one video and one audio track.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="rate">Its rate.</param>
    /// <returns>The timeline.</returns>
    public static Timeline Empty(string name, double rate) => FromMedia(name, rate, []);

    private static JsonObject Track(string name, string kind, JsonArray children) => new()
    {
        ["OTIO_SCHEMA"] = "Track.1",
        ["name"] = name,
        ["kind"] = kind,
        ["children"] = children,
    };

    private static JsonObject Clip(MediaClip clip, JsonObject range) => new()
    {
        ["OTIO_SCHEMA"] = "Clip.2",
        ["name"] = clip.Name,
        ["source_range"] = range.DeepClone(),
        ["media_references"] = new JsonObject
        {
            ["DEFAULT_MEDIA"] = new JsonObject
            {
                ["OTIO_SCHEMA"] = "ExternalReference.1",
                ["name"] = clip.Name,
                ["target_url"] = clip.Url,
                ["available_range"] = range.DeepClone(),
            },
        },
        ["active_media_reference_key"] = "DEFAULT_MEDIA",
    };

    private static JsonObject Gap(JsonObject range) => new()
    {
        ["OTIO_SCHEMA"] = "Gap.1",
        ["name"] = string.Empty,
        ["source_range"] = range,
    };

    private static JsonObject Range(double start, double duration, double rate) => new()
    {
        ["OTIO_SCHEMA"] = "TimeRange.1",
        ["start_time"] = Time(start, rate),
        ["duration"] = Time(duration, rate),
    };

    private static JsonObject Time(double value, double rate) => new()
    {
        ["OTIO_SCHEMA"] = "RationalTime.1",
        ["value"] = value,
        ["rate"] = rate,
    };
}
