using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Currently a no-op: modern renderers skin with a single bone matrix buffer that holds every
/// bone, so meshes don't need per-draw bone-count partitioning. Kept in the pipeline as a marker
/// so configurations targeting older fixed-uniform-array shader models can opt in via
/// <see cref="PostProcessFlags.SplitByBoneCount"/>.
/// </summary>
internal sealed class SplitByBoneCountStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.SplitByBoneCount;
    public string Name => "SplitByBoneCount";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        _ = scene;
        _ = context;
    }
}
