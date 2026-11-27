// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>What a transform modal moves, each call measured from where things started.</summary>
public interface ITransformSession
{
    /// <summary>Where the moving things started, in the world.</summary>
    IReadOnlyList<Float3> Points { get; }

    Float3 Median { get; }

    Float3 Pivot { get; }

    void Translate(Float3 delta);

    /// <summary>Turns by an angle in radians, by the right hand rule about the axis.</summary>
    void Rotate(Float3 axis, float angle);

    /// <summary>Scales along three axes turned by a rotation.</summary>
    void Scale(Float3 factors, Quaternion axes);

    void Restore();
}

/// <summary>
/// Moves the selected points of an edit target, and can put them back. Every move is measured from where the points
/// started, in world space, so a modal transform or a gizmo calls it again each frame with the whole amount. With a
/// falloff, unselected points within it are pulled along too, less the further they are from the selection.
/// </summary>
public sealed class TransformSession : ITransformSession
{
    private readonly IReadOnlyList<(EditTarget Target, ElementSelection Selection)> _parts;
    private readonly List<(GeometryData.Vertex Vertex, Float3 Local, Float3 World, Float3 Origin, float Weight, Float4x4 ToLocal, (int Axis, float Offset)? Clip)> _points = [];
    private readonly List<Float3> _selected = [];
    private float _falloff;
    private Func<Float3, Float3, Float3>? _last;

    public TransformSession(EditTarget target, ElementSelection selection, float falloff = 0f)
        : this([(target, selection)], falloff)
    {
    }

    /// <summary>A session over several objects at once, their points moving together in the world about one pivot.</summary>
    public TransformSession(IReadOnlyList<(EditTarget Target, ElementSelection Selection)> parts, float falloff = 0f)
    {
        _parts = parts;
        _falloff = falloff;
        Gather();

        Float3 sum = Float3.Zero;
        foreach (Float3 point in _selected) sum += point;
        Median = _selected.Count > 0 ? sum / _selected.Count : Float3.Zero;
        Pivot = Median;
    }

    /// <summary>How many selected points move.</summary>
    public int Count => _selected.Count;

    /// <summary>Where the selected points started, in the world.</summary>
    public IReadOnlyList<Float3> Points => _selected;

    /// <summary>The middle of the moving points, in the world.</summary>
    public Float3 Median { get; }

    /// <summary>What rotation and scale turn about, in the world. The median unless set.</summary>
    public Float3 Pivot { get; set; }

    /// <summary>Rotation and scale turn each island of selected faces about its own centre instead of the pivot.</summary>
    public bool Individual { get; set; }

    /// <summary>How far from the selection unselected points are still pulled along, 0 for not at all. Changing it moves the points again.</summary>
    public float Falloff
    {
        get => _falloff;
        set
        {
            if (value == _falloff) return;
            Restore();
            _falloff = value;
            Gather();
            if (_last != null) Apply(_last);
        }
    }

    public void Translate(Float3 delta) => Apply((p, _) => p + delta);

    /// <summary>Turns by an angle in radians, by the right hand rule about the axis.</summary>
    public void Rotate(Float3 axis, float angle) => Rotate(Quaternion.AxisAngle(Float3.Normalize(axis), angle));

    public void Rotate(Quaternion turn) => Apply((p, origin) => origin + turn * (p - origin));

    /// <summary>Scales along three axes turned by a rotation.</summary>
    public void Scale(Float3 factors, Quaternion axes)
    {
        Quaternion back = Quaternion.Inverse(axes);
        Apply((p, origin) => origin + axes * ((back * (p - origin)) * factors));
    }

    public void Restore()
    {
        foreach ((GeometryData.Vertex Vertex, Float3 Local, Float3 World, Float3 Origin, float Weight, Float4x4 ToLocal, (int Axis, float Offset)? Clip) point in _points) point.Vertex.Point = point.Local;
    }

    private void Apply(Func<Float3, Float3, Float3> move)
    {
        _last = move;
        foreach ((GeometryData.Vertex vertex, Float3 local, Float3 world, Float3 origin, float weight, Float4x4 toLocal, (int Axis, float Offset)? clip) in _points)
        {
            Float3 moved = move(world, Individual ? origin : Pivot);
            Float3 point = Float4x4.TransformPoint(world + (moved - world) * weight, toLocal);
            if (clip is ({ } axis, { } offset) && (MathF.Abs(local[axis] - offset) < ClipDistance || local[axis] > offset != point[axis] > offset))
                point[axis] = offset;
            vertex.Point = point;
        }
    }

    // How near the mirror plane a point counts as on it
    private const float ClipDistance = 1e-4f;

