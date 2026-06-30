using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Splits any mesh that contains a mix of point/line/triangle faces into one mesh per topology.
/// </summary>
/// <remarks>
/// After this step every <see cref="IntermediateMesh"/> has exactly one
/// <see cref="PrimitiveKind"/> set in <see cref="IntermediateMesh.PrimitiveKinds"/>. Source data
/// is duplicated where necessary (positions, normals, tangents, etc.) because most renderers
/// expect a single topology per draw call.
/// </remarks>
internal sealed class SortByPrimitiveTypeStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.SortByPrimitiveType;
    public string Name => "SortByPrimitiveType";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        // Snapshot the mesh list because we'll be appending split-out meshes during the walk.
        int originalCount = scene.Meshes.Count;
        for (int i = 0; i < originalCount; i++)
        {
            var mesh = scene.Meshes[i];
            SplitOne(mesh, scene, i, context);
        }
    }

    private static void SplitOne(IntermediateMesh mesh, IntermediateScene scene, int meshIndex, ImportContext ctx)
    {
        var kinds = mesh.PrimitiveKinds;
        int kindCount = CountFlags(kinds);
        if (kindCount <= 1) return;

        // Bucket faces by topology.
        var pointFaces = new List<IntermediateFace>();
        var lineFaces = new List<IntermediateFace>();
        var triFaces = new List<IntermediateFace>();
        foreach (var face in mesh.Faces)
        {
            (face.Indices.Length switch
            {
                1 => pointFaces,
                2 => lineFaces,
                _ => triFaces,
            }).Add(face);
        }

        // Keep the largest bucket in-place; split the others into clones.
        IntermediateFace[] keep;
        PrimitiveKind keepKind;
        IntermediateFace[]? splitPoints = pointFaces.Count > 0 ? pointFaces.ToArray() : null;
        IntermediateFace[]? splitLines = lineFaces.Count > 0 ? lineFaces.ToArray() : null;
        IntermediateFace[]? splitTris = triFaces.Count > 0 ? triFaces.ToArray() : null;

        if (triFaces.Count >= lineFaces.Count && triFaces.Count >= pointFaces.Count)
        {
            keep = splitTris!;
            keepKind = PrimitiveKind.Triangle;
            splitTris = null;
        }
        else if (lineFaces.Count >= pointFaces.Count)
        {
            keep = splitLines!;
            keepKind = PrimitiveKind.Line;
            splitLines = null;
        }
        else
        {
            keep = splitPoints!;
            keepKind = PrimitiveKind.Point;
            splitPoints = null;
        }

        mesh.Faces.Clear();
        mesh.Faces.AddRange(keep);
        mesh.PrimitiveKinds = keepKind;

        if (splitTris is { Length: > 0 })
            scene.Meshes.Add(CloneWithFaces(mesh, splitTris, PrimitiveKind.Triangle));
        if (splitLines is { Length: > 0 })
            scene.Meshes.Add(CloneWithFaces(mesh, splitLines, PrimitiveKind.Line));
        if (splitPoints is { Length: > 0 })
            scene.Meshes.Add(CloneWithFaces(mesh, splitPoints, PrimitiveKind.Point));

        _ = meshIndex; _ = ctx;
    }

    private static IntermediateMesh CloneWithFaces(IntermediateMesh src, IntermediateFace[] faces, PrimitiveKind kind)
    {
        var dst = new IntermediateMesh
        {
            Name = $"{src.Name}_{kind}",
            MaterialIndex = src.MaterialIndex,
            MaxInfluencesPerVertex = src.MaxInfluencesPerVertex,
            PrimitiveKinds = kind,
        };

        dst.Positions.AddRange(src.Positions);
        if (src.Normals is not null) dst.Normals = new List<Prowl.Vector.Float3>(src.Normals);
        if (src.Tangents is not null) dst.Tangents = new List<Prowl.Vector.Float4>(src.Tangents);
        if (src.Colors0 is not null) dst.Colors0 = new List<Prowl.Vector.Color>(src.Colors0);
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
            if (src.UVs[uv] is { } u)
                dst.UVs[uv] = new List<Prowl.Vector.Float2>(u);

        if (src.VertexJoints is { } vj) dst.VertexJoints = (int[])vj.Clone();
        if (src.VertexWeights is { } vw) dst.VertexWeights = (float[])vw.Clone();

        // Blend shapes belong to the triangle-mesh interpretation only; we share by reference
        // since they're never edited after creation.
        foreach (var bs in src.BlendShapes)
            dst.BlendShapes.Add(bs);

        dst.Faces.AddRange(faces);
        return dst;
    }

    private static int CountFlags(PrimitiveKind k)
    {
        int n = 0;
        if ((k & PrimitiveKind.Point) != 0) n++;
        if ((k & PrimitiveKind.Line) != 0) n++;
        if ((k & PrimitiveKind.Triangle) != 0) n++;
        if ((k & PrimitiveKind.Polygon) != 0) n++;
        return n;
    }
}
