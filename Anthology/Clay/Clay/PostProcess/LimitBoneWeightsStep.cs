// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
                // Sorted even though nothing is dropped, so BoneWeight.Index0 really is the strongest
                // influence its documentation promises. The common four-influence glTF case lands here.
                SortByWeightInPlace(mesh.VertexJoints, mesh.VertexWeights, vertexCount, oldInfluences);
                NormaliseInPlace(mesh.VertexWeights, vertexCount, oldInfluences);
                continue;
            }
            int readCount = oldInfluences;
            if (oldInfluences > MaxInfluencesCap)
            {
                context.Log.Warning(
                    $"Mesh '{mesh.Name}' has {oldInfluences} bone influences per vertex which exceeds the analyzer cap of {MaxInfluencesCap}; truncating influences before top-N selection.",
                    Name);
                readCount = MaxInfluencesCap;
            }

            int[] newJoints = new int[vertexCount * limit];
            float[] newWeights = new float[vertexCount * limit];

            for (int v = 0; v < vertexCount; v++)
            {
                // Source arrays keep their original stride even when we only read the first readCount.
                int srcBase = v * oldInfluences;

                for (int k = 0; k < readCount; k++)
                    scratch[k] = (mesh.VertexJoints[srcBase + k], mesh.VertexWeights[srcBase + k]);

                // Top-N selection: simple O(N*K) for small N - N is at most 8 in practice.
                int dstBase = v * limit;
                for (int slot = 0; slot < limit; slot++)
                {
                    int bestK = -1;
                    float bestW = -1f;
                    for (int k = 0; k < readCount; k++)
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
                // A vertex with no influence is left with none. Binding it to bone 0 would snap
                // genuinely unweighted geometry onto whichever joint happens to be listed first,
                // whereas zero weights are what a skinning shader reads as "not skinned" and falls
                // back to mesh local space for. The early-out path above leaves them alone too.
            }

            mesh.VertexJoints = newJoints;
            mesh.VertexWeights = newWeights;
            mesh.MaxInfluencesPerVertex = limit;
        }
    }

    /// <summary>Orders each vertex's influences strongest first, keeping joints and weights paired.</summary>
    private static void SortByWeightInPlace(int[] joints, float[] weights, int vertexCount, int influencesPerVertex)
    {
        // Insertion sort: influencesPerVertex is at most the bone weight limit, so a handful.
        for (int v = 0; v < vertexCount; v++)
        {
            int b = v * influencesPerVertex;
            for (int i = 1; i < influencesPerVertex; i++)
            {
                float w = weights[b + i];
                int j = joints[b + i];
                int k = i - 1;
                while (k >= 0 && weights[b + k] < w)
                {
                    weights[b + k + 1] = weights[b + k];
                    joints[b + k + 1] = joints[b + k];
                    k--;
                }
                weights[b + k + 1] = w;
                joints[b + k + 1] = j;
            }
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
