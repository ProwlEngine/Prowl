// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>Finding the parts of a mesh under a ray, for picking and for telling what is in sight.</summary>
public static class MeshPicking
{
    /// <summary>
    /// The nearest face a world ray meets, either side, and how far along the ray in world units. Faces are tested as fans
    /// of triangles from their first corner.
    /// </summary>
    public static bool Raycast(GeometryData mesh, Float4x4 toWorld, Ray ray, out GeometryData.Face? hit, out float distance,
                               Func<GeometryData.Face, bool>? skip = null)
    {
        hit = null;
        distance = float.MaxValue;
        Float4x4 toLocal = toWorld.Invert();
        Float3 origin = Float4x4.TransformPoint(ray.Origin, toLocal);
        Float3 direction = Float3.NormalizeSafe(Float4x4.TransformNormal(ray.Direction, toLocal), Float3.UnitZ);

        foreach (GeometryData.Face face in mesh.Faces)
        {
            if (skip != null && skip(face) || face.Loop == null) continue;
            GeometryData.Loop first = face.Loop;
            GeometryData.Loop loop = first.Next!;
            while (loop.Next != null && loop.Next != first)
            {
                if (Intersection.RayTriangle(origin, direction, first.Vert.Point, loop.Vert.Point, loop.Next.Vert.Point, out float t, out _, out _))
                {
                    float world = Float3.Distance(ray.Origin, Float4x4.TransformPoint(origin + direction * t, toWorld));
                    if (world < distance)
                    {
                        distance = world;
                        hit = face;
                    }
                }
                loop = loop.Next;
            }
        }
        return hit != null;
    }

    /// <summary>
    /// Whether a point of the mesh is in sight from a camera at <paramref name="eye"/>: nothing of the meshes tested lies
    /// between them. A point on the face it belongs to counts as seen.
    /// </summary>
    public static bool Seen(Float3 worldPoint, Float3 eye, Func<Ray, float> nearestHit)
    {
        Float3 toPoint = worldPoint - eye;
        float length = Float3.Length(toPoint);
        if (length < 1e-6f) return true;
        float hit = nearestHit(new Ray(eye, toPoint / length));
        return hit >= length - MathF.Max(1e-3f, length * 1e-4f);
    }
}

/// <summary>
/// A bounding volume tree over a mesh's faces, cut into fans of triangles as <see cref="MeshPicking.Raycast"/> cuts
/// them, so a ray meets the same face at the same distance in far fewer tests. One is kept per mesh. When only points
/// moved its boxes are fitted round them again, when faces changed it is built again.
/// </summary>
public sealed class MeshBvh
{
    private const int LeafSize = 4;

    private struct Node
    {
        public Float3 Min, Max;
        // A leaf's first triangle and how many. An inner node has no count, its first child right after it and its
        // second at the start
        public int Start, Count;
    }

    private readonly GeometryData.Face[] _faces;
    private readonly GeometryData.Vertex[] _a, _b, _c;
    private readonly List<Node> _nodes = new();
    private readonly int _version;
    private ulong _stamp;

    private static readonly ConditionalWeakTable<GeometryData, MeshBvh> s_trees = new();

    /// <summary>The tree of a mesh, fitted again when points moved and built again when faces changed since the last.</summary>
    public static MeshBvh For(GeometryData mesh)
    {
        ulong stamp = Stamp(mesh);
        if (s_trees.TryGetValue(mesh, out MeshBvh? tree) && tree._version == mesh.Version && tree._faces.Length == Triangles(mesh))
        {
            if (tree._stamp != stamp) tree.Refit(stamp);
            return tree;
        }
        tree = new MeshBvh(mesh, stamp);
        s_trees.AddOrUpdate(mesh, tree);
        return tree;
    }

    private static int Triangles(GeometryData mesh)
    {
        int count = 0;
        foreach (GeometryData.Face face in mesh.Faces)
            if (face.Loop != null) count += Math.Max(0, face.VertCount - 2);
        return count;
    }

