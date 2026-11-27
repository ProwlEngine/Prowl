// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Vector.Geometry;

/// <summary>How <see cref="GeometryData.ToRenderMesh"/> builds its arrays.</summary>
public struct RenderMeshOptions
{
    /// <summary>Gives every face its own vertices, so a stream can colour faces one by one. Normals stay smooth where the mesh says so.</summary>
    public bool SplitFaces;

    /// <summary>Leaves out faces flagged with <see cref="GeometryData.HideAttribute"/>.</summary>
    public bool SkipHidden;

    /// <summary>Shades every face smooth whatever its flag, sharp edges still split.</summary>
    public bool ForceSmooth;

    /// <summary>Winds triangles against their face, as <see cref="GeometryData.ToTriangleMesh"/> does, for renderers whose front faces turn that way.</summary>
    public bool ReverseWinding;

    /// <summary>Only the faces it accepts are drawn. The others still shade their smooth neighbours, so a piece matches the whole.</summary>
    public Predicate<GeometryData.Face>? Emit;
}

/// <summary>
/// A <see cref="GeometryData"/> as indexed triangles for drawing, with the way back to the mesh: the face of every
/// triangle and the vertex and face of every render vertex, as indices into the mesh's lists when it was built.
/// </summary>
public sealed class RenderMesh
{
    public Float3[] Positions = [];
    public Float3[] Normals = [];

    /// <summary>The loop "uv" attribute, null when the mesh has none.</summary>
    public Float2[]? UVs;

    public uint[] Indices = [];

    /// <summary>Index ranges by face material, in rising material order.</summary>
    public (int Material, int IndexStart, int IndexCount)[] SubMeshes = [];

    /// <summary>The mesh face of each triangle.</summary>
    public int[] TriangleFaces = [];

    /// <summary>The mesh vertex of each render vertex.</summary>
    public int[] VertexSources = [];

    /// <summary>The mesh face of each render vertex, -1 where faces share it.</summary>
    public int[] VertexFaces = [];
}

public partial class GeometryData
{
    /// <summary>Vertex, edge and face flag: the element is hidden from view and from picking while editing.</summary>
    public const string HideAttribute = "Hide";

    /// <summary>Face flag: the face is shaded smooth, its normals averaged with the smooth faces around each corner.</summary>
    public const string SmoothAttribute = "Smooth";

    /// <summary>Edge flag: smooth shading breaks at the edge.</summary>
    public const string SharpAttribute = "Sharp";

    /// <summary>Edge attribute, one float from 0 to 1: how much of its line the edge keeps under smooth subdivision.</summary>
    public const string CreaseAttribute = "Crease";

    /// <summary>Loop attribute, two floats: the texture coordinate of the corner.</summary>
    public const string UvAttribute = "uv";

    /// <summary>Edge flag: the UV map is cut along this edge when the mesh is unwrapped.</summary>
    public const string SeamAttribute = "Seam";

    public partial class Vertex
    {
        public bool Hide
        {
            get => ReadFlag(this, HideAttribute);
            set => WriteFlag(this, HideAttribute, value);
        }
    }

    public partial class Edge
    {
        public bool Hide
        {
            get => ReadFlag(this, HideAttribute);
            set => WriteFlag(this, HideAttribute, value);
        }

        public bool IsSharp
        {
            get => ReadFlag(this, SharpAttribute);
            set => WriteFlag(this, SharpAttribute, value);
        }

        public bool IsSeam
        {
            get => ReadFlag(this, SeamAttribute);
            set => WriteFlag(this, SeamAttribute, value);
        }

        public float Crease
        {
            get => TryGetAttribute(CreaseAttribute, out var value) && value is FloatAttributeValue f && f.Data.Length > 0 ? f.Data[0] : 0;
            set => SetAttribute(CreaseAttribute, new FloatAttributeValue(value));
        }
    }

    public partial class Face
    {
        public bool Hide
        {
            get => ReadFlag(this, HideAttribute);
            set => WriteFlag(this, HideAttribute, value);
        }

        public bool IsSmooth
        {
            get => ReadFlag(this, SmoothAttribute);
            set => WriteFlag(this, SmoothAttribute, value);
        }
    }

