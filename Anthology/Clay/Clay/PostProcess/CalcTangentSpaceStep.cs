using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Generates per-vertex tangents from positions, normals and UV0 using the algorithm from
/// Eric Lengyel's "Computing Tangent Space Basis Vectors for an Arbitrary Mesh".
/// </summary>
/// <remarks>
/// Tangent stream layout matches MikkTSpace: <c>(tangent.xyz, bitangent_sign)</c>. The bitangent
/// is reconstructed in the shader as <c>cross(normal, tangent.xyz) * tangent.w</c>.
/// <para>
/// The full MikkTSpace algorithm gives bit-exact results for mirrored UV islands and is preferred
/// for authoring pipelines that pre-bake their tangents the same way. The Lengyel method used here
/// produces visually correct results for typical game content and avoids the ~1500 lines of C
/// from the reference. A future phase may swap in the MikkT port behind the same flag.
/// </para>
/// Skips meshes that have no UVs or no normals (tangents have no meaning without both) and meshes
/// that already supply tangents.
/// </remarks>
internal sealed class CalcTangentSpaceStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.CalcTangentSpace;
    public string Name => "CalcTangentSpace";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        foreach (var mesh in scene.Meshes)
        {
            if (mesh.Tangents is { Count: > 0 })
                continue; // Source supplied tangents - keep them.

            if (mesh.Normals is null || mesh.UVs[0] is null)
                continue;
            if ((mesh.PrimitiveKinds & PrimitiveKind.Triangle) == 0)
                continue; // Tangent space only makes sense on triangulated meshes.

            int vertexCount = mesh.Positions.Count;
            var tan1 = new Float3[vertexCount];
            var tan2 = new Float3[vertexCount];

            var positions = mesh.Positions;
            var normals = mesh.Normals;
            var uvs = mesh.UVs[0]!;

            foreach (var face in mesh.Faces)
            {
                if (face.Indices.Length != 3) continue;
                int i1 = face.Indices[0];
                int i2 = face.Indices[1];
                int i3 = face.Indices[2];

                Float3 v1 = positions[i1];
                Float3 v2 = positions[i2];
                Float3 v3 = positions[i3];

                Float2 w1 = uvs[i1];
                Float2 w2 = uvs[i2];
                Float2 w3 = uvs[i3];

                float x1 = v2.X - v1.X, x2 = v3.X - v1.X;
                float y1 = v2.Y - v1.Y, y2 = v3.Y - v1.Y;
                float z1 = v2.Z - v1.Z, z2 = v3.Z - v1.Z;

                float s1 = w2.X - w1.X, s2 = w3.X - w1.X;
                float t1 = w2.Y - w1.Y, t2 = w3.Y - w1.Y;

                float denom = s1 * t2 - s2 * t1;
                if (MathF.Abs(denom) < 1e-12f) continue;
                float r = 1f / denom;

                var sdir = new Float3(
                    (t2 * x1 - t1 * x2) * r,
                    (t2 * y1 - t1 * y2) * r,
                    (t2 * z1 - t1 * z2) * r);
                var tdir = new Float3(
                    (s1 * x2 - s2 * x1) * r,
                    (s1 * y2 - s2 * y1) * r,
                    (s1 * z2 - s2 * z1) * r);

                tan1[i1] = Add(tan1[i1], sdir);
                tan1[i2] = Add(tan1[i2], sdir);
                tan1[i3] = Add(tan1[i3], sdir);

                tan2[i1] = Add(tan2[i1], tdir);
                tan2[i2] = Add(tan2[i2], tdir);
                tan2[i3] = Add(tan2[i3], tdir);
            }

            var result = new List<Float4>(vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                Float3 n = normals[i];
                Float3 t = tan1[i];

                // Gram-Schmidt orthogonalize: tangent_perp = normalize(t - n * (n.t)).
                Float3 tangent = Normalize(Subtract(t, Scale(n, Dot(n, t))));
                // Bitangent handedness from cross-product sign.
                float w = (Dot(Cross(n, t), tan2[i]) < 0f) ? -1f : 1f;
                result.Add(new Float4(tangent.X, tangent.Y, tangent.Z, w));
            }

            mesh.Tangents = result;
        }

        _ = context;
    }

    private static Float3 Add(Float3 a, Float3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static Float3 Subtract(Float3 a, Float3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static Float3 Scale(Float3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
    private static float Dot(Float3 a, Float3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Float3 Cross(Float3 a, Float3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static Float3 Normalize(Float3 v)
    {
        float len = MathF.Sqrt(Dot(v, v));
        return len < 1e-12f ? new Float3(1f, 0f, 0f) : new Float3(v.X / len, v.Y / len, v.Z / len);
    }
}
