using System.IO;
using System.Reflection;
using Vortice.Direct3D11;

namespace WinTimeline.Render;

/// <summary>
/// WT150: the shaders compiled at build time (Shaders\*.hlsl, embedded as .cso), loaded by
/// name. Nothing is compiled at start, so start-up pays nothing and a typo fails the build. In
/// Debug, with the source tree present, a changed .hlsl is recompiled while the app runs and
/// <see cref="Changed"/> says which, so a shader is tuned without a restart.
/// </summary>
public sealed class ShaderLibrary : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, byte[]> _overrides = new(StringComparer.Ordinal);
    private FileSystemWatcher? _watcher;

    /// <summary>Raised, on a worker thread, with the name of a shader recompiled from its changed source.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>The names of every shader built in, e.g. fill.cs.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [.. typeof(ShaderLibrary).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("WinTimeline.Render.Shaders.", StringComparison.Ordinal) && n.EndsWith(".cso", StringComparison.Ordinal))
            .Select(n => n["WinTimeline.Render.Shaders.".Length..^".cso".Length])
            .Order(StringComparer.Ordinal)];

    /// <summary>The source folder Debug builds watch, or null in Release or when it is not on this disk.</summary>
    public static string? SourceFolder =>
        typeof(ShaderLibrary).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ShaderSourceFolder")?.Value is { } folder && Directory.Exists(folder) ? folder : null;

    /// <summary>A shader's bytecode: the recompiled source when Debug has one, else the built-in.</summary>
    /// <param name="name">e.g. fill.cs.</param>
    /// <returns>The bytecode.</returns>
    /// <exception cref="KeyNotFoundException">No shader has that name.</exception>
    public ReadOnlyMemory<byte> Bytecode(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_gate)
        {
            if (_overrides.TryGetValue(name, out byte[]? fresh))
            {
                return fresh;
            }
        }

        using Stream stream = typeof(ShaderLibrary).Assembly.GetManifestResourceStream($"WinTimeline.Render.Shaders.{name}.cso")
            ?? throw new KeyNotFoundException($"no shader is named {name}; there are {string.Join(", ", Names)}");
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>A compute shader on a device.</summary>
    /// <param name="gpu">The device.</param>
    /// <param name="name">e.g. fill.cs.</param>
    /// <returns>The shader; the caller disposes it.</returns>
    public ID3D11ComputeShader Compute(GpuContext gpu, string name)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        return gpu.Device.CreateComputeShader(Bytecode(name).Span);
    }

    /// <summary>Starts recompiling changed sources, in Debug builds whose source folder is on this disk.</summary>
    /// <returns>Whether it is watching.</returns>
    public bool Watch()
    {
        if (SourceFolder is not { } folder || _watcher is not null)
        {
            return false;
        }

        _watcher = new FileSystemWatcher(folder, "*.hlsl") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        _watcher.Changed += (_, e) => Recompile(e.FullPath);
        _watcher.Renamed += (_, e) => Recompile(e.FullPath);
        _watcher.EnableRaisingEvents = true;
        return true;
    }

    /// <summary>Recompiles one source now and, when it compiles, serves it in place of the built-in.</summary>
    /// <param name="path">The .hlsl file.</param>
    /// <returns>The compiler's errors, or null when it compiled.</returns>
    public string? Recompile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string name = Path.GetFileNameWithoutExtension(path);
        string profile = Path.GetExtension(name) switch { ".vs" => "vs_5_0", ".ps" => "ps_5_0", _ => "cs_5_0" };
        string source;
        try
        {
            source = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            return ex.Message;
        }

        Vortice.D3DCompiler.Compiler.Compile(source, [], null!, "main", path, profile,
            Vortice.D3DCompiler.ShaderFlags.OptimizationLevel3, out Vortice.Direct3D.Blob code, out Vortice.Direct3D.Blob? errors);
        using (errors)
        using (code)
        {
            if (code is null)
            {
                return errors is null ? $"{name} did not compile" : System.Text.Encoding.UTF8.GetString(errors.AsSpan()).Trim('\0', ' ', '\r', '\n');
            }

            lock (_gate)
            {
                _overrides[name] = code.AsSpan().ToArray();
            }
        }

        Changed?.Invoke(this, name);
        return null;
    }

    /// <inheritdoc/>
    public void Dispose() => _watcher?.Dispose();
}