    /// <summary>
    /// The mesh as indexed triangles with normals and texture coordinates. Flat faces get their own normal, smooth faces
    /// share one across the corners no sharp edge separates, and vertices are split wherever normals or texture
    /// coordinates differ. Triangles wind the way their face does, counter clockwise seen from the side its normal points to.
    /// </summary>
    public RenderMesh ToRenderMesh(RenderMeshOptions options = default)
    {
        var faceNormals = new Float3[Faces.Count];
        var faceSmooth = new bool[Faces.Count];
        var faceDrawn = new bool[Faces.Count];
        for (int i = 0; i < Faces.Count; i++)
        {
            Face face = Faces[i];
            faceNormals[i] = face.Normal();
            faceSmooth[i] = options.ForceSmooth || face.IsSmooth;
            faceDrawn[i] = face.VertCount >= 3 && !(options.SkipHidden && face.Hide);
        }

        bool hasUv = HasLoopAttribute(UvAttribute);
        bool hasMaterial = HasFaceAttribute(MaterialAttribute);

        var positions = new List<Float3>(Loops.Count);
        var normals = new List<Float3>(Loops.Count);
        var uvs = hasUv ? new List<Float2>(Loops.Count) : null;
        var sources = new List<int>(Loops.Count);
        var vertexFaces = new List<int>(Loops.Count);
        var shared = new Dictionary<(int Vertex, int Fan, Float2 Uv), int>();
        var fanNormals = new Dictionary<(int Vertex, int Fan), Float3>();
        var byMaterial = new SortedDictionary<int, (List<uint> Indices, List<int> Faces)>();
        var corners = new List<int>(8);
        var polygon = new List<Float2>(8);

        for (int f = 0; f < Faces.Count; f++)
        {
            if (!faceDrawn[f]) continue;
            Face face = Faces[f];
            if (options.Emit != null && !options.Emit(face)) continue;
            int material = hasMaterial ? face.Material : 0;
            if (!byMaterial.TryGetValue(material, out var target))
                byMaterial[material] = target = (new List<uint>(), new List<int>());

            corners.Clear();
            Loop loop = face.Loop!;
            do
            {
                int v = loop.Vert.Slot;
                Float3 normal = faceNormals[f];
                int fan = -1 - f;
                if (faceSmooth[f])
                {
                    fan = SmoothFan(loop, faceSmooth, faceDrawn);
                    if (!fanNormals.TryGetValue((v, fan), out normal))
                        fanNormals[(v, fan)] = normal = FanNormal(loop, faceNormals, faceSmooth, faceDrawn);
                }
                Float2 uv = default;
                if (hasUv && loop.TryGetAttribute(UvAttribute, out AttributeValue? value) && value is FloatAttributeValue { Data.Length: >= 2 } data)
                    uv = new Float2(data.Data[0], data.Data[1]);

                var key = (v, fan, uv);
                if (options.SplitFaces || !shared.TryGetValue(key, out int index))
                {
                    index = positions.Count;
                    positions.Add(loop.Vert.Point);
                    normals.Add(normal);
                    uvs?.Add(uv);
                    sources.Add(v);
                    vertexFaces.Add(options.SplitFaces || fan < 0 ? f : -1);
                    if (!options.SplitFaces) shared[key] = index;
                }
                corners.Add(index);
                loop = loop.Next!;
            } while (loop != face.Loop);

            if (corners.Count == 4 && FoldsAcrossFirstDiagonal(positions[corners[0]], positions[corners[1]], positions[corners[2]], positions[corners[3]]))
            {
                AddTriangle(target, f, corners[0], corners[1], corners[3], options.ReverseWinding);
                AddTriangle(target, f, corners[1], corners[2], corners[3], options.ReverseWinding);
                continue;
            }

            if (corners.Count <= 4)
            {
                for (int i = 1; i < corners.Count - 1; i++)
                    AddTriangle(target, f, corners[0], corners[i], corners[i + 1], options.ReverseWinding);
                continue;
            }

            // Larger faces may be concave, so they are cut in their own plane
            var frame = new PlaneFrame(faceNormals[f], positions[corners[0]]);
            polygon.Clear();
            foreach (int corner in corners) polygon.Add(frame.To2D(positions[corner]));
            List<int> cut = Polygon2D.Triangulate(polygon);
            if (cut.Count == (corners.Count - 2) * 3)
            {
                for (int i = 0; i < cut.Count; i += 3)
                    AddTriangle(target, f, corners[cut[i]], corners[cut[i + 1]], corners[cut[i + 2]], options.ReverseWinding);
            }
            else
            {
                for (int i = 1; i < corners.Count - 1; i++)
                    AddTriangle(target, f, corners[0], corners[i], corners[i + 1], options.ReverseWinding);
            }
        }

        var indices = new List<uint>();
        var triangleFaces = new List<int>();
        var subMeshes = new List<(int, int, int)>();
        foreach (var (material, (materialIndices, materialFaces)) in byMaterial)
        {
            subMeshes.Add((material, indices.Count, materialIndices.Count));
            indices.AddRange(materialIndices);
            triangleFaces.AddRange(materialFaces);
        }

        return new RenderMesh
        {
            Positions = positions.ToArray(),
            Normals = normals.ToArray(),
            UVs = uvs?.ToArray(),
            Indices = indices.ToArray(),
            SubMeshes = subMeshes.ToArray(),
            TriangleFaces = triangleFaces.ToArray(),
            VertexSources = sources.ToArray(),
            VertexFaces = vertexFaces.ToArray(),
        };
    }

