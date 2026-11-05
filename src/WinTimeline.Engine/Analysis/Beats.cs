namespace WinTimeline.Engine.Analysis;

/// <summary>WT121: where the beats of a piece of sound fall, and its tempo.</summary>
/// <param name="Beats">Each beat, in seconds from the sound's start, in order.</param>
/// <param name="Bpm">Beats a minute, from the median gap between them; 0 with fewer than two.</param>
public sealed record BeatAnalysis(IReadOnlyList<double> Beats, double Bpm);

/// <summary>
/// WT121: beats found by onset detection: the sound's energy in one-millisecond frames, an onset
/// wherever it climbs past a threshold set from the loudest and the typical frame, each onset then
/// refined to the first sample past a fifth of its peak. Exact on percussive material (a click
/// track, drums); music whose beats are not struck waits on the neural tracker the design names.
/// </summary>
public static class Beats
{
    /// <summary>The shortest gap two beats can have: 500 beats a minute.</summary>
    private const double Refractory = 0.06;

    /// <summary>Finds the beats of mono samples.</summary>
    /// <param name="samples">The samples, -1 to 1.</param>
    /// <param name="rate">Samples a second.</param>
    /// <returns>The beats and the tempo.</returns>
    public static BeatAnalysis Detect(IReadOnlyList<float> samples, int rate)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (rate <= 0 || samples.Count < rate / 100)
        {
            return new BeatAnalysis([], 0);
        }

        int hop = Math.Max(1, rate / 1000);
        int frames = samples.Count / hop;
        double[] energy = new double[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int i = f * hop; i < (f + 1) * hop; i++)
            {
                sum += samples[i] * samples[i];
            }

            energy[f] = sum / hop;
        }

        double loudest = energy.Max();
        double typical = energy.Order().ElementAt(frames / 2);
        double threshold = Math.Max(loudest * 0.05, typical * 8);
        if (loudest <= 0)
        {
            return new BeatAnalysis([], 0);
        }

        List<double> beats = [];
        double last = double.NegativeInfinity;
        for (int f = 1; f < frames; f++)
        {
            if (energy[f] < threshold || energy[f - 1] >= threshold || (double)f * hop / rate - last < Refractory)
            {
                continue;
            }

            double at = Refine(samples, Math.Max(0, (f - 2) * hop), Math.Min(samples.Count, (f + 20) * hop), rate);
            beats.Add(at);
            last = at;
        }

        double[] gaps = [.. beats.Zip(beats.Skip(1), (a, b) => b - a).Order()];
        double bpm = gaps.Length == 0 ? 0 : 60 / gaps[gaps.Length / 2];
        return new BeatAnalysis(beats, bpm);
    }

    /// <summary>Where an onset starts: the first sample past a fifth of the loudest one around it.</summary>
    private static double Refine(IReadOnlyList<float> samples, int from, int to, int rate)
    {
        float peak = 0;
        for (int i = from; i < to; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }

        for (int i = from; i < to; i++)
        {
            if (Math.Abs(samples[i]) >= peak * 0.2f)
            {
                return (double)i / rate;
            }
        }

        return (double)from / rate;
    }
}
