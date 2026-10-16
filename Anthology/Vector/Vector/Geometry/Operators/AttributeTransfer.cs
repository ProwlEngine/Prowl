// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;


namespace Prowl.Vector.Geometry.Operators;

using AttributeMap = Dictionary<string, GeometryData.AttributeValue>;

/// <summary>
/// A face as it was before an operator replaced it, so what it carried can be given to whatever takes its place.
/// Holds the face attributes, and the loop attributes of every corner, which can be read back by vertex or
/// sampled at any point on the face.
/// </summary>
internal sealed class FaceSnapshot
{
    public AttributeMap FaceAttributes = new();
    public readonly List<GeometryData.Vertex> Vertices = new();
    public readonly List<AttributeMap> Loops = new();

    private PlaneFrame _frame;
    private List<Float2>? _flat;
    private List<int>? _triangles;

    public static FaceSnapshot Capture(GeometryData mesh, GeometryData.Face face)
    {
        var snapshot = new FaceSnapshot();
        snapshot.FaceAttributes = AttributeTransfer.Copy(face.Attributes);
        var loop = face.Loop;
        if (loop == null) return snapshot;
        do
        {
            snapshot.Vertices.Add(loop.Vert);
            snapshot.Loops.Add(AttributeTransfer.Copy(loop.Attributes));
            loop = loop.Next;
        } while (loop != face.Loop && loop != null);
        return snapshot;
    }

    private FaceSnapshot() { }

    /// <summary>The loop attributes of the corner at a vertex, or null when the face had no corner there.</summary>
    public AttributeMap? LoopOf(GeometryData.Vertex vertex)
    {
        int i = Vertices.IndexOf(vertex);
        return i < 0 ? null : Loops[i];
    }

    /// <summary>Copies the face attributes, except any named.</summary>
    public void ApplyFaceAttributes(GeometryData.Face target, string? except = null)
    {
        foreach (var pair in FaceAttributes)
        {
            if (pair.Key == except) continue;
            target.Attributes[pair.Key] = GeometryData.AttributeValue.Copy(pair.Value);
        }
    }

    /// <summary>Gives each corner of the target the attributes the source function returns for its vertex.</summary>
    public static void ApplyLoops(GeometryData.Face target, Func<GeometryData.Vertex, AttributeMap?> source)
    {
        var loop = target.Loop;
        if (loop == null) return;
        do
        {
            AttributeMap? attributes = source(loop.Vert);
            if (attributes != null)
                foreach (var pair in attributes)
                    loop.Attributes[pair.Key] = GeometryData.AttributeValue.Copy(pair.Value);
            loop = loop.Next;
        } while (loop != target.Loop && loop != null);
    }

    /// <summary>Gives each corner of the target the attributes this face had at that point, which keeps texture coordinates continuous.</summary>
    public void ApplyLoopsByPosition(GeometryData mesh, GeometryData.Face target) =>
        ApplyLoops(target, v => Sample(mesh, v.Point));

    /// <summary>How far a point is from the plane of the face, or infinity when the face has no plane.</summary>
    public float PlaneDistance(Float3 point)
    {
        if (Vertices.Count < 3) return float.PositiveInfinity;
        EnsureFrame();
        return _flat == null ? float.PositiveInfinity : MathF.Abs(_frame.Distance(point));
    }

    private void EnsureFrame()
    {
        if (_flat != null || Vertices.Count < 3) return;
        Float3 normal = Float3.Zero;
        for (int i = 0; i < Vertices.Count; i++) normal += Float3.Cross(Vertices[i].Point, Vertices[(i + 1) % Vertices.Count].Point);
        if (Float3.LengthSquared(normal) < 1e-12f) return;
        _frame = new PlaneFrame(Float3.Normalize(normal), Vertices[0].Point);
        _flat = Vertices.Select(v => _frame.To2D(v.Point)).ToList();
        _triangles = Polygon2D.Triangulate(_flat);
    }

    /// <summary>The loop attributes at a point on the face. Float attributes are interpolated across the face, integer ones come from the nearest corner.</summary>
    public AttributeMap Sample(GeometryData mesh, Float3 point)
    {
        if (Vertices.Count == 0) return new AttributeMap();

        int nearest = 0;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < Vertices.Count; i++)
        {
            float d = Float3.Distance(Vertices[i].Point, point);
            if (d < nearestDistance) { nearestDistance = d; nearest = i; }
        }
        if (nearestDistance < 1e-6f || Vertices.Count < 3) return AttributeTransfer.Copy(Loops[nearest]);

        EnsureFrame();
        if (_flat == null) return AttributeTransfer.Copy(Loops[nearest]);