    // Every point's bits mixed together with the counts, so moving any point changes it
    private static ulong Stamp(GeometryData mesh)
    {
        ulong hash = 14695981039346656037UL ^ (ulong)mesh.Vertices.Count ^ ((ulong)mesh.Faces.Count << 32);
        foreach (GeometryData.Vertex vertex in mesh.Vertices)
        {
            Float3 p = vertex.Point;
            hash = (hash ^ BitConverter.SingleToUInt32Bits(p.X)) * 1099511628211UL;
            hash = (hash ^ BitConverter.SingleToUInt32Bits(p.Y)) * 1099511628211UL;
            hash = (hash ^ BitConverter.SingleToUInt32Bits(p.Z)) * 1099511628211UL;
        }
        return hash;
    }

    private MeshBvh(GeometryData mesh, ulong stamp)
    {
        _version = mesh.Version;
        _stamp = stamp;
        var faces = new List<GeometryData.Face>();
        var a = new List<GeometryData.Vertex>();
        var b = new List<GeometryData.Vertex>();
        var c = new List<GeometryData.Vertex>();
        foreach (GeometryData.Face face in mesh.Faces)
        {
            if (face.Loop == null) continue;
            GeometryData.Loop first = face.Loop;
            for (GeometryData.Loop loop = first.Next!; loop.Next != null && loop.Next != first; loop = loop.Next)
            {
                faces.Add(face);
                a.Add(first.Vert);
                b.Add(loop.Vert);
                c.Add(loop.Next.Vert);
            }
        }

        int[] order = new int[faces.Count];
        var centres = new float[3][];
        for (int axis = 0; axis < 3; axis++) centres[axis] = new float[faces.Count];
        var mins = new Float3[faces.Count];
        var maxs = new Float3[faces.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
            Float3 pa = a[i].Point, pb = b[i].Point, pc = c[i].Point;
            mins[i] = Maths.Min(pa, Maths.Min(pb, pc));
            maxs[i] = Maths.Max(pa, Maths.Max(pb, pc));
            Float3 centre = (pa + pb + pc) / 3f;
            centres[0][i] = centre.X;
            centres[1][i] = centre.Y;
            centres[2][i] = centre.Z;
        }
        if (order.Length > 0) Build(order, centres, mins, maxs, 0, order.Length);

        // Triangles in tree order, so a leaf's are side by side
        _faces = new GeometryData.Face[order.Length];
        _a = new GeometryData.Vertex[order.Length];
        _b = new GeometryData.Vertex[order.Length];
        _c = new GeometryData.Vertex[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            _faces[i] = faces[order[i]];
            _a[i] = a[order[i]];
            _b[i] = b[order[i]];
            _c[i] = c[order[i]];
        }
    }

    private int Build(int[] order, float[][] centres, Float3[] mins, Float3[] maxs, int start, int count)
    {
        Float3 min = new(float.MaxValue), max = new(float.MinValue), centreMin = new(float.MaxValue), centreMax = new(float.MinValue);
        for (int i = start; i < start + count; i++)
        {
            int t = order[i];
            min = Maths.Min(min, mins[t]);
            max = Maths.Max(max, maxs[t]);
            var centre = new Float3(centres[0][t], centres[1][t], centres[2][t]);
            centreMin = Maths.Min(centreMin, centre);
            centreMax = Maths.Max(centreMax, centre);
        }
        int index = _nodes.Count;
        _nodes.Add(new Node { Min = min, Max = max, Start = start, Count = count });
        if (count <= LeafSize) return index;

        Float3 extent = centreMax - centreMin;
        int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        int half = count / 2;
        Select(order, centres[axis], start, start + count - 1, start + half);
        Build(order, centres, mins, maxs, start, half);
        int second = Build(order, centres, mins, maxs, start + half, count - half);
        _nodes[index] = new Node { Min = min, Max = max, Start = second, Count = 0 };
        return index;
    }

