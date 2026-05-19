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

    /// <summary>Triangles with area below this threshold are treated as degenerate.</summary>
    private const float AreaEpsilon = 1e-12f;

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
                Float3 e1 = Sub(mesh.Positions[b], mesh.Positions[a]);
                Float3 e2 = Sub(mesh.Positions[c], mesh.Positions[a]);
                Float3 cross = Cross(e1, e2);
                float twiceAreaSq = cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z;
                return twiceAreaSq < AreaEpsilon;
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

    private static Float3 Sub(Float3 a, Float3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static Float3 Cross(Float3 a, Float3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
