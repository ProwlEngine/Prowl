using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Strips per-vertex bone weights that fall below a threshold. Renormalises remaining weights.
/// </summary>
/// <remarks>
/// Useful for content that exports rigid-but-skinned meshes: a static mesh attached to a single
/// joint may carry dummy weights that contribute negligibly to deformation but still consume
/// bone slots. This step compacts those away.
/// </remarks>
internal sealed class DeboneStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.Debone;
    public string Name => "Debone";

    /// <summary>Weights below this contribute less than 0.1% and are pruned.</summary>
    private const float Threshold = 1e-3f;

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        int stripped = 0;
        foreach (var mesh in scene.Meshes)
        {
            if (mesh.VertexJoints is null || mesh.VertexWeights is null) continue;

            int influences = mesh.MaxInfluencesPerVertex;
            int vc = mesh.Positions.Count;
            for (int v = 0; v < vc; v++)
            {
                int b = v * influences;
                float renormSum = 0f;
                for (int k = 0; k < influences; k++)
                {
                    if (mesh.VertexWeights[b + k] < Threshold)
                    {
                        if (mesh.VertexWeights[b + k] > 0f) stripped++;
                        mesh.VertexWeights[b + k] = 0f;
                        mesh.VertexJoints[b + k] = 0;
                    }
                    renormSum += mesh.VertexWeights[b + k];
                }
                if (renormSum > 1e-6f)
                {
                    for (int k = 0; k < influences; k++)
                        mesh.VertexWeights[b + k] /= renormSum;
                }
            }
        }
        if (stripped > 0)
            context.Log.Info($"Stripped {stripped} sub-threshold bone influence(s).", Name);
    }
}
