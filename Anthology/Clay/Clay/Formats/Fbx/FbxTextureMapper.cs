using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Maps FBX <c>Texture</c> + <c>Video</c> objects to <see cref="IntermediateTexture"/>s.
/// </summary>
/// <remarks>
/// FBX texture flow: <c>Material -&gt; (OP, "DiffuseColor") -&gt; Texture -&gt; (OO) -&gt; Video</c>.
/// The Video carries the actual filename and optionally embedded bytes (newer FBX versions can
/// inline images as <c>Content: R "..."</c> blobs).
/// </remarks>
internal static class FbxTextureMapper
{
    public sealed class TextureMapping
    {
        public Dictionary<long, int> TextureIndex { get; } = new();
    }

    public static TextureMapping MapAll(FbxDocument doc, IntermediateScene scene, ImportContext ctx)
    {
        var result = new TextureMapping();
        foreach (var obj in doc.Objects.Values)
        {
            if (obj.ObjectType != "Texture") continue;
            int idx = scene.Textures.Count;
            scene.Textures.Add(BuildTexture(obj, doc, ctx));
            result.TextureIndex[obj.Id] = idx;
        }
        return result;
    }

    private static IntermediateTexture BuildTexture(FbxObject tex, FbxDocument doc, ImportContext ctx)
    {
        var intermediate = new IntermediateTexture
        {
            Name = string.IsNullOrEmpty(tex.Name) ? $"Texture_{tex.Id}" : tex.Name,
        };

        ReadUVTransform(tex, intermediate);

        // Direct filename: Texture has a "FileName" or "RelativeFilename" child.
        string? fileName = tex.Node.FindChild("FileName")?.StringAt(0);
        string? relName = tex.Node.FindChild("RelativeFilename")?.StringAt(0);

        // Walk to the Video object - that's where the bytes might live.
        FbxObject? video = null;
        if (doc.ConnectionsByDestination.TryGetValue(tex.Id, out var conns))
        {
            foreach (var c in conns)
            {
                if (!doc.Objects.TryGetValue(c.Source, out var srcObj)) continue;
                if (srcObj.ObjectType == "Video")
                {
                    video = srcObj;
                    break;
                }
            }
        }

        if (video is not null)
        {
            fileName ??= video.Node.FindChild("FileName")?.StringAt(0)
                       ?? video.Node.FindChild("Filename")?.StringAt(0);
            relName ??= video.Node.FindChild("RelativeFilename")?.StringAt(0);

            // Embedded content (newer FBX): R-blob child.
            var content = video.Node.FindChild("Content");
            if (content is not null && content.Properties.Count > 0 && content.Properties[0].BlobValue is { Length: > 0 } bytes)
            {
                intermediate.EncodedBytes = bytes;
                intermediate.MimeType = GuessMime(fileName ?? relName);
            }
        }

        // Resolve external path against the FBX file's directory.
        if (intermediate.EncodedBytes is null && (fileName is not null || relName is not null))
        {
            string? toResolve = relName ?? fileName;
            if (toResolve is not null && ctx.SourcePath is not null)
            {
                // Some FBX exporters dump the full Windows path - try the filename alone too.
                string? resolved = ctx.Resolver.Resolve(ctx.SourcePath, toResolve)
                                ?? ctx.Resolver.Resolve(ctx.SourcePath, Path.GetFileName(toResolve));
                if (resolved is not null)
                {
                    intermediate.SourcePath = resolved;
                    intermediate.MimeType = GuessMime(resolved);
                }
                else if (IsMayaInternalPreviewTexture(toResolve))
                {
                    // Maya stamps ShaderFX/PBS preview cubemaps + BRDF LUTs into every FBX it
                    // exports (specular_cube.dds, diffuse_cube.dds, ibl_brdf_lut.dds, etc.). They
                    // live inside the Maya install dir on the artist's machine and are never
                    // shipped with the model. Skip them silently rather than spamming warnings.
                }
                else
                {
                    ctx.Log.Warning($"FBX texture '{intermediate.Name}': could not resolve '{toResolve}'.", "FbxTextureMapper");
                }
            }
        }
        return intermediate;
    }

    /// <summary>
    /// Reads the Texture node's UV transform onto <paramref name="intermediate"/>. Pulls the
    /// old-style <c>ModelUVTranslation</c> / <c>ModelUVScaling</c> from the node's direct
    /// children, then overrides with the <c>Texture.FbxFileTexture</c> property table's
    /// <c>Translation</c> / <c>Scaling</c> / <c>Rotation</c> (3ds Max + FBX SDK style;
    /// <c>Rotation.Z</c> is the 2D angle in degrees).
    /// </summary>
    private static void ReadUVTransform(FbxObject tex, IntermediateTexture intermediate)
    {
        // Old-style ModelUVTranslation / ModelUVScaling: direct children with two floats.
        var modelUVT = tex.Node.FindChild("ModelUVTranslation");
        if (modelUVT is not null && modelUVT.Properties.Count >= 2)
        {
            intermediate.UVOffset = new Float2(
                (float)(modelUVT.Properties[0].AsDouble()),
                (float)(modelUVT.Properties[1].AsDouble()));
        }
        var modelUVS = tex.Node.FindChild("ModelUVScaling");
        if (modelUVS is not null && modelUVS.Properties.Count >= 2)
        {
            intermediate.UVScale = new Float2(
                (float)(modelUVS.Properties[0].AsDouble()),
                (float)(modelUVS.Properties[1].AsDouble()));
        }
        // Property-table form (overrides old-style if present).
        var p = tex.Properties;
        if (p.TryGetVec3("Translation", out double tx, out double ty, out _))
            intermediate.UVOffset = new Float2((float)tx, (float)ty);
        if (p.TryGetVec3("Scaling", out double sx, out double sy, out _))
            intermediate.UVScale = new Float2((float)sx, (float)sy);
        if (p.TryGetVec3("Rotation", out _, out _, out double rz))
            intermediate.UVRotation = (float)(rz * Math.PI / 180.0); // FBX stores rotation in degrees, we want radians
    }

    /// <summary>
    /// Matches Maya-internal preview asset paths that Maya bakes into every FBX export but never
    /// ships with the model (ShaderFX/PBS environment cubemaps, IBL BRDF lookup table, etc.).
    /// </summary>
    private static bool IsMayaInternalPreviewTexture(string path)
    {
        string p = path.Replace('\\', '/');
        // Path-shape: typically "<...>/Maya<version>/presets/ShaderFX/<...>" or
        // "<...>/Maya/presets/<...>". Match on the distinctive "presets/ShaderFX" segment, plus
        // a few common standalone filenames Maya emits regardless of path layout.
        if (p.Contains("/presets/ShaderFX/", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Contains("/Maya2", StringComparison.OrdinalIgnoreCase) && p.Contains("/presets/", StringComparison.OrdinalIgnoreCase)) return true;
        string fileName = Path.GetFileName(p);
        return fileName.Equals("specular_cube.dds", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("diffuse_cube.dds",  StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("ibl_brdf_lut.dds",  StringComparison.OrdinalIgnoreCase);
    }

    private static string? GuessMime(string? path) =>
        path is null ? null : Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".tga" => "image/x-tga",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".dds" => "image/vnd-ms.dds",
            ".webp" => "image/webp",
            ".ktx2" => "image/ktx2",
            _ => null,
        };
}
