using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WinTimeline.Model.Serialization;

namespace WinTimeline.Model.Adapters;

/// <summary>Thrown when OpenTimelineIO in Python could not convert a file.</summary>
public sealed class PythonBridgeException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PythonBridgeException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    public PythonBridgeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why.</param>
    /// <param name="inner">The cause.</param>
    public PythonBridgeException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>An adapter a Python OpenTimelineIO offers.</summary>
/// <param name="Name">Its name, e.g. AAF.</param>
/// <param name="Suffixes">The extensions it takes, lower case with the dot.</param>
public sealed record PythonAdapter(string Name, IReadOnlyList<string> Suffixes);

/// <summary>
/// WT157: OpenTimelineIO's own Python adapters (AAF, ALE, kdenlive, a studio's plugins), reached
/// out of process. Python never runs inside the app: a child process reads the file with the
/// reference library and writes .otio to its standard output, which <see cref="OtioReader"/>
/// reads. Present only where a Python with opentimelineio exists.
/// </summary>
public sealed class PythonBridge
{
    /// <summary>How long a conversion may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private const string ProbeScript =
        "import json, opentimelineio as otio\n" +
        "print(json.dumps([[a.name] + list(a.suffixes) for a in otio.plugins.ActiveManifest().adapters if a.has_feature('read')]))";

    private const string ConvertScript =
        "import sys, opentimelineio as otio\n" +
        "sys.stdout.reconfigure(encoding='utf-8')\n" +
        "sys.stdout.write(otio.adapters.write_to_string(otio.adapters.read_from_file(sys.argv[1], adapter_name=sys.argv[2]), 'otio_json'))";

    private PythonBridge(string python, IReadOnlyList<PythonAdapter> adapters)
    {
        Python = python;
        Adapters = adapters;
    }

    /// <summary>The Python executable.</summary>
    public string Python { get; }

    /// <summary>The reading adapters it offers.</summary>
    public IReadOnlyList<PythonAdapter> Adapters { get; }

    /// <summary>The bridge to the first Python with OpenTimelineIO: the one named, else python on the PATH.</summary>
    /// <param name="configured">A Python the settings name, or null.</param>
    /// <returns>The bridge, or null when no such Python exists.</returns>
    public static PythonBridge? Find(string? configured) =>
        new[] { configured, "python", "python3" }.OfType<string>().Select(Probe).FirstOrDefault(b => b is not null);

    /// <summary>Asks one Python which adapters its OpenTimelineIO reads with.</summary>
    /// <param name="python">The executable.</param>
    /// <returns>The bridge, or null when it does not run or has no OpenTimelineIO.</returns>
    public static PythonBridge? Probe(string python)
    {
        ArgumentNullException.ThrowIfNull(python);
        try
        {
            (int exit, string output, _) = Run(python, ["-c", ProbeScript], TimeSpan.FromSeconds(30));
            if (exit != 0)
            {
                return null;
            }

            string[][] listed = JsonSerializer.Deserialize<string[][]>(output.Trim()) ?? [];
            return new PythonBridge(python, [.. listed.Select(a => new PythonAdapter(a[0], [.. a.Skip(1).Select(s => "." + s.ToLowerInvariant())]))]);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or JsonException or InvalidOperationException or TimeoutException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// The adapters for every format the native readers do not take, one per Python adapter, to
    /// stand beside them in <see cref="TimelineAdapters"/>.
    /// </summary>
    /// <param name="native">The adapters the app has itself.</param>
    /// <returns>The bridged adapters.</returns>
    public IReadOnlyList<ITimelineAdapter> AdaptersBeside(IEnumerable<ITimelineAdapter> native)
    {
        ArgumentNullException.ThrowIfNull(native);
        HashSet<string> taken = [.. native.SelectMany(a => a.Suffixes)];
        // OpenTimelineIO's own formats (otio_json, otioz, otiod) the app reads natively, bundles included.
        return [.. Adapters
            .Where(a => !a.Name.StartsWith("otio", StringComparison.Ordinal))
            .Select(a => (Adapter: a, Free: a.Suffixes.Where(s => !taken.Contains(s)).ToArray()))
            .Where(a => a.Free.Length > 0)
            .Select(a => (ITimelineAdapter)new Bridged(this, a.Adapter.Name, a.Free))];
    }

    /// <summary>Reads a file through an adapter in the child process.</summary>
    /// <param name="path">The file.</param>
    /// <param name="adapter">The Python adapter's name, e.g. AAF.</param>
    /// <returns>What it read.</returns>
    /// <exception cref="PythonBridgeException">Python could not read it, or took too long.</exception>
    public ModelObject Read(string path, string adapter)
    {
        ArgumentNullException.ThrowIfNull(path);
        (int exit, string output, string errors) = Run(Python, ["-c", ConvertScript, Path.GetFullPath(path), adapter], Timeout);
        if (exit != 0)
        {
            string first = string.Join(" ", errors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(3));
            throw new PythonBridgeException($"OpenTimelineIO's {adapter} adapter could not read {Path.GetFileName(path)}: {first}");
        }

        return OtioReader.ReadString(output);
    }

    private static (int Exit, string Output, string Errors) Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        ProcessStartInfo start = new(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} did not start");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{executable} took longer than {timeout.TotalSeconds:0} s");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), errors.GetAwaiter().GetResult());
    }

    /// <summary>One Python adapter as the app's adapter list shows it.</summary>
    private sealed class Bridged(PythonBridge bridge, string adapter, IReadOnlyList<string> suffixes) : ITimelineAdapter
    {
        public string Name => $"{adapter} (OpenTimelineIO)";

        public IReadOnlyList<string> Suffixes { get; } = suffixes;

        public bool CanRead => true;

        public bool CanWrite => false;

        public ModelObject Read(string path, AdapterOptions options) => bridge.Read(path, adapter);

        public void Write(ModelObject root, string path, AdapterOptions options) =>
            throw new NotSupportedException($"Writing through {adapter} is not supported.");
    }
}
