// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>One mesh being edited: its geometry, where it stands, and the rule every edit to it must keep.</summary>
public sealed class EditTarget
{
    public EditTarget(GeometryData mesh, Float4x4 localToWorld, Func<GeometryData, string?>? validate = null)
    {
        Mesh = mesh;
        LocalToWorld = localToWorld;
        Validate = validate;
    }

    public GeometryData Mesh { get; }

    public Float4x4 LocalToWorld { get; set; }

    /// <summary>A mirror plane in the mesh's space, by axis and offset, that moved points stay on and do not cross.</summary>
    public (int Axis, float Offset)? MirrorClip { get; init; }

    /// <summary>Why the mesh as it now stands may not be kept, or null when it may.</summary>
    public Func<GeometryData, string?>? Validate { get; }
}

/// <summary>Selected elements of one mesh, held by reference while the mesh is edited.</summary>
public sealed class ElementSelection
{
    public readonly HashSet<GeometryData.Vertex> Vertices = new();
    public readonly HashSet<GeometryData.Edge> Edges = new();
    public readonly HashSet<GeometryData.Face> Faces = new();

    public bool IsEmpty => Vertices.Count == 0 && Edges.Count == 0 && Faces.Count == 0;

    public void Clear()
    {
        Vertices.Clear();
        Edges.Clear();
        Faces.Clear();
    }
}

/// <summary>Runs edits on targets, keeping each target's rule.</summary>
public static class MeshEdit
{
    /// <summary>Locale key for an operation that could not be made on the mesh as it is.</summary>
    public const string OperationFailed = "mesh.status_operation_failed";

    /// <summary>
    /// Runs an operation on a target's mesh. The operation adds what should be selected after it to
    /// <paramref name="after"/>. When the result breaks the target's rule, or the operation throws because it cannot be
    /// made, the mesh is put back as it was, the selection emptied, and the reason returned as a locale key.
    /// </summary>
    public static string? Run(EditTarget target, Action<GeometryData, ElementSelection> operation, ElementSelection after)
    {
        GeometrySnapshot before = target.Mesh.Capture();
        string? problem;
        try
        {
            operation(target.Mesh, after);
            problem = target.Validate?.Invoke(target.Mesh);
        }
        catch (InvalidOperationException)
        {
            problem = OperationFailed;
        }
        if (problem == null) return null;

        target.Mesh.Restore(before);
        after.Clear();
        return problem;
    }
}
