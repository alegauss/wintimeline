using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinTimeline.App;

/// <summary>
/// What the person chose that outlives a run (WT5), kept in settings.json in the data folder.
/// Immutable: a change is a new record handed to <see cref="SettingsStore.Update"/>.
/// </summary>
/// <param name="Version">The schema version the file was written in.</param>
/// <param name="Adapter">The GPU to run on, as <c>--adapter</c> spells it, or null for the high-performance one.</param>
/// <param name="ModelsFolder">Where AI models live, or null for the default.</param>
/// <param name="CacheFolder">Where proxies and render caches go, or null for the default.</param>
/// <param name="RecentFiles">The documents opened or saved last, most recent first (WT68), or null for none yet.</param>
public sealed record AppSettings(
    int Version = AppSettings.CurrentVersion,
    string? Adapter = null,
    string? ModelsFolder = null,
    string? CacheFolder = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? RecentFiles = null)
{
    /// <summary>The schema version this build writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>How many recent files are kept.</summary>
    public const int RecentLimit = 10;

    /// <summary>These settings with a document put first among the recent files, once, the oldest dropped past the limit.</summary>
    /// <param name="path">The document's full path.</param>
    /// <returns>The new settings.</returns>
    public AppSettings WithRecent(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        IEnumerable<string> rest = (RecentFiles ?? []).Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        string[] next = [path, .. rest.Take(RecentLimit - 1)];
        return RecentFiles is { } now && now.SequenceEqual(next, StringComparer.Ordinal) ? this : this with { RecentFiles = next };
    }

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        WriteIndented = true,
    };
}

/// <summary>How a load went, so the window can say so.</summary>
public enum SettingsOrigin
{
    /// <summary>No file yet: the defaults.</summary>
    Defaults,

    /// <summary>Read from the file.</summary>
    File,

    /// <summary>The file did not read; it was kept as settings.json.broken and the defaults are in use.</summary>
    Damaged,

    /// <summary>Written by a newer WinTimeline: read for what this build knows, and never overwritten.</summary>
    Newer,
}

/// <summary>
/// The settings file and the live settings (WT5). A file that is missing, damaged or unreadable
/// never stops a start: it is the defaults, and a damaged one is kept beside as .broken for
/// inspection. Writes replace the file whole, so a crash never leaves half of it. Panels listen
/// to <see cref="Changed"/> to follow a change at once.
/// </summary>
public sealed class SettingsStore
{
    private readonly Lock _lock = new();

    private SettingsStore(string path, AppSettings current, SettingsOrigin origin)
    {
        Path = path;
        Current = current;
        Origin = origin;
    }

    /// <summary>Raised after <see cref="Current"/> changes, with the new settings.</summary>
    public event EventHandler<AppSettings>? Changed;

    /// <summary>The file.</summary>
    public string Path { get; }

    /// <summary>The settings in force.</summary>
    public AppSettings Current { get; private set; }

    /// <summary>How the file read at start.</summary>
    public SettingsOrigin Origin { get; }

    /// <summary>
    /// The data folder: <c>WINTIMELINE_DATA</c> when set (the UI cases and a portable install),
    /// else WinTimeline under <c>LOCALAPPDATA</c>, the variable before the known folder so a test
    /// can move it.
    /// </summary>
    public static string DataFolder
    {
        get
        {
            if (Environment.GetEnvironmentVariable("WINTIMELINE_DATA") is { Length: > 0 } data)
            {
                return data;
            }

            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } variable
                ? variable
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(local, "WinTimeline");
        }
    }

    /// <summary>settings.json in <see cref="DataFolder"/>.</summary>
    public static string DefaultPath => System.IO.Path.Combine(DataFolder, "settings.json");

    /// <summary>What the status bar says about a load that did not go plainly, or null.</summary>
    public string? Notice => Origin switch
    {
        SettingsOrigin.Damaged => UiText.SettingsDamaged(System.IO.Path.GetFileName(Path)),
        SettingsOrigin.Newer => UiText.SettingsNewer,
        _ => null,
    };

    /// <summary>Reads the settings file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The store.</returns>
    public static SettingsStore Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            return new SettingsStore(path, new AppSettings(), SettingsOrigin.Defaults);
        }

        try
        {
            AppSettings read = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), AppSettings.Json)
                               ?? throw new JsonException("the file holds null");
            return read.Version > AppSettings.CurrentVersion
                ? new SettingsStore(path, read, SettingsOrigin.Newer)
                : new SettingsStore(path, read with { Version = AppSettings.CurrentVersion }, SettingsOrigin.File);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            KeepBroken(path);
            return new SettingsStore(path, new AppSettings(), SettingsOrigin.Damaged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked or unreadable rather than damaged: nothing to keep aside, and the next start tries again.
            return new SettingsStore(path, new AppSettings(), SettingsOrigin.Defaults);
        }
    }

    /// <summary>Applies a change, saves it and raises <see cref="Changed"/>.</summary>
    /// <param name="change">The new settings from the current ones.</param>
    /// <returns>Whether it was saved; a newer file is never overwritten, though the change still applies to this run.</returns>
    public bool Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        AppSettings next;
        bool saved = false;
        lock (_lock)
        {
            next = change(Current);
            if (next == Current)
            {
                return false;
            }

            Current = next;
            if (Origin != SettingsOrigin.Newer)
            {
                Save(Path, next);
                saved = true;
            }
        }

        Changed?.Invoke(this, next);
        return saved;
    }

    private static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, AppSettings.Json));
        File.Move(temporary, path, overwrite: true);
    }

    private static void KeepBroken(string path)
    {
        try
        {
            File.Copy(path, path + ".broken", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keeping the evidence is a courtesy; starting matters more.
        }
    }
}