        Float2 p = _frame.To2D(point);
        float bestScore = float.NegativeInfinity;
        int bestA = 0, bestB = 1, bestC = 2;
        Float3 bestWeights = new(1, 0, 0);
        for (int t = 0; t + 2 < _triangles!.Count; t += 3)
        {
            int a = _triangles[t], b = _triangles[t + 1], c = _triangles[t + 2];
            Float3? weights = Barycentric(p, _flat[a], _flat[b], _flat[c]);
            if (weights is not Float3 w) continue;
            float score = Maths.Min(w.X, Maths.Min(w.Y, w.Z));
            if (score <= bestScore) continue;
            bestScore = score;
            bestA = a; bestB = b; bestC = c;
            bestWeights = w;
        }
        if (float.IsNegativeInfinity(bestScore)) return AttributeTransfer.Copy(Loops[nearest]);

        // A point outside the triangle is pulled back onto it
        float x = Maths.Max(0f, bestWeights.X), y = Maths.Max(0f, bestWeights.Y), z = Maths.Max(0f, bestWeights.Z);
        float sum = x + y + z;
        x /= sum; y /= sum; z /= sum;
        int dominant = x >= y && x >= z ? bestA : y >= z ? bestB : bestC;

        var result = new AttributeMap();
        foreach (var definition in mesh.LoopAttributes)
        {
            if (!Loops[bestA].TryGetValue(definition.Name, out var va) ||
                !Loops[bestB].TryGetValue(definition.Name, out var vb) ||
                !Loops[bestC].TryGetValue(definition.Name, out var vc)) continue;

            if (va is GeometryData.FloatAttributeValue fa && vb is GeometryData.FloatAttributeValue fb && vc is GeometryData.FloatAttributeValue fc)
            {
                var data = new float[fa.Data.Length];
                for (int k = 0; k < data.Length; k++) data[k] = fa.Data[k] * x + fb.Data[k] * y + fc.Data[k] * z;
                result[definition.Name] = new GeometryData.FloatAttributeValue { Data = data };
            }
            else if (Loops[dominant].TryGetValue(definition.Name, out var nearestValue))
            {
                result[definition.Name] = GeometryData.AttributeValue.Copy(nearestValue);
            }
        }
        return result;
    }

    private static Float3? Barycentric(Float2 p, Float2 a, Float2 b, Float2 c)
    {
        Float2 v0 = b - a, v1 = c - a, v2 = p - a;
        float denominator = v0.X * v1.Y - v1.X * v0.Y;
        if (MathF.Abs(denominator) < 1e-12f) return null;
        float v = (v2.X * v1.Y - v1.X * v2.Y) / denominator;
        float w = (v0.X * v2.Y - v2.X * v0.Y) / denominator;
        return new Float3(1f - v - w, v, w);
    }
}

internal static class AttributeTransfer
{
    public static AttributeMap Copy(AttributeMap source)
    {
        var copy = new AttributeMap(source.Count);
        foreach (var pair in source) copy[pair.Key] = GeometryData.AttributeValue.Copy(pair.Value);
        return copy;
    }

    public static void Apply(AttributeMap source, AttributeMap target)
    {
        foreach (var pair in source) target[pair.Key] = GeometryData.AttributeValue.Copy(pair.Value);
    }

    /// <summary>Interpolates every attribute two maps share. Float attributes blend, integer attributes take whichever side is nearer.</summary>
    public static AttributeMap Lerp(AttributeMap a, AttributeMap b, float t)
    {
        var result = new AttributeMap();
        foreach (var pair in a)
        {
            if (!b.TryGetValue(pair.Key, out var other)) { result[pair.Key] = GeometryData.AttributeValue.Copy(pair.Value); continue; }
            if (pair.Value is GeometryData.FloatAttributeValue fa && other is GeometryData.FloatAttributeValue fb && fa.Data.Length == fb.Data.Length)
            {
                var data = new float[fa.Data.Length];
                for (int i = 0; i < data.Length; i++) data[i] = fa.Data[i] + (fb.Data[i] - fa.Data[i]) * t;
                result[pair.Key] = new GeometryData.FloatAttributeValue { Data = data };
            }
            else
            {
                result[pair.Key] = GeometryData.AttributeValue.Copy(t < 0.5f ? pair.Value : other);
            }
        }
        return result;
    }

    /// <summary>The average of several maps, for a corner that sits in the middle of a face.</summary>
    public static AttributeMap Average(IReadOnlyList<AttributeMap> maps)
    {
        if (maps.Count == 0) return new AttributeMap();
        AttributeMap result = Copy(maps[0]);
        for (int i = 1; i < maps.Count; i++) result = Lerp(result, maps[i], 1f / (i + 1));
        return result;
    }
}
