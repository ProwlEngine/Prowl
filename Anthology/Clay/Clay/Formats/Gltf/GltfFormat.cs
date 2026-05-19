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

        ReportRequiredExtensions(dom, context);

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

    private static void ReportRequiredExtensions(GltfDom dom, ImportContext ctx)
    {
        if (dom.ExtensionsRequired is null) return;
        foreach (var ext in dom.ExtensionsRequired)
        {
            if (!IsKnown(ext))
            {
                ctx.Log.Warning(
                    $"Required extension '{ext}' is not implemented; importing may produce incomplete data.",
                    "GltfFormat");
            }
        }
    }

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
        "KHR_materials_pbrSpecularGlossiness" => true,
        _ => false,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}
