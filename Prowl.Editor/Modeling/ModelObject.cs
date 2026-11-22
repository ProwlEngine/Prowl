// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>
/// An object of a model being edited in the model world: its persistent id, its editable mesh and what it is drawn with.
/// A <see cref="MeshRenderer"/> on the same object shows the mesh, so the hierarchy, selection and tools work as anywhere.
/// </summary>
public sealed class ModelObject : Component
{
    /// <summary>Kept through renames and moves. What the imported model keys everything of this object on.</summary>
    public Guid Id = Guid.NewGuid();

    /// <summary>The editable mesh. Edits to it are undone by the model world's own steps, not by recording the component.</summary>
    [NonSerialized] public GeometryData Mesh = new();

    /// <summary>The material asset of each slot, empty for a slot with none.</summary>
    public List<Guid> Materials = new();

    /// <summary>Edges whose faces meet at less than this many degrees are smooth when normals are worked out.</summary>
    public float SmoothingAngle = 30f;

    private Mesh? _preview;

    /// <summary>Builds the mesh drawn for the geometry as the import would, and shows it on the renderer.</summary>
    public void RefreshPreview()
    {
        MeshRenderer? renderer = GameObject.GetComponent<MeshRenderer>();
        if (renderer.IsNotValid()) renderer = GameObject.AddComponent<MeshRenderer>();
        Mesh? old = _preview;
        _preview = PMeshImporter.BuildMesh(ToDocumentObject());
        renderer!.Mesh = _preview;

        var materials = new List<Material>();
        foreach (Guid id in Materials)
            materials.Add(id != Guid.Empty && AssetDatabase.Get<Material>(id) is { } material ? material : StandardMaterial);
        if (materials.Count == 0) materials.Add(StandardMaterial);
        renderer.Materials = materials;

        if (old.IsValid()) old!.Dispose();
    }

    private static Material StandardMaterial => BuiltInAssets.Load<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard))!;

    /// <summary>This object as written into a model document, its transform taken as it is under its parent.</summary>
    public PMeshObject ToDocumentObject()
    {
        Transform transform = GameObject.Transform;
        return new PMeshObject
        {
            Id = Id,
            Name = GameObject.Name,
            Parent = transform.Parent?.GameObject.GetComponent<ModelObject>() is { } parent ? parent.Id : Guid.Empty,
            Position = transform.LocalPosition,
            Rotation = transform.LocalRotation,
            Scale = transform.LocalScale,
            Mesh = Mesh,
            Materials = new List<Guid>(Materials),
            SmoothingAngle = SmoothingAngle,
        };
    }

    protected override void OnDispose()
    {
        if (_preview.IsValid()) _preview!.Dispose();
        _preview = null;
    }
}
