// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.GUI.SceneView;
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
[ExecuteAlways]
public sealed class ModelObject : Component
{
    /// <summary>Kept through renames and moves. What the imported model keys everything of this object on.</summary>
    public Guid Id = Guid.NewGuid();

    /// <summary>The editable mesh. Saved with the component as packed columns, so undo, redo and duplicating keep it.</summary>
    [NonSerialized] public GeometryData Mesh = new();

    /// <summary>The mesh as packed columns, read and written whenever the component is saved or recorded for undo.</summary>
    [System.Runtime.Serialization.DataMember]
    private byte[] MeshData
    {
        get => PMeshDocument.MeshToBytes(Mesh);
        set
        {
            Mesh = PMeshDocument.MeshFromBytes(value);
            _previewStale = true;
        }
    }

    // Set when the mesh came back from being saved, so the preview is rebuilt once the object is live
    [NonSerialized] private bool _previewStale = true;

    /// <summary>The material asset of each slot, empty for a slot with none.</summary>
    public List<Guid> Materials = [];

    /// <summary>Edges whose faces meet at less than this many degrees are smooth when normals are worked out.</summary>
    public float SmoothingAngle = 30f;

    /// <summary>Run in order on a copy of the mesh for what is shown and imported, the mesh underneath left as edited.</summary>
    public List<ModelModifier> Modifiers = [];

    /// <summary>The skeleton the mesh is skinned to, kept for the import. The model world shows the mesh at rest.</summary>
    public List<PMeshBone> Bones = [];

    /// <summary>The blend shapes the mesh's shape attributes hold.</summary>
    public List<PMeshShape> Shapes = [];

    [NonSerialized] private Mesh? _preview;
    [NonSerialized] private bool _hidden;

    /// <summary>Left out of the view, and so of picking, while the model is open. Not saved.</summary>
    public bool Hidden
    {
        get => _hidden;
        set
        {
            _hidden = value;
            if (GameObject.GetComponent<MeshRenderer>() is { } renderer && renderer.IsValid()) renderer.Mesh = value ? null : _preview;
        }
    }

    // The point of the mesh each preview vertex sits on, and the mesh and version it was built from, so a drag that only
    // moves points rewrites the preview's positions instead of building it again
    [NonSerialized] private int[]? _previewSources;
    [NonSerialized] private GeometryData? _previewFrom;
    [NonSerialized] private int _previewVersion;

    public override void OnEnable()
    {
        if (_previewStale || _preview.IsNotValid()) RefreshPreview();
    }

    // An undo puts back the mesh without enabling the object again, so the preview catches up here
    public override void Update()
    {
        if (_previewStale || TargetsStamp() != _targetsStamp) RefreshPreview();
    }

    // How the objects this one's booleans use stood when the preview was built, so a change to one builds it again
    [NonSerialized] private int _targetsStamp;

    private int TargetsStamp()
    {
        var stamp = new HashCode();
        foreach (ModelModifier modifier in Modifiers)
            if (modifier is BooleanModifier { Enabled: true } boolean && Find(boolean.Target) is { } other)
            {
                stamp.Add(other.Mesh.Version);
                stamp.Add(other.Transform.LocalToWorldMatrix);
                stamp.Add(Transform.LocalToWorldMatrix);
            }
        return stamp.ToHashCode();
    }

    private ModelObject? Find(Guid id)
    {
        if (id == Id || ModelEditingMode.World is not { } world) return null;
        foreach (GameObject go in world.Objects)
            if (go.GetComponent<ModelObject>() is { } model && model.IsValid() && model.Id == id) return model;
        return null;
    }

    /// <summary>What this object's modifiers see: its bones, and the other model objects of its world placed into its space.</summary>
    public ModifierContext ModifierContext()
    {
        Float4x4 toThis = Transform.LocalToWorldMatrix.Invert();
        return new ModifierContext([.. Bones.Select(b => b.Name)],
            id => Find(id) is { } other ? (other.Mesh, toThis * other.Transform.LocalToWorldMatrix) : null);
    }

