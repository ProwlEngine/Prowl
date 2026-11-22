// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>
/// Moves the selected points of an edit target, and can put them back. Every move is measured from where the points
/// started, in world space, so a modal transform or a gizmo calls it again each frame with the whole amount.
/// </summary>
public sealed class TransformSession
{
    private readonly EditTarget _target;
    private readonly List<(GeometryData.Vertex Vertex, Float3 Local, Float3 World, Float3 Origin)> _points = new();
    private readonly Float4x4 _toLocal;

    public TransformSession(EditTarget target, ElementSelection selection)
    {
        _target = target;
        _toLocal = target.LocalToWorld.Invert();

        Dictionary<GeometryData.Vertex, Float3> origins = IslandCentres(selection, target.LocalToWorld);
        Float3 sum = Float3.Zero;
        foreach (GeometryData.Vertex vertex in selection.Vertices)
        {
            Float3 world = Float4x4.TransformPoint(vertex.Point, target.LocalToWorld);
            _points.Add((vertex, vertex.Point, world, origins.TryGetValue(vertex, out Float3 origin) ? origin : world));
            sum += world;
        }
        Median = _points.Count > 0 ? sum / _points.Count : Float3.Zero;
        Pivot = Median;
    }

    public int Count => _points.Count;

    /// <summary>The middle of the moving points, in the world.</summary>
    public Float3 Median { get; }

    /// <summary>What rotation and scale turn about, in the world. The median unless set.</summary>
    public Float3 Pivot { get; set; }

    /// <summary>Rotation and scale turn each island of selected faces about its own centre instead of the pivot.</summary>
    public bool Individual { get; set; }

    public void Translate(Float3 delta) => Apply((p, _) => p + delta);

    /// <summary>Turns by an angle in radians, by the right hand rule about the axis.</summary>
    public void Rotate(Float3 axis, float angle)
    {
        Quaternion turn = Quaternion.AxisAngle(Float3.Normalize(axis), angle);
        Apply((p, origin) => origin + turn * (p - origin));
    }

    /// <summary>Scales along three axes turned by a rotation.</summary>
    public void Scale(Float3 factors, Quaternion axes)
    {
        Quaternion back = Quaternion.Inverse(axes);
        Apply((p, origin) => origin + axes * ((back * (p - origin)) * factors));
    }

    public void Restore()
    {
        foreach ((GeometryData.Vertex vertex, Float3 local, _, _) in _points) vertex.Point = local;
    }

    private void Apply(Func<Float3, Float3, Float3> move)
    {
        foreach ((GeometryData.Vertex vertex, _, Float3 world, Float3 origin) in _points)
            vertex.Point = Float4x4.TransformPoint(move(world, Individual ? origin : Pivot), _toLocal);
    }

    /// <summary>The centre of each island of selected faces joined by edges, for each point of it.</summary>
    private static Dictionary<GeometryData.Vertex, Float3> IslandCentres(ElementSelection selection, Float4x4 toWorld)
    {
        var centres = new Dictionary<GeometryData.Vertex, Float3>();
        var done = new HashSet<GeometryData.Face>();
        foreach (GeometryData.Face start in selection.Faces)
        {
            if (!done.Add(start)) continue;
            var island = new HashSet<GeometryData.Vertex>();
            var stack = new Stack<GeometryData.Face>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                GeometryData.Face face = stack.Pop();
                island.UnionWith(face.NeighborVertices());
                foreach (GeometryData.Edge edge in face.NeighborEdges())
                    foreach (GeometryData.Face next in edge.NeighborFaces())
                        if (selection.Faces.Contains(next) && done.Add(next)) stack.Push(next);
            }

            Float3 sum = Float3.Zero;
            foreach (GeometryData.Vertex vertex in island) sum += Float4x4.TransformPoint(vertex.Point, toWorld);
            Float3 centre = sum / island.Count;
            foreach (GeometryData.Vertex vertex in island) centres[vertex] = centre;
        }
        return centres;
    }
}