    /// <summary>The selected points at full weight, and with a falloff the unselected points within it, weighted by a smooth curve.</summary>
    private void Gather()
    {
        _points.Clear();
        _selected.Clear();
        foreach ((EditTarget target, ElementSelection selection) in _parts)
        {
            Float4x4 toWorld = target.LocalToWorld, toLocal = toWorld.Invert();
            Dictionary<GeometryData.Vertex, Float3> origins = IslandCentres(selection, toWorld);
            foreach (GeometryData.Vertex vertex in selection.Vertices)
            {
                Float3 world = Float4x4.TransformPoint(vertex.Point, toWorld);
                _points.Add((vertex, vertex.Point, world, origins.TryGetValue(vertex, out Float3 origin) ? origin : world, 1f, toLocal, target.MirrorClip));
                _selected.Add(world);
            }
        }
        if (_falloff <= 0f || _selected.Count == 0) return;

        // Unselected points of every object are pulled by the nearest selected point of any of them
        foreach ((EditTarget target, ElementSelection selection) in _parts)
        {
            Float4x4 toWorld = target.LocalToWorld, toLocal = toWorld.Invert();
            foreach (GeometryData.Vertex vertex in target.Mesh.Vertices)
            {
                if (selection.Vertices.Contains(vertex)) continue;
                Float3 world = Float4x4.TransformPoint(vertex.Point, toWorld);
                float nearest = float.MaxValue;
                foreach (Float3 point in _selected) nearest = MathF.Min(nearest, Float3.DistanceSquared(world, point));
                float distance = MathF.Sqrt(nearest);
                if (distance >= _falloff) continue;

                float t = 1f - distance / _falloff;
                _points.Add((vertex, vertex.Point, world, world, t * t * (3f - 2f * t), toLocal, target.MirrorClip));
            }
        }
    }

    /// <summary>The centre of each island of selected faces joined by edges, for each point of it.</summary>
    private static Dictionary<GeometryData.Vertex, Float3> IslandCentres(ElementSelection selection, Float4x4 toWorld)
    {
        var centres = new Dictionary<GeometryData.Vertex, Float3>();
        foreach (List<GeometryData.Face> region in GeometryOperators.FaceRegions(selection.Faces))
        {
            var island = new HashSet<GeometryData.Vertex>(region.SelectMany(f => f.NeighborVertices()));
            Float3 centre = Float4x4.TransformPoint(GeometryData.Centroid(island), toWorld);
            foreach (GeometryData.Vertex vertex in island) centres[vertex] = centre;
        }
        return centres;
    }
}

/// <summary>Whole objects moved, turned and scaled in the world about the middle of where they stand.</summary>
public sealed class ObjectTransformSession : ITransformSession
{
    private readonly List<(Transform Transform, Float3 Position, Quaternion Rotation, Float3 Scale)> _start = [];
    private readonly List<Float3> _points = [];

    public ObjectTransformSession(IEnumerable<GameObject> objects)
    {
        foreach (GameObject go in objects)
        {
            Transform transform = go.Transform;
            _start.Add((transform, transform.Position, transform.Rotation, transform.LocalScale));
            _points.Add(transform.Position);
        }
        Float3 sum = Float3.Zero;
        foreach (Float3 point in _points) sum += point;
        Median = _points.Count > 0 ? sum / _points.Count : Float3.Zero;
        Pivot = Median;
    }

    public IReadOnlyList<Float3> Points => _points;

    public Float3 Median { get; }

    /// <summary>What rotation and scale turn about, in the world. The median unless set.</summary>
    public Float3 Pivot { get; set; }

    public void Translate(Float3 delta)
    {
        Restore();
        foreach ((Transform Transform, Float3 Position, Quaternion Rotation, Float3 Scale) start in _start) start.Transform.Position = start.Position + delta;
    }

    public void Rotate(Float3 axis, float angle)
    {
        Quaternion turn = Quaternion.AxisAngle(Float3.Normalize(axis), angle);
        Restore();
        foreach ((Transform Transform, Float3 Position, Quaternion Rotation, Float3 Scale) start in _start)
        {
            start.Transform.Position = Pivot + turn * (start.Position - Pivot);
            start.Transform.Rotation = turn * start.Rotation;
        }
    }

    public void Scale(Float3 factors, Quaternion axes)
    {
        Quaternion back = Quaternion.Inverse(axes);
        Restore();
        foreach ((Transform Transform, Float3 Position, Quaternion Rotation, Float3 Scale) start in _start)
        {
            start.Transform.Position = Pivot + axes * ((back * (start.Position - Pivot)) * factors);
            // Each local axis grows by how much the scale stretches its direction
            Float3 grown = new(Float3.Length((back * (start.Rotation * Float3.UnitX)) * factors),
                               Float3.Length((back * (start.Rotation * Float3.UnitY)) * factors),
                               Float3.Length((back * (start.Rotation * Float3.UnitZ)) * factors));
            start.Transform.LocalScale = start.Scale * grown;
        }
    }

    public void Restore()
    {
        foreach ((Transform Transform, Float3 Position, Quaternion Rotation, Float3 Scale) start in _start)
        {
            start.Transform.Position = start.Position;
            start.Transform.Rotation = start.Rotation;
            start.Transform.LocalScale = start.Scale;
        }
    }
}
