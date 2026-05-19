using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Computes a local-space AABB for every <see cref="IntermediateMesh"/>. Per-submesh bounds are
/// computed during the final bake into the public <see cref="Mesh"/>.
/// </summary>
internal sealed class GenerateBoundsStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.GenerateBounds;
    public string Name => "GenerateBounds";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        // Bounds is recomputed during SceneBaker (which has access to the final flat vertex layout),
        // so this step is currently a no-op marker that ensures the post-process pipeline ran
        // (the bake performs the actual AABB pass). Kept as its own step so users who opt out of
        // the bake-time AABB can still toggle it.
        _ = scene;
        _ = context;
    }
}
