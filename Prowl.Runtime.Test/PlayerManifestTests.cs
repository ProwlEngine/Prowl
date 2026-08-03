// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// The manifest is the entire contract between a build and the player it produced. A field that fails to
/// round-trip does not throw, it silently becomes a default, which shows up as a game that starts and
/// then does nothing.
/// </summary>
public class PlayerManifestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "prowl-manifest", Guid.NewGuid().ToString("N"));

    public PlayerManifestTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void RoundTrips_EveryField()
    {
        Guid scene = Guid.NewGuid();

        var written = new PlayerManifest
        {
            ProductName = "Test Game",
            CompanyName = "Test Co",
            Version = "1.2.3",
            TargetId = "windows-x64",
            BuildDateUtcTicks = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc).Ticks,
            DefaultScene = scene.ToString(),
            AssemblyLoadOrder = ["Game.Core", "Game.Gameplay"],
            Packaging = AssetPackagingMode.ProwlPak,
            WindowWidth = 1600,
            WindowHeight = 900,
            TargetExtras = { ["android.orientation"] = "landscape" },
        };

        written.Save(_dir);
        var read = PlayerManifest.Load(_dir);

        Assert.Equal("Test Game", read.ProductName);
        Assert.Equal("Test Co", read.CompanyName);
        Assert.Equal("1.2.3", read.Version);
        Assert.Equal("windows-x64", read.TargetId);
        Assert.Equal(written.BuildDateUtcTicks, read.BuildDateUtcTicks);
        Assert.Equal(scene, read.DefaultSceneGuid);
        Assert.Equal(["Game.Core", "Game.Gameplay"], read.AssemblyLoadOrder);
        Assert.Equal(AssetPackagingMode.ProwlPak, read.Packaging);
        Assert.Equal(1600, read.WindowWidth);
        Assert.Equal(900, read.WindowHeight);
        Assert.Equal("landscape", read.TargetExtras["android.orientation"]);
    }

    // A player with no manifest beside it has to start on defaults rather than throw.
    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var read = PlayerManifest.Load(_dir);

        Assert.Equal("Prowl Game", read.ProductName);
        Assert.Empty(read.AssemblyLoadOrder);
        Assert.Equal(Guid.Empty, read.DefaultSceneGuid);
    }

    // A manifest the player cannot read must be loud, not silently become defaults.
    [Fact]
    public void FormatVersion_RoundTripsAndDefaultsToCurrent()
    {
        var written = new PlayerManifest { ProductName = "Versioned" };
        Assert.Equal(PlayerManifest.CurrentFormatVersion, written.FormatVersion);

        written.Save(_dir);
        Assert.Equal(PlayerManifest.CurrentFormatVersion, PlayerManifest.Load(_dir).FormatVersion);
    }
}