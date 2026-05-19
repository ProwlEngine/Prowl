using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Truncates each vertex's bone-influence list to the top-N entries by weight, then renormalises
/// the kept weights so they sum to 1.
/// </summary>
/// <remarks>
/// glTF allows up to 8 influences (JOINTS_0 + JOINTS_1); FBX is unbounded. Game engines typically
/// upload 4 influences per vertex for skinning. After this step every mesh has exactly
/// <see cref="ModelImporterSettings.BoneWeightLimit"/> influences.
/// </remarks>
internal sealed class LimitBoneWeightsStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.LimitBoneWeights;
    public string Name => "LimitBoneWeights";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        int limit = Math.Max(1, context.Settings.BoneWeightLimit);

        // glTF JOINTS_n caps at 8 components per vertex in practice (JOINTS_0 + JOINTS_1).
        // Use a single stack-allocated scratch buffer for top-N selection per vertex.
        const int MaxInfluencesCap = 32;
        Span<(int index, float weight)> scratch = stackalloc (int, float)[MaxInfluencesCap];

        foreach (var mesh in scene.Meshes)
        {
            if (mesh.VertexJoints is null || mesh.VertexWeights is null)
                continue;

            int oldInfluences = mesh.MaxInfluencesPerVertex;
            int vertexCount = mesh.Positions.Count;
            if (oldInfluences <= limit)
            {
                NormaliseInPlace(mesh.VertexWeights, vertexCount, oldInfluences);
                continue;
            }
            if (oldInfluences > MaxInfluencesCap)
            {
                context.Log.Warning(
                    $"Mesh '{mesh.Name}' has {oldInfluences} bone influences per vertex which exceeds the analyzer cap of {MaxInfluencesCap}; truncating influences before top-N selection.",
                    Name);
                oldInfluences = MaxInfluencesCap;
            }

            int[] newJoints = new int[vertexCount * limit];
            float[] newWeights = new float[vertexCount * limit];

            for (int v = 0; v < vertexCount; v++)
            {
                int srcBase = v * oldInfluences;

                for (int k = 0; k < oldInfluences; k++)
                    scratch[k] = (mesh.VertexJoints[srcBase + k], mesh.VertexWeights[srcBase + k]);

                // Top-N selection: simple O(N*K) for small N - N is at most 8 in practice.
                int dstBase = v * limit;
                for (int slot = 0; slot < limit; slot++)
                {
                    int bestK = -1;
                    float bestW = -1f;
                    for (int k = 0; k < oldInfluences; k++)
                    {
                        if (scratch[k].weight > bestW)
                        {
                            bestW = scratch[k].weight;
                            bestK = k;
                        }
                    }
                    if (bestK >= 0)
                    {
                        newJoints[dstBase + slot] = scratch[bestK].index;
                        newWeights[dstBase + slot] = scratch[bestK].weight;
                        scratch[bestK] = (0, -1f);
                    }
                }

                // Renormalise.
                float sum = 0f;
                for (int s = 0; s < limit; s++)
                    sum += newWeights[dstBase + s];
                if (sum > 1e-6f)
                {
                    for (int s = 0; s < limit; s++)
                        newWeights[dstBase + s] /= sum;
                }
                else
                {
                    // No bone influence at all: bind to bone 0 with weight 1 to keep the math sane.
                    newJoints[dstBase + 0] = 0;
                    newWeights[dstBase + 0] = 1f;
                    for (int s = 1; s < limit; s++)
                    {
                        newJoints[dstBase + s] = 0;
                        newWeights[dstBase + s] = 0f;
                    }
                }
            }

            mesh.VertexJoints = newJoints;
            mesh.VertexWeights = newWeights;
            mesh.MaxInfluencesPerVertex = limit;
        }
    }

    private static void NormaliseInPlace(float[] weights, int vertexCount, int influencesPerVertex)
    {
        for (int v = 0; v < vertexCount; v++)
        {
            int b = v * influencesPerVertex;
            float sum = 0f;
            for (int i = 0; i < influencesPerVertex; i++)
                sum += weights[b + i];
            if (sum > 1e-6f)
            {
                for (int i = 0; i < influencesPerVertex; i++)
                    weights[b + i] /= sum;
            }
        }
    }
}
