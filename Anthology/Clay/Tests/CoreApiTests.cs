using System.Text;
using Prowl.Clay;
using Prowl.Clay.Importer;
using Prowl.Vector;
using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// Tests for the public surface area that doesn't need any real model data: format detection,
/// stream loading of a minimal-valid glTF, and the lightweight <see cref="Bounds"/> /
/// <see cref="Mesh"/> helpers.
/// </summary>
public sealed class CoreApiTests
{
    [Fact]
    public void FormatDetector_RecognizesEveryKnownExtension()
    {
        Assert.Equal("gltf", FormatDetector.FromPath("foo.gltf"));
        Assert.Equal("glb",  FormatDetector.FromPath("foo.glb"));
        Assert.Equal("vrm",  FormatDetector.FromPath("foo.vrm"));
        Assert.Equal("obj",  FormatDetector.FromPath("foo.obj"));
        Assert.Equal("fbx",  FormatDetector.FromPath("foo.FBX"));   // case insensitive
        Assert.Null(FormatDetector.FromPath("foo.bin"));            // unknown extension
        Assert.Null(FormatDetector.FromPath("noext"));              // no extension at all
    }

    [Fact]
    public void FormatDetector_DetectsGlbMagicByStream()
    {
        // glb magic = 'glTF' (0x46546C67) followed by little-endian version 2.
        byte[] bytes = { 0x67, 0x6C, 0x54, 0x46, 0x02, 0, 0, 0 };
        using var ms = new MemoryStream(bytes);
        Assert.True(FormatDetector.TryDetectFromStream(ms, out string fmt));
        Assert.Equal("glb", fmt);
    }

    [Fact]
    public void FormatDetector_DetectsFbxMagicByStream()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("Kaydara FBX Binary  ");
        using var ms = new MemoryStream(bytes);
        Assert.True(FormatDetector.TryDetectFromStream(ms, out string fmt));
        Assert.Equal("fbx", fmt);
    }

    [Fact]
    public void ModelImporter_LoadsMinimalValidGltfFromStream()
    {
        // The minimal valid glTF 2.0 document: just the required asset block.
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"}}"));

        var model = ModelImporter.Load(ms, "gltf");

        Assert.NotNull(model);
        Assert.Equal("gltf", model.Metadata.Format);
        Assert.Equal("2.0",  model.Metadata.FormatVersion);
        Assert.Single(model.Nodes);             // synthetic root
        Assert.Empty(model.Meshes);
        Assert.Empty(model.Materials);
        Assert.Empty(model.AnimationClips);
    }

    [Fact]
    public void Bounds_Empty_GrowsToEncapsulatePoints()
    {
        var b = Bounds.Empty;
        Assert.True(b.IsEmpty);

        b.Encapsulate(new Float3(1f, 2f, 3f));
        b.Encapsulate(new Float3(-1f, 5f, -3f));

        Assert.Equal(new Float3(-1f, 2f, -3f), b.Min);
        Assert.Equal(new Float3( 1f, 5f,  3f), b.Max);
    }

    [Fact]
    public void Mesh_GetIndices16_ThrowsWhenAnyIndexExceedsUshortRange()
    {
        var mesh = new Mesh
        {
            Vertices  = new Float3[1],
            SubMeshes = new[] { new SubMesh { IndexStart = 0, IndexCount = 1 } },
            Indices   = new uint[] { 70_000 },
        };
        Assert.Throws<InvalidOperationException>(() => mesh.GetIndices16(0));
    }
}
