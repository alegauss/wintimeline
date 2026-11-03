using System.Diagnostics;
using System.Text.Json.Nodes;
using OtioSpike;
using OtioSpike.Port;

// WT11: the OTIO spike's three measurements.
//
//   OtioSpike compare <file>...          fidelity: the port's output against the reference's
//   OtioSpike time native|managed <file>...   load time and memory, one path per process
//
// The reference is the C++ library itself, through otio_shim.dll beside this exe.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: OtioSpike compare <file>... | OtioSpike time native|managed <file>...");
    return 2;
}

return args[0] switch
{
    "compare" => Compare(args[1..]),
    "time" => Time(args[1], args[2..]),
    _ => 2,
};

static int Compare(string[] files)
{
    int failures = 0;
    Console.WriteLine("file | bytes equal | structure equal | clips (ref/port) | duration s (ref/port) | unknown schema kept");
    foreach (string file in files)
    {
        using NativeTimeline native = NativeTimeline.Read(file);
        string reference = native.ToJson();
        OtioObject port = OtioReader.ReadFile(file);
        string mine = OtioWriter.Write(port);

        bool bytes = reference == mine;
        JsonNode? left = JsonNode.Parse(OtioReader.Parseable(reference));
        JsonNode? right = JsonNode.Parse(OtioReader.Parseable(mine));
        long ulps = 0;
        bool structure = JsonNode.DeepEquals(left, right) || Near(left, right, ref ulps);
        string clips = port is Timeline t ? $"{native.CountClips()}/{t.Tracks.FindClips().Count()}" : "-";
        string duration = port is Timeline timeline ? $"{native.DurationSeconds():0.###}/{Seconds(timeline)}" : "-";
        string unknown = UnknownKept(file, reference, mine);
        string equal = ulps == 0 ? structure.ToString() : $"{structure} (numbers within {ulps} ulp)";
        Console.WriteLine($"{Path.GetFileName(file)} | {bytes} | {equal} | {clips} | {duration} | {unknown}");

        if (!structure)
        {
            failures++;
            string dump = Path.Combine(Path.GetTempPath(), "otio-spike-" + Path.GetFileNameWithoutExtension(file));
            File.WriteAllText(dump + ".ref.json", reference);
            File.WriteAllText(dump + ".port.json", mine);
            Console.WriteLine($"  differs: {dump}.ref.json vs {dump}.port.json");
        }
    }

    return failures == 0 ? 0 : 1;
}

// Equal but for doubles a few units in the last place apart; the widest gap is reported.
static bool Near(JsonNode? a, JsonNode? b, ref long ulps)
{
    switch (a, b)
    {
        case (null, null):
            return true;
        case (JsonObject x, JsonObject y):
            if (x.Count != y.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, JsonNode?> pair in x)
            {
                if (!y.TryGetPropertyValue(pair.Key, out JsonNode? other) || !Near(pair.Value, other, ref ulps))
                {
                    return false;
                }
            }

            return true;
        case (JsonArray x, JsonArray y):
            if (x.Count != y.Count)
            {
                return false;
            }

            for (int i = 0; i < x.Count; i++)
            {
                if (!Near(x[i], y[i], ref ulps))
                {
                    return false;
                }
            }

            return true;
        case (JsonValue x, JsonValue y) when x.TryGetValue(out double p) && y.TryGetValue(out double q):
            long gap = Math.Abs(BitConverter.DoubleToInt64Bits(p) - BitConverter.DoubleToInt64Bits(q));
            ulps = Math.Max(ulps, gap);
            return gap <= 4;
        default:
            return JsonNode.DeepEquals(a, b);
    }
}

static string Seconds(Timeline timeline)
{
    try
    {
        return $"{timeline.Duration.Seconds:0.###}";
    }
    catch (InvalidDataException)
    {
        return "error";
    }
}

// Whether every object of a schema neither side knows came back exactly as the file had it.
static string UnknownKept(string file, string reference, string mine)
{
    List<JsonNode> original = Unknown(JsonNode.Parse(OtioReader.Parseable(File.ReadAllText(file))));
    if (original.Count == 0)
    {
        return "-";
    }

    bool Same(string written)
    {
        List<JsonNode> back = Unknown(JsonNode.Parse(OtioReader.Parseable(written)));
        return back.Count == original.Count && original.Zip(back).All(pair => JsonNode.DeepEquals(pair.First, pair.Second));
    }

    return $"ref {Same(reference)}, port {Same(mine)}";
}

static List<JsonNode> Unknown(JsonNode? node)
{
    List<JsonNode> found = [];
    void Walk(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o when o["OTIO_SCHEMA"]?.GetValue<string>().StartsWith("MyStudio.", StringComparison.Ordinal) == true:
                found.Add(o);
                break;
            case JsonObject o:
                foreach (KeyValuePair<string, JsonNode?> pair in o)
                {
                    Walk(pair.Value);
                }

                break;
            case JsonArray a:
                foreach (JsonNode? item in a)
                {
                    Walk(item);
                }

                break;
        }
    }

    Walk(node);
    return found;
}

static int Time(string path, string[] files)
{
    const int Runs = 11;
    Console.WriteLine($"{path}: file | median load ms | min ms | write ms | memory held MB");
    foreach (string file in files)
    {
        Load(path, file); // warm-up: JIT, the DLLs, the file cache
        List<double> loads = [];
        double write = 0;
        for (int run = 0; run < Runs; run++)
        {
            Settle(); // each run starts from a collected heap, so no run pays for the last one's garbage
            Stopwatch watch = Stopwatch.StartNew();
            using IDisposable held = Load(path, file);
            loads.Add(watch.Elapsed.TotalMilliseconds);
            if (run == 0)
            {
                Stopwatch w = Stopwatch.StartNew();
                _ = Write(held);
                write = w.Elapsed.TotalMilliseconds;
            }
        }

        loads.Sort();
        double memory = Held(path, file);
        Console.WriteLine($"{Path.GetFileName(file)} | {loads[Runs / 2]:0.0} | {loads[0]:0.0} | {write:0.0} | {memory:0.0}");
    }

    return 0;
}

// Loads, and walks every clip, so a lazy reader is charged for what an editor would touch.
static IDisposable Load(string path, string file)
{
    if (path == "native")
    {
        NativeTimeline native = NativeTimeline.Read(file);
        _ = native.CountClips();
        return native;
    }

    OtioObject port = OtioReader.ReadFile(file);
    if (port is Timeline timeline)
    {
        _ = timeline.Tracks.FindClips().Count();
    }

    return new Holder(port);
}

static string Write(IDisposable held) => held is NativeTimeline native ? native.ToJson() : OtioWriter.Write(((Holder)held).Value);

// What the loaded file keeps once everything transient is collected: the native heap's growth
// in private bytes, or the managed heap's live bytes (private bytes never shrink back after a GC,
// so they would charge the port for the parse's garbage).
static double Held(string path, string file)
{
    Settle();
    long before = path == "native" ? Process.GetCurrentProcess().PrivateMemorySize64 : GC.GetTotalMemory(true);
    using IDisposable held = Load(path, file);
    Settle();
    long after = path == "native" ? Process.GetCurrentProcess().PrivateMemorySize64 : GC.GetTotalMemory(true);
    GC.KeepAlive(held);
    return (after - before) / (1024.0 * 1024.0);
}

static void Settle()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

internal sealed record Holder(OtioObject Value) : IDisposable
{
    public void Dispose()
    {
    }
}
