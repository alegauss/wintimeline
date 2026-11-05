using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace WinTimeline.Inference.Native;

/// <summary>
/// WT166: a model loaded by a native engine in a process of its own, WinTimeline.Inference.Host,
/// so a crash in llama.cpp or whisper.cpp ends that process and never the editor. Ported from
/// Revelei's GenerationClient (PB78). It is an <see cref="IDisposable"/> session like an ONNX one,
/// so the same <see cref="SessionCache{TSession}"/> loads, counts and unloads it; unloading ends
/// the process, which hands its memory back. One run at a time. A cancel asks the engine to stop
/// and, if it has not within <see cref="CancelGrace"/>, ends the process tree. A host that crashed
/// or was ended is started again, and the model loaded again, by the next run.
/// </summary>
public sealed class NativeHostSession : IDisposable
{
    private readonly string _hostPath;
    private readonly IReadOnlyList<string> _arguments;
    private readonly string _model;
    private readonly TimeSpan _loadTimeout;
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly Queue<string> _errorTail = new();
    private Process? _host;
    private Channel<HostMessage?>? _inbox;
    private int _run;
    private int _current;
    private bool _disposed;

    /// <summary>Starts the host and loads the model in it.</summary>
    /// <param name="hostPath">WinTimeline.Inference.Host.exe.</param>
    /// <param name="arguments">Its arguments, e.g. --engine llama.cpp --engines &lt;folder&gt;, or --fake in tests.</param>
    /// <param name="model">The model file the engine opens.</param>
    /// <param name="loadTimeout">How long loading may take; a minute when null.</param>
    /// <exception cref="NativeEngineException">The host did not start, or the engine could not load the model.</exception>
    public NativeHostSession(string hostPath, IReadOnlyList<string> arguments, string model, TimeSpan? loadTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(hostPath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(model);
        _hostPath = hostPath;
        _arguments = arguments;
        _model = model;
        _loadTimeout = loadTimeout ?? TimeSpan.FromMinutes(1);
        try
        {
            StartAndLoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>How long a cancel may take before the host is ended.</summary>
    public TimeSpan CancelGrace { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The host's process id while it runs, or null.</summary>
    public int? ProcessId => _host is { HasExited: false } running ? running.Id : null;

    /// <summary>Whether the host process is running now.</summary>
    public bool HostRunning => _host is { HasExited: false };

    /// <summary>Runs the model on a prompt.</summary>
    /// <param name="prompt">What the engine is given.</param>
    /// <param name="tokens">Told each piece of output as it comes.</param>
    /// <param name="cancellation">Stops it: by asking the engine, or by ending the host.</param>
    /// <returns>The whole output.</returns>
    /// <exception cref="OperationCanceledException">It was cancelled.</exception>
    /// <exception cref="NativeEngineException">The engine failed, or its host stopped.</exception>
    public async Task<string> RunAsync(string prompt, IProgress<string>? tokens, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _one.WaitAsync(cancellation).ConfigureAwait(false);
        int run = Interlocked.Increment(ref _run);
        _current = run;
        try
        {
            if (!HostRunning)
            {
                await StartAndLoadAsync(cancellation).ConfigureAwait(false);
            }

            (Process host, Channel<HostMessage?> inbox) = (_host!, _inbox!);
            await SendAsync(host, new HostMessage("run", Prompt: prompt)).ConfigureAwait(false);
            using CancellationTokenRegistration asked = cancellation.Register(() => _ = CancelAsync(host, run));
            StringBuilder output = new();
            while (true)
            {
                switch (await inbox.Reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    case null:
                        cancellation.ThrowIfCancellationRequested();
                        throw new NativeEngineException($"The native engine stopped. {Tail()}".TrimEnd());
                    case { Type: "token", Text: { } piece }:
                        output.Append(piece);
                        tokens?.Report(piece);
                        break;
                    case { Type: "done" } done:
                        return done.Text ?? output.ToString();
                    case { Type: "failed", Cancelled: true }:
                        throw new OperationCanceledException(cancellation);
                    case { Type: "failed" } failed:
                        cancellation.ThrowIfCancellationRequested();
                        throw new NativeEngineException(failed.Message ?? "The native engine failed without saying why.");
                }
            }
        }
        finally
        {
            _current = 0;
            _one.Release();
        }
    }

    /// <summary>Ends the host, which unloads the model and frees what it held.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _one.Dispose();
    }

    private async Task StartAndLoadAsync(CancellationToken cancellation)
    {
        Stop();
        lock (_errorTail)
        {
            _errorTail.Clear();
        }

        ProcessStartInfo start = new(_hostPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (string argument in _arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process host;
        try
        {
            host = Process.Start(start) ?? throw new NativeEngineException($"{Path.GetFileName(_hostPath)} did not start.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new NativeEngineException($"{Path.GetFileName(_hostPath)} could not be started: {ex.Message}", ex);
        }

        Channel<HostMessage?> inbox = Channel.CreateUnbounded<HostMessage?>();
        _ = Task.Run(async () =>
        {
            while (await host.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (HostMessage.Parse(line) is { } message)
                {
                    await inbox.Writer.WriteAsync(message).ConfigureAwait(false);
                }
            }

            await host.WaitForExitAsync().ConfigureAwait(false);
            inbox.Writer.TryWrite(null);
        });
        host.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 } line)
            {
                lock (_errorTail)
                {
                    _errorTail.Enqueue(line);
                    while (_errorTail.Count > 8)
                    {
                        _errorTail.Dequeue();
                    }
                }
            }
        };
        host.BeginErrorReadLine();
        _host = host;
        _inbox = inbox;

        await SendAsync(host, new HostMessage("load", Model: _model)).ConfigureAwait(false);
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(_loadTimeout);
        try
        {
            switch (await inbox.Reader.ReadAsync(limit.Token).ConfigureAwait(false))
            {
                case { Type: "loaded" }:
                    return;
                case { Type: "failed" } failed:
                    Stop();
                    throw new NativeEngineException(failed.Message ?? $"The native engine could not load {Path.GetFileName(_model)}.");
                default:
                    Stop();
                    throw new NativeEngineException($"The native engine stopped while loading {Path.GetFileName(_model)}. {Tail()}".TrimEnd());
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            Stop();
            throw new NativeEngineException($"The native engine took longer than {_loadTimeout.TotalSeconds:0} s to load {Path.GetFileName(_model)}.");
        }
    }

    private static async Task SendAsync(Process host, HostMessage message)
    {
        try
        {
            await host.StandardInput.WriteLineAsync(message.ToLine()).ConfigureAwait(false);
            await host.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The host already stopped; the reader says so.
        }
    }

    // Asks the engine to stop, then ends the host if that run is still going after the grace.
    private async Task CancelAsync(Process host, int run)
    {
        await SendAsync(host, new HostMessage("cancel")).ConfigureAwait(false);
        await Task.Delay(CancelGrace).ConfigureAwait(false);
        if (_current == run)
        {
            Kill(host);
        }
    }

    private void Stop()
    {
        if (_host is not { } host)
        {
            return;
        }

        _host = null;
        try
        {
            if (!host.HasExited)
            {
                host.StandardInput.Close();
                if (!host.WaitForExit(TimeSpan.FromSeconds(5)))
                {
                    Kill(host);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Kill(host);
        }

        host.Dispose();
    }

    private static void Kill(Process host)
    {
        try
        {
            host.Kill(entireProcessTree: true);
            host.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It ended on its own.
        }
    }

    private string Tail()
    {
        lock (_errorTail)
        {
            return string.Join(" ", _errorTail);
        }
    }
}