    private static void AddTriangle((List<uint> Indices, List<int> Faces) target, int face, int a, int b, int c, bool reverse)
    {
        target.Indices.Add((uint)a);
        target.Indices.Add((uint)(reverse ? c : b));
        target.Indices.Add((uint)(reverse ? b : c));
        target.Faces.Add(face);
    }

    // The smooth faces around a corner's vertex that no sharp edge, border or flat face separates from the corner's
    // face, walked both ways around the vertex. Calls visit for each, the corner's own face included.
    // True when the second and fourth corner of a quad lie on the same side of the diagonal from the first to the third,
    // so the two triangles along that diagonal would lie over each other. The rule Blender splits its quads by.
    private static bool FoldsAcrossFirstDiagonal(Float3 a, Float3 b, Float3 c, Float3 d)
    {
        Float3 diagonal = c - a;
        return Float3.Dot(Float3.Cross(b - a, diagonal), Float3.Cross(d - a, diagonal)) > 0;
    }

    private static void WalkFan(Loop corner, bool[] faceSmooth, bool[] faceDrawn, Action<Loop> visit)
    {
        visit(corner);
        for (int direction = 0; direction < 2; direction++)
        {
            // The edge leaving the corner one way, the edge arriving at it the other way
            Edge edge = direction == 0 ? corner.Edge : corner.Prev!.Edge;
            Loop at = corner;
            while (true)
            {
                if (edge.IsSharp) break;
                Loop? across = Across(edge, at.Face);
                if (across == null) break;
                Loop next = across.Vert == corner.Vert ? across : across.Next!;
                if (next.Vert != corner.Vert) break;
                if (next == corner) return;
                int face = next.Face.Slot;
                if (!faceSmooth[face] || !faceDrawn[face]) break;
                visit(next);
                edge = next.Edge == edge ? next.Prev!.Edge : next.Edge;
                at = next;
            }
        }
    }

    // The loop of the one other face on a manifold edge, null on a border or where more than two faces meet
    private static Loop? Across(Edge edge, Face face)
    {
        Loop? first = edge.Loop;
        if (first == null || first.RadialNext == first) return null;
        if (first.RadialNext!.RadialNext != first) return null;
        return first.Face == face ? first.RadialNext : first;
    }

    // Fans are named by the lowest face in them, so every corner of a fan agrees
    private static int SmoothFan(Loop corner, bool[] faceSmooth, bool[] faceDrawn)
    {
        int lowest = int.MaxValue;
        WalkFan(corner, faceSmooth, faceDrawn, l => lowest = Math.Min(lowest, l.Face.Slot));
        return lowest;
    }

    private static Float3 FanNormal(Loop corner, Float3[] faceNormals, bool[] faceSmooth, bool[] faceDrawn)
    {
        Float3 sum = Float3.Zero;
        WalkFan(corner, faceSmooth, faceDrawn, l =>
        {
            // Weighted by the corner's angle, so how a face is cut into triangles does not tilt the normal
            Float3 a = Float3.Normalize(l.Next!.Vert.Point - l.Vert.Point);
            Float3 b = Float3.Normalize(l.Prev!.Vert.Point - l.Vert.Point);
            float angle = MathF.Acos(Math.Clamp(Float3.Dot(a, b), -1f, 1f));
            sum += faceNormals[l.Face.Slot] * angle;
        });
        return Float3.Dot(sum, sum) > 1e-12f ? Float3.Normalize(sum) : faceNormals[corner.Face.Slot];
    }
}
