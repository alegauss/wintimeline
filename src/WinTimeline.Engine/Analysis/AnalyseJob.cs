using System.Text.Json.Nodes;
using WinTimeline.Engine.Jobs;
using WinTimeline.Model.Project;

namespace WinTimeline.Engine.Analysis;

/// <summary>One kind of analysis: what it makes, with which model, and how.</summary>
public interface IAnalyser
{
    /// <summary>What it makes, e.g. transcript.</summary>
    string Kind { get; }

    /// <summary>The catalog id and variant of its model, e.g. whisper-base/base.</summary>
    string Model { get; }

    /// <summary>The model's version; a new one marks older results stale.</summary>
    string Version { get; }

    /// <summary>Analyses one media file.</summary>
    /// <param name="media">The file.</param>
    /// <param name="cancellation">Set when the job is cancelled.</param>
    /// <returns>The result, in the kind's own shape.</returns>
    Task<JsonNode> AnalyseAsync(string media, CancellationToken cancellation);
}

/// <summary>
/// WT104: the Analyse job: runs a set of analyses over media files in the background, through the
/// job scheduler (WT40), skipping every file whose result is already stored by the current model,
/// so reopening a project or analysing a file again runs no model. Visible media go at Visible
/// priority, the rest at Background; a checkpoint after each file makes it resume where it was.
/// </summary>
public static class AnalyseJob
{
    /// <summary>The job kind the scheduler knows it by.</summary>
    public const string Kind = "analyse";

    /// <summary>The request that analyses media.</summary>
    /// <param name="media">The files, in the order to analyse them.</param>
    /// <param name="kinds">The analysis kinds to run.</param>
    /// <param name="visible">Whether the media are on screen.</param>
    /// <returns>The request.</returns>
    public static JobRequest Request(IReadOnlyList<string> media, IReadOnlyList<string> kinds, bool visible)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(kinds);
        return new JobRequest(
            Kind,
            media.Count == 1 ? $"Analyse {Path.GetFileName(media[0])}" : $"Analyse {media.Count} files",
            new JsonObject
            {
                ["media"] = new JsonArray([.. media.Select(m => (JsonNode?)JsonValue.Create(m))]),
                ["kinds"] = new JsonArray([.. kinds.Select(k => (JsonNode?)JsonValue.Create(k))]),
            },
            visible ? JobPriority.Visible : JobPriority.Background,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["gpu-inference"] = 1 },
            MaxAttempts: 2);
    }

    /// <summary>The work the scheduler runs for the Analyse kind.</summary>
    /// <param name="store">Where results are kept.</param>
    /// <param name="analysers">The analyses this build has, by kind.</param>
    /// <param name="ran">Told each time a model actually runs, for the activity log and tests.</param>
    /// <returns>The work.</returns>
    public static JobWork Work(AnalysisStore store, IReadOnlyDictionary<string, IAnalyser> analysers, Action<IAnalyser, string>? ran = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(analysers);
        return async context =>
        {
            string[] media = [.. context.Arguments["media"]!.AsArray().Select(m => m!.GetValue<string>())];
            IAnalyser[] wanted = [.. context.Arguments["kinds"]!.AsArray().Select(k => analysers[k!.GetValue<string>()])];
            for (int i = context.Checkpoint["done"]?.GetValue<int>() ?? 0; i < media.Length; i++)
            {
                string signature = MediaSignature.Of(media[i]);
                foreach (IAnalyser analyser in wanted)
                {
                    if (store.Get(signature, analyser.Kind, analyser.Model, analyser.Version) is { Stale: false })
                    {
                        continue;
                    }

                    JsonNode data = await analyser.AnalyseAsync(media[i], context.Cancellation).ConfigureAwait(false);
                    ran?.Invoke(analyser, media[i]);
                    store.Put(new AnalysisResult(analyser.Kind, signature, analyser.Model, analyser.Version, DateTimeOffset.Now, data));
                }

                context.Report((i + 1) / (double)media.Length);
                context.Save(new JsonObject { ["done"] = i + 1 });
            }
        };
    }
}
