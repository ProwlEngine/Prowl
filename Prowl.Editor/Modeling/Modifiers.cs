// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>
/// One step of a model object's modifier stack, run in order on a copy of its mesh. What the world shows and what the
/// import builds is the end of the stack, while edits go on the mesh underneath.
/// </summary>
public abstract class ModelModifier
{
    public bool Enabled = true;

    /// <summary>The name the stack shows it by.</summary>
    public abstract string Name { get; }

    /// <summary>Too slow to run on every frame of a drag, so the preview leaves it out until the drag ends.</summary>
    public virtual bool SkipWhileDragging => false;

    /// <summary>Runs on a mesh of its own, which it may change or replace.</summary>
    public abstract GeometryData Apply(GeometryData mesh);

    /// <summary>Runs every enabled modifier in order on a copy of the mesh, or gives back the mesh itself when none runs.</summary>
    public static GeometryData Run(GeometryData mesh, IReadOnlyList<ModelModifier> stack, bool dragging = false)
    {
        GeometryData result = mesh;
        foreach (ModelModifier modifier in stack)
        {
            if (!modifier.Enabled || dragging && modifier.SkipWhileDragging) continue;
            if (ReferenceEquals(result, mesh))
            {
                result = new GeometryData();
                result.Restore(mesh.Capture());
            }
            result = modifier.Apply(result);
        }
        return result;
    }

    /// <summary>Every kind of modifier, as the stack's add menu lists them.</summary>
    public static readonly Func<ModelModifier>[] Kinds =
    [
        () => new SubdivideModifier(),
        () => new MirrorModifier(),
        () => new SolidifyModifier(),
        () => new WeldModifier(),
        () => new TriangulateModifier(),
        () => new SimplifyModifier(),
    ];
}

public sealed class SubdivideModifier : ModelModifier
{
    public int Levels = 1;

    /// <summary>Makes every edge not flagged sharp smooth, for a rounded look.</summary>
    public bool SmoothShading = true;

    public override string Name => "Subdivide";

    public override GeometryData Apply(GeometryData mesh)
    {
        GeometryData result = GeometryOperators.SubdivideSmooth(mesh, Math.Clamp(Levels, 0, 6));
        if (SmoothShading)
            foreach (GeometryData.Edge edge in result.Edges)
                if (!edge.IsSharp) edge.IsSoft = true;
        return result;
    }
}

public sealed class MirrorModifier : ModelModifier
{
    /// <summary>The axis the mirror plane is across: 0, 1 or 2 for X, Y or Z.</summary>
    public int Axis;
    public float Offset;
    public float MergeDistance = 0.001f;

    public override string Name => "Mirror";

    public override GeometryData Apply(GeometryData mesh) => GeometryOperators.MirrorCopy(mesh, Math.Clamp(Axis, 0, 2), MergeDistance, Offset);
}

public sealed class SolidifyModifier : ModelModifier
{
    public float Thickness = 0.1f;

    /// <summary>From -1 to 1: -1 keeps the outer shell on the surface, 1 the inner one.</summary>
    public float Offset = -1f;

    public override string Name => "Solidify";

    public override GeometryData Apply(GeometryData mesh) => GeometryOperators.Solidify(mesh, Thickness, Math.Clamp(Offset, -1f, 1f));
}

public sealed class WeldModifier : ModelModifier
{
    public float Distance = 0.001f;

    public override string Name => "Weld";

    public override GeometryData Apply(GeometryData mesh)
    {
        GeometryOperators.MergeByDistance(mesh, null, Distance);
        return mesh;
    }
}

public sealed class TriangulateModifier : ModelModifier
{
    public override string Name => "Triangulate";

    public override GeometryData Apply(GeometryData mesh)
    {
        GeometryOperators.Triangulate(mesh);
        return mesh;
    }
}

public sealed class SimplifyModifier : ModelModifier
{
    /// <summary>The fraction of triangles kept, from 0 to 1.</summary>
    public float Ratio = 0.5f;

    public override string Name => "Simplify";

    public override bool SkipWhileDragging => true;

    public override GeometryData Apply(GeometryData mesh)
    {
        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = Math.Clamp(Ratio, 0f, 1f) });
        return mesh;
    }
}
