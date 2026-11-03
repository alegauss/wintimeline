using System.IO;

namespace WinTimeline.App.Tests;

/// <summary>WT5: settings round-trip, and no state of the file stops a start.</summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wt5-" + Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void NoFileIsTheDefaultsWithNothingToSay()
    {
        SettingsStore store = SettingsStore.Load(File_);

        Assert.Equal(new AppSettings(), store.Current);
        Assert.Equal(SettingsOrigin.Defaults, store.Origin);
        Assert.Null(store.Notice);
    }

    [Fact]
    public void AChangeIsSavedReadBackAndAnnounced()
    {
        SettingsStore store = SettingsStore.Load(File_);
        AppSettings? announced = null;
        store.Changed += (_, settings) => announced = settings;

        Assert.True(store.Update(s => s with { Adapter = "NVIDIA", CacheFolder = @"D:\cache" }));

        AppSettings expected = new(Adapter: "NVIDIA", CacheFolder: @"D:\cache");
        Assert.Equal(expected, announced);
        Assert.Equal(expected, SettingsStore.Load(File_).Current);
        Assert.Equal(SettingsOrigin.File, SettingsStore.Load(File_).Origin);
        Assert.False(File.Exists(File_ + ".tmp"));
    }

    [Fact]
    public void AnUnchangedUpdateWritesNothing()
    {
        SettingsStore store = SettingsStore.Load(File_);

        Assert.False(store.Update(s => s));
        Assert.False(File.Exists(File_));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("{ \"adapter\": 42 }")]
    public void ADamagedFileIsTheDefaultsAndIsKeptAsBroken(string damaged)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(File_, damaged);

        SettingsStore store = SettingsStore.Load(File_);

        Assert.Equal(new AppSettings(), store.Current);
        Assert.Equal(SettingsOrigin.Damaged, store.Origin);
        Assert.Equal(damaged, File.ReadAllText(File_ + ".broken"));
        Assert.Equal("Settings could not be read; defaults are in use. The file was kept as settings.json.broken.", store.Notice);
    }

    [Fact]
    public void ANewerFileIsReadButNeverOverwritten()
    {
        Directory.CreateDirectory(_folder);
        const string Newer = "{ \"version\": 9, \"adapter\": \"Intel\", \"timeline_zoom\": 3 }";
        File.WriteAllText(File_, Newer);

        SettingsStore store = SettingsStore.Load(File_);
        bool saved = store.Update(s => s with { Adapter = "NVIDIA" });

        Assert.Equal(SettingsOrigin.Newer, store.Origin);
        Assert.False(saved);
        Assert.Equal("NVIDIA", store.Current.Adapter);
        Assert.Equal(Newer, File.ReadAllText(File_));
    }

    [Fact]
    public void UnknownFieldsFromAnOlderOrSiblingBuildAreSkipped()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(File_, "{ \"version\": 1, \"adapter\": \"1\", \"retired_option\": true }");

        Assert.Equal(new AppSettings(Adapter: "1"), SettingsStore.Load(File_).Current);
    }
}