    // Undo and the inspector can change modifiers or materials without touching the mesh
    public override void OnValidate() => _previewStale = true;

    /// <summary>Marks the preview out of date, to be rebuilt on the next update.</summary>
    public void MarkPreviewStale() => _previewStale = true;

    /// <summary>
    /// Builds the mesh drawn for the geometry as the import would, and shows it on the renderer. While dragging, modifiers
    /// too slow for every frame are left out.
    /// </summary>
    public void RefreshPreview(bool dragging = false)
    {
        MeshRenderer? renderer = GameObject.GetComponent<MeshRenderer>();
        if (renderer.IsNotValid()) renderer = GameObject.AddComponent<MeshRenderer>();
        if (dragging && MovePreviewPoints(renderer!)) return;

        Mesh? old = _preview;
        _preview = PMeshImporter.BuildMesh(ToDocumentObject(), ModifierContext(), dragging, out _previewSources);
        _targetsStamp = TargetsStamp();
        _previewFrom = Mesh;
        _previewVersion = Mesh.Version;
        _previewStale = false;
        renderer!.Mesh = _hidden ? null : _preview;

        var materials = new List<Material>();
        foreach (Guid id in Materials)
            materials.Add(ShowChecker ? Checker : id != Guid.Empty && AssetDatabase.Get<Material>(id) is { } material ? material : StandardMaterial);
        if (materials.Count == 0) materials.Add(ShowChecker ? Checker : StandardMaterial);
        renderer.Materials = materials;

        if (old.IsValid()) old!.Dispose();
    }

    /// <summary>
    /// Rewrites the positions of the shown preview when only points moved since it was built, its normals left as they
    /// were until the drag ends. False when it has to be built again.
    /// </summary>
    private bool MovePreviewPoints(MeshRenderer renderer)
    {
        if (_previewSources is not { } sources || _preview.IsNotValid() || !ReferenceEquals(renderer.Mesh, _preview)) return false;
        if (!ReferenceEquals(_previewFrom, Mesh) || Mesh.Version != _previewVersion || Modifiers.Exists(m => m.Enabled)) return false;
        var positions = new Float3[sources.Length];
        List<GeometryData.Vertex> vertices = Mesh.Vertices;
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] >= vertices.Count) return false;
            positions[i] = vertices[sources[i]].Point;
        }
        _preview!.Vertices = positions;
        _preview.RecalculateBounds();
        _previewStale = false;
        return true;
    }

    private static Material StandardMaterial => BuiltInAssets.Load<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard))!;

    /// <summary>Draws every model object with a checker in place of its materials, for judging how UVs stretch.</summary>
    public static bool ShowChecker { get; set; }

    private static Material? s_checker;

    private static Material Checker
    {
        get
        {
            if (s_checker.IsValid()) return s_checker!;
            const int Size = 256, Cells = 8;
            byte[] pixels = new byte[Size * Size * 4];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    bool light = (x / (Size / Cells) + y / (Size / Cells)) % 2 == 0;
                    int at = (y * Size + x) * 4;
                    pixels[at] = light ? (byte)220 : (byte)60;
                    pixels[at + 1] = light ? (byte)220 : (byte)60;
                    pixels[at + 2] = light ? (byte)220 : (byte)70;
                    pixels[at + 3] = 255;
                }
            var texture = new Texture2D(Size, Size, srgb: true) { Name = "UV Checker", Filter = FilterMode.Point };
            texture.SetData(pixels, 0, 0, Size, Size);
            s_checker = new Material(StandardMaterial) { Name = "UV Checker" };
            s_checker.SetTexture("base_color_map", texture);
            return s_checker;
        }
    }

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
            Materials = [with(Materials)],
            SmoothingAngle = SmoothingAngle,
            Modifiers = [with(Modifiers)],
            Bones = [with(Bones)],
            Shapes = [with(Shapes)],
        };
    }

    protected override void OnDispose()
    {
        if (_preview.IsValid()) _preview!.Dispose();
        _preview = null;
    }
}
