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

        /// <summary>Per-Texture-object UV transform, keyed by the FBX Texture object's own id -
        /// kept separate from the (possibly shared/deduplicated) <see cref="IntermediateTexture"/>
        /// image data in <see cref="TextureIndex"/>, since two materials can legitimately tile/offset
        /// the same image differently (e.g. a shared brick texture repeated 2x2 on one wall and 4x4
        /// on another).</summary>
        public Dictionary<long, (Vector.Float2 Offset, Vector.Float2 Scale, float Rotation)> UVTransform { get; } = new();
    }

    public static TextureMapping MapAll(FbxDocument doc, IntermediateScene scene, ImportContext ctx)
    {
        var result = new TextureMapping();

        // FBX creates one "Texture" object per material-texture *usage*, not per unique image - a
        // scene where many materials reuse the same diffuse/normal map (e.g. every wall/floor
        // material in an architectural scene sharing a handful of textures) commonly has far more
        // Texture objects than actual images. Without dedup here, each usage would independently
        // resolve/embed and (downstream, in the caller that decodes IntermediateTexture into a real
        // texture) fully decode the same image again, multiplying import cost by the reuse count
        // instead of the unique-texture count. Dedup first by the shared Video object (the common
        // case: multiple Texture objects pointing at one Video), then by resolved file path (covers
        // Texture objects with no Video that still point at the same file on disk). UV transform is
        // read per Texture-object regardless of dedup, since it's a per-usage property, not part of
        // the image identity.
        var videoIdToIndex = new Dictionary<long, int>();
        var pathToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var obj in doc.Objects.Values)
        {
            if (obj.ObjectType != "Texture") continue;

            result.UVTransform[obj.Id] = ReadUVTransform(obj);

            FbxObject? video = FindConnectedVideo(doc, obj.Id);
            if (video is not null && videoIdToIndex.TryGetValue(video.Id, out int existingByVideo))
            {
                result.TextureIndex[obj.Id] = existingByVideo;
                continue;
            }

            var built = BuildTexture(obj, video, ctx);

            if (built.SourcePath is not null && pathToIndex.TryGetValue(built.SourcePath, out int existingByPath))
            {
                result.TextureIndex[obj.Id] = existingByPath;
                if (video is not null) videoIdToIndex[video.Id] = existingByPath;
                continue;
            }

            int idx = scene.Textures.Count;
            scene.Textures.Add(built);
            result.TextureIndex[obj.Id] = idx;
            if (video is not null) videoIdToIndex[video.Id] = idx;
            if (built.SourcePath is not null) pathToIndex[built.SourcePath] = idx;
        }
        return result;
    }

    /// <summary>Walks the Texture object's connections to find the Video object carrying its bytes/filename, if any.</summary>
    private static FbxObject? FindConnectedVideo(FbxDocument doc, long texId)
    {
        if (doc.ConnectionsByDestination.TryGetValue(texId, out var conns))
        {
            foreach (var c in conns)
            {
                if (!doc.Objects.TryGetValue(c.Source, out var srcObj)) continue;
                if (srcObj.ObjectType == "Video")
                    return srcObj;
            }
        }
        return null;
    }

    private static IntermediateTexture BuildTexture(FbxObject tex, FbxObject? video, ImportContext ctx)
    {
        var intermediate = new IntermediateTexture
        {
            Name = string.IsNullOrEmpty(tex.Name) ? $"Texture_{tex.Id}" : tex.Name,
        };

        // Direct filename: Texture has a "FileName" or "RelativeFilename" child.
        string? fileName = tex.Node.FindChild("FileName")?.StringAt(0);
        string? relName = tex.Node.FindChild("RelativeFilename")?.StringAt(0);

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
    /// Reads a Texture object's UV transform. Pulls the old-style <c>ModelUVTranslation</c> /
    /// <c>ModelUVScaling</c> from the node's direct children, then overrides with the
    /// <c>Texture.FbxFileTexture</c> property table's <c>Translation</c> / <c>Scaling</c> /
    /// <c>Rotation</c> (3ds Max + FBX SDK style; <c>Rotation.Z</c> is the 2D angle in degrees).
    /// </summary>
    private static (Float2 Offset, Float2 Scale, float Rotation) ReadUVTransform(FbxObject tex)
    {
        Float2 offset = Float2.Zero;
        Float2 scale = Float2.One;
        float rotation = 0f;

        // Old-style ModelUVTranslation / ModelUVScaling: direct children with two floats.
        var modelUVT = tex.Node.FindChild("ModelUVTranslation");
        if (modelUVT is not null && modelUVT.Properties.Count >= 2)
        {
            offset = new Float2(
                (float)(modelUVT.Properties[0].AsDouble()),
                (float)(modelUVT.Properties[1].AsDouble()));
        }
        var modelUVS = tex.Node.FindChild("ModelUVScaling");
        if (modelUVS is not null && modelUVS.Properties.Count >= 2)
        {
            scale = new Float2(
                (float)(modelUVS.Properties[0].AsDouble()),
                (float)(modelUVS.Properties[1].AsDouble()));
        }
        // Property-table form (overrides old-style if present).
        var p = tex.Properties;
        if (p.TryGetVec3("Translation", out double tx, out double ty, out _))
            offset = new Float2((float)tx, (float)ty);
        if (p.TryGetVec3("Scaling", out double sx, out double sy, out _))
            scale = new Float2((float)sx, (float)sy);
        if (p.TryGetVec3("Rotation", out _, out _, out double rz))
            rotation = (float)(rz * Math.PI / 180.0); // FBX stores rotation in degrees, we want radians

        return (offset, scale, rotation);
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
