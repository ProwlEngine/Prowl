// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>
/// What a modifier may look at beyond its own mesh: the names of the bones its skin counts, and the model's other
/// objects by id, each as its mesh and the matrix from its space into this object's.
/// </summary>
public sealed record ModifierContext(IReadOnlyList<string> Bones, Func<Guid, (GeometryData Mesh, Float4x4 ToObject)?>? FindObject = null);

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

    public ModelModifier Clone() => (ModelModifier)MemberwiseClone();

    /// <summary>Runs on a mesh of its own, which it may change or replace.</summary>
    public abstract GeometryData Apply(GeometryData mesh, ModifierContext context);

    /// <summary>Runs every enabled modifier in order on a copy of the mesh, or gives back the mesh itself when none runs.</summary>
    public static GeometryData Run(GeometryData mesh, IReadOnlyList<ModelModifier> stack, ModifierContext? context = null, bool dragging = false)
    {
        context ??= new ModifierContext([]);
        GeometryData result = mesh;
        bool skinned = false;
        foreach (ModelModifier modifier in stack)
        {
            if (!modifier.Enabled || dragging && modifier.SkipWhileDragging) continue;
            if (ReferenceEquals(result, mesh))
            {
                result = mesh.Clone();
                skinned = Operators.SkinToWeights(result);
            }
            result = modifier.Apply(result, context);
        }
        if (skinned) Operators.WeightsToSkin(result);
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
        () => new ArrayModifier(),
        () => new BevelModifier(),
        () => new BooleanModifier(),
    ];
}

public sealed class SubdivideModifier : ModelModifier
{
    public int Levels = 1;

    /// <summary>Makes every edge not flagged sharp smooth, for a rounded look.</summary>
    public bool SmoothShading = true;

    public override string Name => "Subdivide";

    public override GeometryData Apply(GeometryData mesh, ModifierContext context)
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

    /// <summary>The mirrored half has its blend shapes reflected and its skin moved to the bones of the other side.</summary>
    public override GeometryData Apply(GeometryData mesh, ModifierContext context)
    {
        int axis = Math.Clamp(Axis, 0, 2);
        var mirrored = new List<GeometryData.Face>();
        GeometryData result = GeometryOperators.MirrorCopy(mesh, axis, MergeDistance, Offset, mirrored);
        Operators.TurnMirrored(mirrored, axis, context.Bones);
        return result;
    }
}

public sealed class SolidifyModifier : ModelModifier
{
    public float Thickness = 0.1f;

    /// <summary>From -1 to 1: -1 keeps the outer shell on the surface, 1 the inner one.</summary>
    public float Offset = -1f;

    public override string Name => "Solidify";

    public override GeometryData Apply(GeometryData mesh, ModifierContext context) => GeometryOperators.Solidify(mesh, Thickness, Math.Clamp(Offset, -1f, 1f));
}

public sealed class WeldModifier : ModelModifier
{
    public float Distance = 0.001f;

    public override string Name => "Weld";

    public override GeometryData Apply(GeometryData mesh, ModifierContext context)
    {
        GeometryOperators.MergeByDistance(mesh, null, Distance);
        return mesh;
    }
}

public sealed class TriangulateModifier : ModelModifier
{
    public override string Name => "Triangulate";

    public override GeometryData Apply(GeometryData mesh, ModifierContext context)
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

    public override GeometryData Apply(GeometryData mesh, ModifierContext context)
    {
        GeometryOperators.Simplify(mesh, new SimplifyOptions { TargetRatio = Math.Clamp(Ratio, 0f, 1f) });
        return mesh;
    }
}

public sealed class ArrayModifier : ModelModifier
{
    public int Count = 2;

    /// <summary>Each copy's step as a part of the mesh's size along each axis.</summary>
    public Float3 RelativeOffset = new(1f, 0f, 0f);

    /// <summary>Each copy's step in the mesh's own units, added to the relative one.</summary>
    public Float3 ConstantOffset;

    /// <summary>Points of neighbouring copies this close are joined, 0 for never.</summary>
    public float MergeDistance;

    public override string Name => "Array";

    public override GeometryData Apply(GeometryData mesh, ModifierContext context)
    {
        if (Count <= 1 || mesh.Vertices.Count == 0) return mesh;
        AABB bounds = mesh.Bounds();
        Float3 step = RelativeOffset * (bounds.Max - bounds.Min) + ConstantOffset;
        GeometryOperators.Array(mesh, [.. mesh.Faces], [.. mesh.Edges.Where(e => e.Loop == null)], Count - 1, (point, copy) => point + step * copy, weld: false);
        if (MergeDistance > 0f) GeometryOperators.MergeByDistance(mesh, null, MergeDistance);
        return mesh;
    }
}

public sealed class BevelModifier : ModelModifier
{
    public float Width = 0.1f;
    public int Segments = 1;

    /// <summary>The shape of the rounding: 0 flat, 0.5 a circle, higher squarer.</summary>
    public float Profile = 0.5f;

    /// <summary>Only edges whose faces turn against each other by more than this many degrees are bevelled.</summary>
    public float Angle = 30f;

    public override string Name => "Bevel";

    public override bool SkipWhileDragging => true;

    public override GeometryData Apply(GeometryData mesh, ModifierContext context)
    {
        List<GeometryData.Edge> edges = GeometryOperators.EdgesSharperThan(mesh, Angle);
        if (edges.Count == 0 || Width <= 0f) return mesh;
        GeometrySnapshot before = mesh.Capture();
        try
        {
            GeometryOperators.BevelEdges(mesh, edges, Width, Math.Max(1, Segments), Profile);
        }
        catch (InvalidOperationException)
        {
            mesh.Restore(before);
        }
        return mesh;
    }
}

public sealed class BooleanModifier : ModelModifier
{
    /// <summary>The model object whose mesh is added, cut away or kept, by its id.</summary>
    [Prowl.Runtime.HideInInspector] public Guid Target;

    public CSGOperation Operation = CSGOperation.Subtractive;

    public override string Name => "Boolean";

    public override bool SkipWhileDragging => true;

    public override GeometryData Apply(GeometryData mesh, ModifierContext context)
        => Target != Guid.Empty && context.FindObject?.Invoke(Target) is { } other ? GeometryOperators.Boolean(mesh, other.Mesh, other.ToObject, Operation) : mesh;
}
