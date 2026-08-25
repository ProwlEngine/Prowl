// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// Gating on extensions the importer cannot honour. The case that matters is compressed geometry:
/// a compressed primitive's accessors carry no bufferView, and an accessor without one reads as all
/// zeros, so the file used to import as every vertex on the origin with nothing to say so.
/// </summary>
public sealed class GltfExtensionGatingTests
{
    private static Model Load(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ModelImporter.Load(stream, "gltf", ModelImporterSettings.Raw);
    }

    private static string Doc(string body) => $$"""
    {
      "asset": { "version": "2.0" },
      {{body}}
      "scene": 0,
      "scenes": [ { "nodes": [] } ]
    }
    """;

    [Theory]
    [InlineData("KHR_draco_mesh_compression")]
    [InlineData("EXT_meshopt_compression")]
    public void CompressedGeometry_IsRejectedWhenRequired(string extension)
    {
        var ex = Assert.Throws<ImportException>(() =>
            Load(Doc($"\"extensionsRequired\": [ \"{extension}\" ], \"extensionsUsed\": [ \"{extension}\" ],")));

        Assert.Contains(extension, ex.Message);
    }

    // Listing it only under extensionsUsed decodes to exactly the same nothing, so the check does
    // not rely on the file having declared it required.
    [Theory]
    [InlineData("KHR_draco_mesh_compression")]
    [InlineData("EXT_meshopt_compression")]
    public void CompressedGeometry_IsRejectedWhenOnlyUsed(string extension)
    {
        var ex = Assert.Throws<ImportException>(() =>
            Load(Doc($"\"extensionsUsed\": [ \"{extension}\" ],")));

        Assert.Contains(extension, ex.Message);
        Assert.Contains("origin", ex.Message);
    }

    // A file that declares it nowhere at the document level but still compresses a primitive.
    [Fact]
    public void CompressedGeometry_IsRejectedFromThePrimitiveAlone()
    {
        var ex = Assert.Throws<ImportException>(() => Load(Doc("""
        "meshes": [ { "primitives": [ {
          "attributes": { "POSITION": 0 },
          "extensions": { "KHR_draco_mesh_compression": { "bufferView": 0, "attributes": { "POSITION": 0 } } }
        } ] } ],
        "accessors": [ { "componentType": 5126, "count": 3, "type": "VEC3" } ],
        """)));

        Assert.Contains("KHR_draco_mesh_compression", ex.Message);
    }

    /// <summary>
    /// extensionsRequired means a client that cannot honour it must not render the file. Importing
    /// anyway produced something that looked plausible and was quietly missing whatever the
    /// extension carried.
    /// </summary>
    [Theory]
    [InlineData("KHR_texture_basisu")]
    [InlineData("EXT_texture_webp")]
    [InlineData("KHR_materials_variants")]
    public void UnimplementedRequiredExtension_IsRejected(string extension)
    {
        var ex = Assert.Throws<ImportException>(() =>
            Load(Doc($"\"extensionsRequired\": [ \"{extension}\" ],")));

        Assert.Contains(extension, ex.Message);
    }

    [Fact]
    public void EveryMissingRequiredExtension_IsNamedAtOnce()
    {
        var ex = Assert.Throws<ImportException>(() =>
            Load(Doc("\"extensionsRequired\": [ \"KHR_texture_basisu\", \"KHR_materials_variants\" ],")));

        Assert.Contains("KHR_texture_basisu", ex.Message);
        Assert.Contains("KHR_materials_variants", ex.Message);
    }

    [Theory]
    [InlineData("KHR_texture_transform")]
    [InlineData("KHR_materials_unlit")]
    [InlineData("KHR_materials_pbrSpecularGlossiness")]
    // Quantized attributes need no special handling: the accessor reader already decodes every
    // integer component type the extension permits.
    [InlineData("KHR_mesh_quantization")]
    public void ImplementedRequiredExtension_LoadsFine(string extension)
    {
        var model = Load(Doc($"\"extensionsRequired\": [ \"{extension}\" ],"));
        Assert.NotNull(model.Root);
    }

    [Fact]
    public void UnimplementedExtensionThatIsOnlyUsed_StillLoads()
    {
        // Not required means the file renders acceptably without it, so it must not be a hard stop.
        var model = Load(Doc("\"extensionsUsed\": [ \"KHR_materials_iridescence\" ],"));
        Assert.NotNull(model.Root);
    }

    /// <summary>
    /// An accessor with neither a bufferView nor a sparse overlay is legal and reads as zeros. For
    /// POSITION that collapses the mesh to a point, which is worth saying out loud even though it
    /// is not an error.
    /// </summary>
    [Fact]
    public void PositionAccessorWithNoData_IsReported()
    {
        var model = Load(Doc("""
        "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 } } ] } ],
        "accessors": [ { "componentType": 5126, "count": 3, "type": "VEC3" } ],
        """));

        Assert.Contains(model.Log.Entries, e =>
            e.Severity == ImportLogSeverity.Warning && e.Message.Contains("POSITION accessor has no bufferView"));
    }
}
