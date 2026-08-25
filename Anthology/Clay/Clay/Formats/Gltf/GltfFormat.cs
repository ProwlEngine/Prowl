// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.Json;

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Clay.Internal.IO;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// glTF 2.0 importer. Handles <c>.gltf</c> (JSON + external resources), <c>.glb</c> (chunked
/// binary), and <c>.vrm</c> (a <c>.glb</c> with VRM extensions exposed via raw JSON).
/// </summary>
internal sealed class GltfFormat : IModelFormat
{
    public string Token => "gltf";

    public bool CanRead(string formatToken) =>
        formatToken is "gltf" or "glb" or "vrm";

    public IntermediateScene Read(Stream stream, ImportContext context)
    {
        byte[] jsonBytes;
        byte[]? binChunk = null;

        if (context.Format == "glb" || context.Format == "vrm")
        {
            var glb = Glb.Read(stream);
            jsonBytes = glb.Json;
            binChunk = glb.Bin;
        }
        else
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            jsonBytes = ms.ToArray();
        }

        GltfDom dom;
        try
        {
            dom = JsonSerializer.Deserialize<GltfDom>(jsonBytes, JsonOptions) ??
                  throw new ImportException("glTF JSON is empty or null.", context.SourcePath, context.Format);
        }
        catch (JsonException ex)
        {
            throw new ImportException($"Malformed glTF JSON: {ex.Message}", context.SourcePath, context.Format, ex);
        }

        // glTF 1.0 (deprecated 2017) uses a completely different schema: GLSL shader programs
        // inline in the material, "technique" definitions, string-keyed accessor offsets, no PBR
        // material model. We don't carry that codepath; reject with a clear re-export hint
        // rather than silently mis-parsing as 2.0.
        if (dom.Asset.Version is { } v && v.StartsWith("1.", System.StringComparison.Ordinal))
        {
            throw new ImportException(
                $"glTF 1.0 (asset version '{v}') is not supported. Re-export as glTF 2.0 from your DCC " +
                "(Blender's glTF exporter, FBX2glTF, Khronos's converters, etc.).",
                context.SourcePath, context.Format);
        }
        if (dom.Asset.Version is not "2.0")
            context.Log.Warning(
                $"glTF asset version is '{dom.Asset.Version}'; only 2.0 is officially supported.",
                "GltfFormat");

        CheckRequiredExtensions(dom, context);
        CheckMeshCompression(dom, context);

        var buffers = new GltfBufferStore(dom, binChunk, context);
        var accessor = new GltfAccessorReader(dom, buffers);

        var scene = new IntermediateScene
        {
            Format = context.Format,
            FormatVersion = dom.Asset.Version,
            Generator = dom.Asset.Generator,
            Copyright = dom.Asset.Copyright,
            SourceCoordinateSystem = CoordinateSystem.RightHandedYUp,
            SourceUnitToMeters = 1f,
        };

        if (dom.Extensions is not null)
        {
            foreach (var kvp in dom.Extensions)
                scene.RawExtensions[kvp.Key] = kvp.Value.Clone();
        }

        GltfTextureMapper.MapAll(dom, buffers, scene, context);
        GltfMaterialMapper.MapAll(dom, scene, context);
        var meshMapping = GltfMeshMapper.MapAll(dom, accessor, scene, context);
        var nodeMapping = GltfNodeMapper.Map(dom, meshMapping, scene, context);
        scene.Root = nodeMapping.Root;

        GltfSkinMapper.MapAll(dom, nodeMapping.SourceNodeToIntermediate, accessor, scene, context);
        GltfAnimationMapper.MapAll(dom, nodeMapping.SourceNodeToIntermediate, accessor, meshMapping, scene, context);

        return scene;
    }

    /// <summary>
    /// An <c>extensionsRequired</c> entry means exactly what it says: a client that does not
    /// implement it must not render the file. Importing anyway produced a model that looked
    /// plausible and was quietly wrong, so this fails instead, naming everything missing at once
    /// rather than one re-export at a time.
    /// </summary>
    private static void CheckRequiredExtensions(GltfDom dom, ImportContext ctx)
    {
        if (dom.ExtensionsRequired is null) return;

        var missing = new List<string>();
        foreach (var ext in dom.ExtensionsRequired)
            if (!IsKnown(ext) && !missing.Contains(ext))
                missing.Add(ext);

        if (missing.Count == 0) return;

        throw new ImportException(
            $"This glTF requires extension(s) the importer does not implement: {string.Join(", ", missing)}. " +
            "The file declared them as required, so importing anyway would silently produce wrong data. " +
            "Re-export without them, or run the file through a converter such as gltf-transform to bake them out.",
            ctx.SourcePath, ctx.Format);
    }

    /// <summary>
    /// Compressed geometry is the dangerous case and gets its own check. A compressed primitive's
    /// accessors carry no <c>bufferView</c>, and an accessor without one reads as all zeros, so
    /// without this the mesh imports as every vertex sitting on the origin with nothing to show
    /// that anything went wrong. Checked wherever the extension appears rather than only in
    /// <c>extensionsRequired</c>, since a file that lists it only under <c>extensionsUsed</c>
    /// decodes to the same nothing.
    /// </summary>
    private static void CheckMeshCompression(GltfDom dom, ImportContext ctx)
    {
        string? found = null;

        if (dom.ExtensionsUsed is { } used)
            foreach (var ext in used)
                if (IsMeshCompression(ext)) { found = ext; break; }

        if (found is null && dom.Meshes is { } meshes)
        {
            foreach (var mesh in meshes)
            {
                foreach (var prim in mesh.Primitives)
                {
                    if (prim.Extensions is null) continue;
                    foreach (var key in prim.Extensions.Keys)
                        if (IsMeshCompression(key)) { found = key; break; }
                    if (found is not null) break;
                }
                if (found is not null) break;
            }
        }

        if (found is null) return;

        throw new ImportException(
            $"This glTF uses '{found}' to compress its geometry, which the importer cannot decode. " +
            "Compressed vertex data has no readable fallback in the file, so the mesh would import as " +
            "every vertex at the origin. Re-export without compression, or decompress the file first " +
            "(gltf-transform and the Khronos tools both do this).",
            ctx.SourcePath, ctx.Format);
    }

    private static bool IsMeshCompression(string extension) =>
        extension is "KHR_draco_mesh_compression" or "EXT_meshopt_compression";

    private static bool IsKnown(string extension) => extension switch
    {
        "KHR_materials_unlit" or
        "KHR_texture_transform" or
        "KHR_materials_emissive_strength" or
        "KHR_materials_clearcoat" or
        "KHR_materials_sheen" or
        "KHR_materials_transmission" or
        "KHR_materials_volume" or
        "KHR_materials_ior" or
        "KHR_materials_specular" or
        "KHR_materials_pbrSpecularGlossiness" or
        // Quantized attributes need no special handling: the accessor reader already decodes every
        // integer component type the extension permits, normalized or not.
        "KHR_mesh_quantization" => true,
        _ => false,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}
