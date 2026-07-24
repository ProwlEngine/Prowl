// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Drops zero-area triangles, zero-length lines, and faces with coincident indices.
/// </summary>
/// <remarks>
/// Removes the face from the face list rather than demoting it to a point or line. Runs as a
/// pre-pass before vertex-cache optimization so the optimizer doesn't waste budget on garbage
/// triangles.
/// </remarks>
internal sealed class RemoveDegeneratesStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.RemoveDegenerates;
    public string Name => "RemoveDegenerates";

    /// <summary>
    /// Squared sine of the smallest angle a triangle may have before it counts as collinear. The
    /// test is scale-invariant, so small-but-valid triangles are kept while true slivers are dropped.
    /// </summary>
    private const float SinSqEpsilon = 1e-12f;

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        int removedTotal = 0;
        foreach (var mesh in scene.Meshes)
        {
            int kept = 0;
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                if (IsDegenerate(mesh, mesh.Faces[i].Indices))
                {
                    removedTotal++;
                    continue;
                }
                if (kept != i)
                    mesh.Faces[kept] = mesh.Faces[i];
                kept++;
            }
            if (kept < mesh.Faces.Count)
                mesh.Faces.RemoveRange(kept, mesh.Faces.Count - kept);
        }

        if (removedTotal > 0)
            context.Log.Info($"Removed {removedTotal} degenerate face(s).", Name);
    }

    private static bool IsDegenerate(IntermediateMesh mesh, int[] indices)
    {
        switch (indices.Length)
        {
            case 0:
                return true;

            case 1:
                return false;

            case 2:
                return indices[0] == indices[1]
                       || mesh.Positions[indices[0]].Equals(mesh.Positions[indices[1]]);

            case 3:
                {
                    int a = indices[0], b = indices[1], c = indices[2];
                    if (a == b || b == c || a == c) return true;

                    Float3 pa = mesh.Positions[a], pb = mesh.Positions[b], pc = mesh.Positions[c];
                    if (pa.Equals(pb) || pb.Equals(pc) || pa.Equals(pc)) return true;

                    Float3 e1 = pb - pa;
                    Float3 e2 = pc - pa;
                    float len1Sq = Float3.Dot(e1, e1);
                    float len2Sq = Float3.Dot(e2, e2);

                    // |e1 x e2|^2 == len1Sq * len2Sq * sin^2(theta). Dividing out the edge lengths
                    // keeps the threshold scale-invariant so only near-collinear triangles are culled.
                    Float3 cross = Float3.Cross(e1, e2);
                    float crossSq = Float3.Dot(cross, cross);
                    return crossSq < len1Sq * len2Sq * SinSqEpsilon;
                }

            default:
                // Polygons should have been triangulated, but for safety: check duplicates.
                for (int i = 0; i < indices.Length; i++)
                {
                    for (int j = i + 1; j < indices.Length; j++)
                        if (indices[i] == indices[j])
                            return true;
                }
                return false;
        }
    }
}
