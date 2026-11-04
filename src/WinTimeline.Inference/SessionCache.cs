using Microsoft.ML.OnnxRuntime;

namespace WinTimeline.Inference;

/// <summary>A model and the variant of it to run, e.g. transnetv2 / fp16.</summary>
/// <param name="Model">The model's name.</param>
/// <param name="Variant">fp32, fp16, int8, or a GGUF quantisation such as q4_k_m.</param>
public readonly record struct ModelKey(string Model, string Variant);

/// <summary>A session borrowed from the cache; it is not unloaded while a lease is held.</summary>
/// <typeparam name="TSession">The session type.</typeparam>
public sealed class SessionLease<TSession> : IDisposable
    where TSession : class, IDisposable
{
    private readonly SessionCache<TSession> _owner;
    private readonly SessionCache<TSession>.Entry _entry;
    private int _returned;

    internal SessionLease(SessionCache<TSession> owner, SessionCache<TSession>.Entry entry)
    {
        _owner = owner;
        _entry = entry;
    }

    /// <summary>The session.</summary>
    public TSession Session => _entry.Session;

    /// <summary>Gives the session back to the cache.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _returned, 1) == 0)
        {
            _owner.Return(_entry);
        }
    }
}

/// <summary>
/// WT99: every model session the app holds, whatever engine runs it (an ONNX session in process,
/// a llama.cpp host process), under one memory budget. A session loads the first time its model
/// is used and stays; when the resident estimate passes the budget the least recently used idle
/// sessions are unloaded first, so models never take the VRAM the frame cache and the decoders
/// need. An idle session also goes after <see cref="Idle"/>. Loading one variant of a model drops
/// its other idle variants. Ported from Revelei's PB16 cache, with the budget added.
/// </summary>
/// <typeparam name="TSession">The session type.</typeparam>
public sealed class SessionCache<TSession> : IDisposable
    where TSession : class, IDisposable
{
    private readonly Func<ModelKey, TSession> _load;
    private readonly Func<ModelKey, long> _estimate;
    private readonly TimeProvider _time;
    private readonly Dictionary<ModelKey, Entry> _entries = [];
    private readonly Lock _gate = new();
    private readonly ITimer _sweeper;
    private bool _disposed;

    /// <summary>Creates the cache and its idle sweep.</summary>
    /// <param name="load">Loads a session; called outside the cache's lock.</param>
    /// <param name="estimate">How many bytes of video memory a session takes when loaded.</param>
    /// <param name="budget">How many bytes the sessions may take together.</param>
    /// <param name="idle">How long an unused session is kept.</param>
    /// <param name="time">The clock; the system's in the app.</param>
    public SessionCache(Func<ModelKey, TSession> load, Func<ModelKey, long> estimate, long budget, TimeSpan idle, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idle, TimeSpan.Zero);
        _load = load;
        _estimate = estimate;
        Budget = budget;
        Idle = idle;
        _time = time ?? TimeProvider.System;
        TimeSpan period = idle < TimeSpan.FromMinutes(1) ? idle : TimeSpan.FromMinutes(1);
        _sweeper = _time.CreateTimer(_ => Sweep(), null, period, period);
    }

    /// <summary>Raised, outside the lock, with each key whose session was unloaded and why.</summary>
    public event EventHandler<(ModelKey Key, string Reason)>? Unloaded;

    /// <summary>How many bytes the sessions may take together.</summary>
    public long Budget { get; }

    /// <summary>How long an unused session is kept.</summary>
    public TimeSpan Idle { get; }

    /// <summary>The keys whose sessions are loaded, least recently used first.</summary>
    public IReadOnlyList<ModelKey> Loaded
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Values.Where(e => e.Loaded is not null).OrderBy(e => e.LastUsed).Select(e => e.Key)];
            }
        }
    }

    /// <summary>The estimated bytes the loaded sessions take.</summary>
    public long Resident
    {
        get
        {
            lock (_gate)
            {
                return _entries.Values.Where(e => e.Loaded is not null).Sum(e => e.Bytes);
            }
        }
    }

    /// <summary>Borrows the session for a model and variant, loading it, and unloading others, as needed.</summary>
    /// <param name="key">The model and variant.</param>
    /// <returns>A lease to dispose when the run is done.</returns>
    /// <exception cref="InsufficientMemoryException">The session alone is larger than the budget.</exception>
    public SessionLease<TSession> Acquire(ModelKey key)
    {
        long bytes = _estimate(key);
        if (bytes > Budget)
        {
            throw new InsufficientMemoryException($"{key.Model} ({key.Variant}) needs {bytes / 1048576} MB and models may take {Budget / 1048576} MB");
        }

        List<(Entry Entry, string Reason)> dropped = [];
        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry(key, bytes);
                _entries[key] = entry;
            }

            entry.Leases++;
            entry.LastUsed = _time.GetUtcNow();
            foreach (Entry other in _entries.Values.Where(e => e.Key.Model == key.Model && e.Key != key && e.Leases == 0).ToList())
            {
                Remove(other, "another variant of the model was loaded", dropped);
            }

            // Least recently used first, never one in use, until this one fits.
            long resident = _entries.Values.Where(e => e.Loaded is not null && e != entry).Sum(e => e.Bytes);
            foreach (Entry victim in _entries.Values.Where(e => e != entry && e.Leases == 0 && e.Loaded is not null).OrderBy(e => e.LastUsed).ToList())
            {
                if (resident + (entry.Loaded is null ? bytes : 0) <= Budget)
                {
                    break;
                }

                resident -= victim.Bytes;
                Remove(victim, "the model memory budget was reached", dropped);
            }
        }

        Release(dropped);
        try
        {
            entry.EnsureLoaded(_load);
        }
        catch
        {
            lock (_gate)
            {
                entry.Leases--;
                if (entry.Leases == 0 && entry.Loaded is null)
                {
                    _entries.Remove(key);
                }
            }

            throw;
        }

        return new SessionLease<TSession>(this, entry);
    }

    /// <summary>Unloads every session idle for longer than <see cref="Idle"/>; the timer calls it.</summary>
    /// <returns>How many were unloaded.</returns>
    public int Sweep()
    {
        List<(Entry, string)> released = [];
        lock (_gate)
        {
            if (_disposed)
            {
                return 0;
            }

            DateTimeOffset now = _time.GetUtcNow();
            foreach (Entry entry in _entries.Values.Where(e => e.Leases == 0 && now - e.LastUsed >= Idle).ToList())
            {
                Remove(entry, "it was idle", released);
            }
        }

        Release(released);
        return released.Count;
    }

    /// <summary>Unloads every idle session now, as a memory-pressure notice asks.</summary>
    /// <returns>How many were unloaded.</returns>
    public int UnloadIdle()
    {
        List<(Entry, string)> released = [];
        lock (_gate)
        {
            foreach (Entry entry in _entries.Values.Where(e => e.Leases == 0).ToList())
            {
                Remove(entry, "memory was needed", released);
            }
        }

        Release(released);
        return released.Count;
    }

    /// <summary>Stops the sweep and unloads every session.</summary>
    public void Dispose()
    {
        List<TSession> all;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            all = [.. _entries.Values.Select(e => e.Loaded).OfType<TSession>()];
            _entries.Clear();
        }

        _sweeper.Dispose();
        foreach (TSession session in all)
        {
            session.Dispose();
        }
    }

    internal void Return(Entry entry)
    {
        lock (_gate)
        {
            entry.Leases--;
            entry.LastUsed = _time.GetUtcNow();
        }
    }

    private void Remove(Entry entry, string reason, List<(Entry, string)> into)
    {
        _entries.Remove(entry.Key);
        if (entry.Loaded is not null)
        {
            into.Add((entry, reason));
        }
    }

    private void Release(List<(Entry Entry, string Reason)> released)
    {
        foreach ((Entry entry, string reason) in released)
        {
            entry.Loaded?.Dispose();
            Unloaded?.Invoke(this, (entry.Key, reason));
        }
    }

    /// <summary>One cached session and who is using it.</summary>
    internal sealed class Entry(ModelKey key, long bytes)
    {
        private readonly Lock _loading = new();

        public ModelKey Key { get; } = key;

        public long Bytes { get; } = bytes;

        public int Leases { get; set; }

        public DateTimeOffset LastUsed { get; set; }

        public TSession? Loaded { get; private set; }

        public TSession Session => Loaded ?? throw new InvalidOperationException($"{Key} is not loaded.");

        public void EnsureLoaded(Func<ModelKey, TSession> load)
        {
            lock (_loading)
            {
                Loaded ??= load(Key);
            }
        }
    }
}

/// <summary>The app's ONNX sessions: models from a folder, on the detected provider.</summary>
public static class OnnxSessions
{
    /// <summary>
    /// A cache loading &lt;root&gt;/&lt;model&gt;/&lt;variant&gt;.onnx on the selected provider. A
    /// session's VRAM is estimated as twice its file, weights plus activations, until the model
    /// manager (WT100) records measured sizes.
    /// </summary>
    /// <param name="root">The models folder.</param>
    /// <param name="provider">What detection chose.</param>
    /// <param name="budget">The bytes models may take together.</param>
    /// <param name="idle">How long an unused session is kept.</param>
    /// <returns>The cache; the caller disposes it.</returns>
    public static SessionCache<InferenceSession> Create(string root, ProviderSelection provider, long budget, TimeSpan idle)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(provider);
        string File(ModelKey key) => Path.Combine(root, key.Model, key.Variant + ".onnx");
        return new SessionCache<InferenceSession>(
            key =>
            {
                using SessionOptions options = SessionOptionsFactory.Create(provider);
                return new InferenceSession(File(key), options);
            },
            key => new FileInfo(File(key)) is { Exists: true } file ? file.Length * 2 : 0,
            budget,
            idle);
    }
}
