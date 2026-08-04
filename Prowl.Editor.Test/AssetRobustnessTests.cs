// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using ImageMagick;

using Prowl.Editor.Importers;
using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// A transiently unreadable or unwritable file must never be treated as a permanent verdict about an
/// asset's identity or its imported state - both are how references silently die.
/// </summary>
[Trait("Category", "Build")]
public class AssetRobustnessTests : EditorTestHarness
{
    private Guid MakeTexture(string relativePath)
    {
        string abs = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        using (var image = new MagickImage(new MagickColor(20, 40, 60, 255), 8, 8))
        {
            image.Format = MagickFormat.Png;
            image.Write(abs);
        }
        Guid guid = Assets.ImportFile(relativePath);
        Assert.NotEqual(Guid.Empty, guid);
        return guid;
    }

    // A .meta that exists but momentarily cannot be read (antivirus, a sync client, a backup agent
    // holding it) must not be replaced with a fresh GUID - that orphans the asset and every sub-asset.
    [Fact]
    public void UnreadableMetaFile_DoesNotRegenerateTheGuid()
    {
        Guid texGuid = MakeTexture("Locked.png");
        string metaPath = MetaFile.GetMetaPath(AssetAbsolutePath("Locked.png"));
        Assert.True(File.Exists(metaPath));

        using (File.Open(metaPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Refuse outright rather than mint a replacement identity.
            Assert.Throws<IOException>(
                () => MetaFile.EnsureMeta(AssetAbsolutePath("Locked.png"), nameof(TextureImporter)));

            // And a full refresh over the locked file has to skip it, not orphan it.
            Assets.Refresh();
        }

        Assert.Equal(texGuid, MetaFile.Read(metaPath).Guid);
        Assert.Equal(texGuid, Assets.PathToGuid("Locked.png"));
    }

    // Duplicating an asset (file + .meta) gives two files one GUID. The copy must be the one re-minted;
    // if the original is picked instead, every reference in the project silently retargets.
    [Fact]
    public void DuplicatedMetaFile_ReMintsTheCopyNotTheOriginal()
    {
        // Named so the copy sorts first, which is the order a directory walk hands them over in.
        Guid originalGuid = MakeTexture("Original.png");
        string originalAbs = AssetAbsolutePath("Original.png");

        File.Copy(originalAbs, AssetAbsolutePath("Copy.png"));
        File.Copy(MetaFile.GetMetaPath(originalAbs), MetaFile.GetMetaPath(AssetAbsolutePath("Copy.png")));

        // Simulate a fresh checkout: no metadata.db, so the scan has only the .meta files to go on.
        File.Delete(Project.MetadataDbPath);
        ReopenDatabase();

        Assert.Equal(originalGuid, Assets.PathToGuid("Original.png"));
        Assert.NotEqual(originalGuid, Assets.PathToGuid("Copy.png"));
        Assert.NotEqual(Guid.Empty, Assets.PathToGuid("Copy.png"));
    }

    // If the import cannot write its cache, the entry must not be recorded as freshly imported -
    // it would leave the previous cache in place while claiming to be current, which is what a build ships.
    [Fact]
    public void FailedCacheWrite_LeavesTheAssetMarkedStale()
    {
        Guid texGuid = MakeTexture("Cached.png");
        string cachePath = Path.Combine(Project.CachePath, $"{texGuid}.asset");
        Assert.True(File.Exists(cachePath));

        // Touch the source so the reimport has something new to write, then block the write.
        string abs = AssetAbsolutePath("Cached.png");
        File.SetLastWriteTimeUtc(abs, DateTime.UtcNow.AddSeconds(5));

        using (File.Open(cachePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assets.Reimport(texGuid);
        }

        Assert.True(Assets.EnsureCacheUpToDate(texGuid),
            "An import whose cache write failed must still be considered stale.");
    }
}
