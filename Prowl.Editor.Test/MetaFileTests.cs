// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Tests for the pure pieces of the asset-database layer that don't need a live project:
/// <see cref="MetaFile"/> read/write/ensure, sub-asset GUID derivation, and path normalization.
/// </summary>
public class MetaFileTests : IDisposable
{
    private readonly string _dir;

    public MetaFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ProwlMetaTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string AssetPath(string name)
    {
        string p = Path.Combine(_dir, name);
        File.WriteAllText(p, "dummy");
        return p;
    }

    [Fact]
    public void EnsureMeta_CreatesMetaFileWithGuid()
    {
        string asset = AssetPath("thing.scene");

        var meta = MetaFile.EnsureMeta(asset, "SceneImporter", 2);

        Assert.True(File.Exists(asset + ".meta"));
        // Re-read from disk so we verify what was actually written, not just the returned object.
        var onDisk = MetaFile.Read(asset + ".meta");
        Assert.NotEqual(Guid.Empty, onDisk.Guid);
        Assert.Equal(meta.Guid, onDisk.Guid);
        Assert.Equal("SceneImporter", onDisk.ImporterType);
        Assert.Equal(2, onDisk.ImporterVersion);
    }

    [Fact]
    public void EnsureMeta_IsStable_WhenMetaAlreadyExists()
    {
        string asset = AssetPath("thing.scene");

        var first = MetaFile.EnsureMeta(asset, "SceneImporter");
        var second = MetaFile.EnsureMeta(asset, "SceneImporter");

        Assert.Equal(first.Guid, second.Guid); // honors the existing .meta, doesn't mint a new GUID
    }

    [Fact]
    public void ReadWrite_RoundTripsAllFields()
    {
        string metaPath = Path.Combine(_dir, "x.meta");
        var settings = EchoObject.NewCompound();
        settings["foo"] = new EchoObject(42);
        var data = new MetaFileData { Guid = Guid.NewGuid(), ImporterType = "TextureImporter", ImporterVersion = 7, Settings = settings };

        MetaFile.Write(metaPath, data);
        var read = MetaFile.Read(metaPath);

        Assert.Equal(data.Guid, read.Guid);
        Assert.Equal("TextureImporter", read.ImporterType);
        Assert.Equal(7, read.ImporterVersion);
        Assert.NotNull(read.Settings);
        Assert.Equal(42, read.Settings!["foo"].IntValue);
    }

    [Fact]
    public void EnsureMeta_HonorsPreCommittedGuid()
    {
        // Simulates a .meta committed to version control: its GUID must be used verbatim.
        string asset = AssetPath("shared.prefab");
        var fixedGuid = Guid.NewGuid();
        MetaFile.Write(asset + ".meta", new MetaFileData { Guid = fixedGuid, ImporterType = "PrefabImporter", ImporterVersion = 1 });

        var meta = MetaFile.EnsureMeta(asset, "PrefabImporter");

        Assert.Equal(fixedGuid, meta.Guid);
    }

    [Fact]
    public void EnsureMeta_RegeneratesCorruptMeta()
    {
        string asset = AssetPath("broken.scene");
        File.WriteAllText(asset + ".meta", "}{ this is not valid echo");

        var meta = MetaFile.EnsureMeta(asset, "SceneImporter");

        // The corrupt .meta must be rewritten to a valid, re-readable file - not just returned in-memory.
        var recovered = MetaFile.Read(asset + ".meta");
        Assert.NotEqual(Guid.Empty, recovered.Guid);
        Assert.Equal(meta.Guid, recovered.Guid);
        Assert.Equal("SceneImporter", recovered.ImporterType);
    }

    [Fact]
    public void DeriveSubAssetGuid_IsDeterministic_AndNameSensitive()
    {
        var parent = Guid.NewGuid();

        Assert.Equal(AssetEntry.DeriveSubAssetGuid(parent, "Mesh_0"), AssetEntry.DeriveSubAssetGuid(parent, "Mesh_0"));
        Assert.NotEqual(AssetEntry.DeriveSubAssetGuid(parent, "Mesh_0"), AssetEntry.DeriveSubAssetGuid(parent, "Mesh_1"));
        Assert.NotEqual(AssetEntry.DeriveSubAssetGuid(parent, "Mesh_0"), AssetEntry.DeriveSubAssetGuid(Guid.NewGuid(), "Mesh_0"));
    }

    [Fact]
    public void NormalizePath_UsesForwardSlashes()
    {
        Assert.Equal("Foo/Bar/Baz.scene", EditorAssetDatabase.NormalizePath("Foo\\Bar\\Baz.scene"));
    }
}