    // Puts the triangle that belongs at place k by its centre there, the ones before it no greater and after no smaller
    private static void Select(int[] order, float[] keys, int left, int right, int k)
    {
        while (left < right)
        {
            float pivot = keys[order[(left + right) >> 1]];
            int i = left, j = right;
            while (i <= j)
            {
                while (keys[order[i]] < pivot) i++;
                while (keys[order[j]] > pivot) j--;
                if (i > j) break;
                (order[i], order[j]) = (order[j], order[i]);
                i++;
                j--;
            }
            if (k <= j) right = j;
            else if (k >= i) left = i;
            else return;
        }
    }

    // Fits every box round the triangles' points as they are now, children before their parents
    private void Refit(ulong stamp)
    {
        _stamp = stamp;
        for (int at = _nodes.Count - 1; at >= 0; at--)
        {
            Node node = _nodes[at];
            if (node.Count > 0)
            {
                Float3 min = new(float.MaxValue), max = new(float.MinValue);
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    min = Maths.Min(min, Maths.Min(_a[i].Point, Maths.Min(_b[i].Point, _c[i].Point)));
                    max = Maths.Max(max, Maths.Max(_a[i].Point, Maths.Max(_b[i].Point, _c[i].Point)));
                }
                node.Min = min;
                node.Max = max;
            }
            else
            {
                Node first = _nodes[at + 1], second = _nodes[node.Start];
                node.Min = Maths.Min(first.Min, second.Min);
                node.Max = Maths.Max(first.Max, second.Max);
            }
            _nodes[at] = node;
        }
    }

    /// <summary>The nearest face a world ray meets, as <see cref="MeshPicking.Raycast"/> finds it.</summary>
    public bool Raycast(Float4x4 toWorld, Ray ray, out GeometryData.Face? hit, out float distance, Func<GeometryData.Face, bool>? skip = null)
    {
        hit = null;
        distance = float.MaxValue;
        if (_nodes.Count == 0) return false;
        Float4x4 toLocal = toWorld.Invert();
        Float3 origin = Float4x4.TransformPoint(ray.Origin, toLocal);
        Float3 direction = Float3.NormalizeSafe(Float4x4.TransformNormal(ray.Direction, toLocal), Float3.UnitZ);
        var inverse = new Float3(Inverse(direction.X), Inverse(direction.Y), Inverse(direction.Z));

        float nearestLocal = float.MaxValue;
        Span<int> stack = stackalloc int[128];
        int depth = 0;
        stack[depth++] = 0;
        while (depth > 0)
        {
            int at = stack[--depth];
            Node node = _nodes[at];
            if (!Meets(node.Min, node.Max, origin, inverse, nearestLocal)) continue;
            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    if (skip != null && skip(_faces[i])) continue;
                    if (!Intersection.RayTriangle(origin, direction, _a[i].Point, _b[i].Point, _c[i].Point, out float t, out _, out _)) continue;
                    float world = Float3.Distance(ray.Origin, Float4x4.TransformPoint(origin + direction * t, toWorld));
                    if (world >= distance) continue;
                    distance = world;
                    nearestLocal = t;
                    hit = _faces[i];
                }
                continue;
            }
            stack[depth++] = node.Start;
            stack[depth++] = at + 1;
        }
        return hit != null;
    }

    private static float Inverse(float d) => 1f / (MathF.Abs(d) < 1e-12f ? (d < 0f ? -1e-12f : 1e-12f) : d);

    private static bool Meets(Float3 min, Float3 max, Float3 origin, Float3 inverse, float limit)
    {
        float t1 = (min.X - origin.X) * inverse.X, t2 = (max.X - origin.X) * inverse.X;
        float near = MathF.Min(t1, t2), far = MathF.Max(t1, t2);
        t1 = (min.Y - origin.Y) * inverse.Y;
        t2 = (max.Y - origin.Y) * inverse.Y;
        near = MathF.Max(near, MathF.Min(t1, t2));
        far = MathF.Min(far, MathF.Max(t1, t2));
        t1 = (min.Z - origin.Z) * inverse.Z;
        t2 = (max.Z - origin.Z) * inverse.Z;
        near = MathF.Max(near, MathF.Min(t1, t2));
        far = MathF.Min(far, MathF.Max(t1, t2));
        return far >= MathF.Max(near, 0f) && near <= limit;
    }
}
