using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Reads any externally referenced texture file into <see cref="IntermediateTexture.EncodedBytes"/>,
/// so the caller can ship a single self-contained <see cref="Model"/> without sidecar files.
/// </summary>
/// <remarks>
/// Leaves <see cref="IntermediateTexture.SourcePath"/> populated as well so consumers that prefer
/// streaming-from-disk can still see where the bytes came from. Files that can't be read produce
/// a warning in the import log; the texture's <c>EncodedBytes</c> stays <c>null</c> in that case.
/// </remarks>
internal sealed class EmbedTexturesStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.EmbedTextures;
    public string Name => "EmbedTextures";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        foreach (var tex in scene.Textures)
        {
            if (tex.EncodedBytes is not null)
                continue; // Already embedded (data URI, GLB bufferView, etc.)
            if (tex.SourcePath is null)
                continue;
            if (!context.Resolver.Exists(tex.SourcePath))
            {
                context.Log.Warning(
                    $"Texture '{tex.Name ?? "(unnamed)"}' source path '{tex.SourcePath}' does not exist on disk; skipping embed.",
                    Name);
                continue;
            }

            try
            {
                tex.EncodedBytes = context.Resolver.ReadAllBytes(tex.SourcePath);
            }
            catch (Exception ex)
            {
                context.Log.Warning(
                    $"Failed to embed texture '{tex.Name ?? "(unnamed)"}' from '{tex.SourcePath}': {ex.Message}",
                    Name);
            }
        }
    }
}
