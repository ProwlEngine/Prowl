// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.Editor.Core;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>One surface: a brush and the plane of it that produced a face.</summary>
public readonly struct CSGSurfaceRef : IEquatable<CSGSurfaceRef>
{
    public readonly CSGBrush Brush;
    public readonly int PlaneIndex;

    /// <summary>The plane when the surface was picked, to find it again if the brush reorders its planes.</summary>
    public readonly Plane Plane;

    public CSGSurfaceRef(CSGBrush brush, int planeIndex)
    {
        Brush = brush;
        PlaneIndex = planeIndex;
        Plane = brush.IsValid() && planeIndex >= 0 && planeIndex < brush.Planes.Length ? brush.Planes[planeIndex] : default;
    }

    public bool IsValid => Brush.IsValid() && PlaneIndex >= 0 && PlaneIndex < Brush.Planes.Length;

    /// <summary>This surface after its brush reordered or replaced its planes, or null when its plane is gone.</summary>
    public CSGSurfaceRef? Relocated()
    {
        if (Brush.IsNotValid()) return null;
        Plane[] planes = Brush.Planes;
        if (PlaneIndex >= 0 && PlaneIndex < planes.Length && planes[PlaneIndex].Equals(Plane)) return this;

        // Point edits refit every plane, so the nearest one within a tolerance is still this surface
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < planes.Length; i++)
        {
            float score = (1f - Float3.Dot(planes[i].Normal, Plane.Normal)) * 10f + MathF.Abs(planes[i].D - Plane.D);
            if (score >= bestScore) continue;
            bestScore = score;
            best = i;
        }
        return best >= 0 && bestScore < 0.05f ? new CSGSurfaceRef(Brush, best) : null;
    }

    public SurfaceData Data => Brush.Surfaces[PlaneIndex];

    public bool Equals(CSGSurfaceRef other)
        => ReferenceEquals(Brush, other.Brush) && PlaneIndex == other.PlaneIndex;

    public override bool Equals(object? obj) => obj is CSGSurfaceRef o && Equals(o);
    public static bool operator ==(CSGSurfaceRef a, CSGSurfaceRef b) => a.Equals(b);
    public static bool operator !=(CSGSurfaceRef a, CSGSurfaceRef b) => !a.Equals(b);

    // Hashed on reference identity so a destroyed brush still hashes the same.
    public override int GetHashCode()
        => HashCode.Combine(RuntimeHelpers.GetHashCode(Brush), PlaneIndex);
}

/// <summary>The selected surfaces and the edits on them, shared by the surface tool and the CSG panel.</summary>
public static class CSGSurfaceSelection
{
    private static readonly List<CSGSurfaceRef> _selected = [];

    public static IReadOnlyList<CSGSurfaceRef> Selected => _selected;

    public static int Count => _selected.Count;

    /// <summary>The surface whose values the panel shows, or null when nothing is selected.</summary>
    public static CSGSurfaceRef? Active => _selected.Count > 0 ? _selected[^1] : null;

    public static bool IsSelected(CSGSurfaceRef surface) => _selected.Contains(surface);

    public static void Clear() => _selected.Clear();

    public static void Select(CSGSurfaceRef surface)
    {
        _selected.Clear();
        if (surface.IsValid) _selected.Add(surface);
    }

    public static void Add(CSGSurfaceRef surface)
    {
        if (surface.IsValid && !_selected.Contains(surface)) _selected.Add(surface);
    }

    public static void Remove(CSGSurfaceRef surface) => _selected.Remove(surface);

    public static void Toggle(CSGSurfaceRef surface)
    {
        if (!_selected.Remove(surface)) Add(surface);
    }

    /// <summary>Every surface of the brushes already selected.</summary>
    public static void SelectAllOfBrush()
    {
        var brushes = new List<CSGBrush>();
        foreach (CSGSurfaceRef surface in _selected)
            if (surface.Brush.IsValid() && !brushes.Contains(surface.Brush)) brushes.Add(surface.Brush);

        _selected.Clear();
        foreach (CSGBrush brush in brushes)
            for (int i = 0; i < brush.Planes.Length; i++) _selected.Add(new CSGSurfaceRef(brush, i));
    }

    /// <summary>Follows surfaces whose brush reordered its planes, and drops those whose brush or plane has gone.</summary>
    public static void Prune()
    {
        for (int i = _selected.Count - 1; i >= 0; i--)
        {
            if (_selected[i].Relocated() is CSGSurfaceRef moved) _selected[i] = moved;
            else _selected.RemoveAt(i);
        }
    }

    /// <summary>Every surface in the model using the active surface's material.</summary>
    public static void SelectSameMaterial()
    {
        if (Active is not CSGSurfaceRef source || !source.IsValid) return;
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return;

        Material? material = source.Data.Material;
        _selected.Clear();
        foreach (CSGBrush brush in CSGSelection.BrushesIn(model!))
        {
            SurfaceData[] surfaces = brush.Surfaces;
            for (int i = 0; i < surfaces.Length; i++)
                if (ReferenceEquals(surfaces[i].Material, material) && brush.FaceCorners(i).Count > 0) _selected.Add(new CSGSurfaceRef(brush, i));
        }
    }

    /// <summary>Every surface of the model facing up, so all floors can be textured at once.</summary>
    public static void SelectFloors() => SelectWhere(s => s.Normal.Y > 0.7f);

    public static void SelectCeilings() => SelectWhere(s => s.Normal.Y < -0.7f);

    public static void SelectWalls() => SelectWhere(s => MathF.Abs(s.Normal.Y) < 0.3f);

    /// <summary>Every surface lying in the active surface's plane.</summary>
    public static void SelectCoplanar()
    {
        if (!TryActiveFace(out ModelSurface active)) return;
        SelectWhere(s => Float3.Dot(s.Normal, active.Normal) > 0.999f
                      && MathF.Abs(Float3.Dot(s.Corners[0] - active.Corners[0], active.Normal)) < 1e-3f, active.Surface);
    }

    /// <summary>Every surface joined to the active one through shared edges, without turning a corner sharper than 45 degrees.</summary>
    public static void SelectConnected()
    {
        if (!TryActiveFace(out ModelSurface active)) return;

        List<ModelSurface> all = ModelSurfaces();
        var found = new List<ModelSurface> { active };
        for (int i = 0; i < found.Count; i++)
        {
            foreach (ModelSurface other in all)
            {
                if (found.Exists(f => ReferenceEquals(f.Corners, other.Corners))) continue;
                if (Float3.Dot(other.Normal, found[i].Normal) < 0.7f || !SharesEdge(found[i], other)) continue;
                found.Add(other);
            }
        }

        _selected.Clear();
        foreach (ModelSurface surface in found)
            if (!surface.Surface.Equals(active.Surface) && !_selected.Contains(surface.Surface)) _selected.Add(surface.Surface);
        _selected.Add(active.Surface);
    }

    private readonly record struct ModelSurface(CSGSurfaceRef Surface, Float3 Normal, IReadOnlyList<Float3> Corners);

    /// <summary>The faces the model actually shows, so a face is judged by where it ends up rather than by its brush.</summary>
    private static List<ModelSurface> ModelSurfaces()
    {
        var result = new List<ModelSurface>();
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return result;

        foreach (CSGModelFace face in model!.Faces())
        {
            if (!face.Brush.EnabledInHierarchy || CSGVisibility.IsHidden(face.Brush)) continue;
            result.Add(new ModelSurface(new CSGSurfaceRef(face.Brush, face.PlaneIndex), face.Normal, face.Corners));
        }
        return result;
    }

    /// <summary>The active surface as the model shows it, the largest of its pieces when it shows in several.</summary>
    private static bool TryActiveFace(out ModelSurface active)
    {
        active = default;
        if (Active is not CSGSurfaceRef surface || !surface.IsValid) return false;

        bool found = false;
        float best = -1f;
        foreach (ModelSurface piece in ModelSurfaces())
        {
            if (!piece.Surface.Equals(surface)) continue;
            float area = Area(piece.Corners);
            if (area <= best) continue;
            best = area;
            active = piece;
            found = true;
        }
        return found;
    }

    private static float Area(IReadOnlyList<Float3> corners)
    {
        Float3 sum = Float3.Zero;
        for (int i = 1; i + 1 < corners.Count; i++) sum += Float3.Cross(corners[i] - corners[0], corners[i + 1] - corners[0]);
        return Float3.Length(sum) * 0.5f;
    }

    private static void SelectWhere(Func<ModelSurface, bool> keep, CSGSurfaceRef? last = null)
    {
        _selected.Clear();
        foreach (ModelSurface surface in ModelSurfaces())
            if (keep(surface) && !surface.Surface.Equals(last) && !_selected.Contains(surface.Surface)) _selected.Add(surface.Surface);
        if (last is CSGSurfaceRef active) _selected.Add(active);
    }

    private static bool SharesEdge(ModelSurface a, ModelSurface b)
    {
        int shared = 0;
        foreach (Float3 p in a.Corners)
            foreach (Float3 q in b.Corners)
                if (Float3.DistanceSquared(p, q) < 1e-6f && ++shared >= 2) return true;
        return false;
    }

    /// <summary>Puts a material on the surface under the ray, or on the whole selection when that surface is selected.</summary>
    public static void AssignMaterial(CSGModel model, Ray ray, Material material)
    {
        if (!model.Raycast(ray, out CSGBrush brush, out int plane, out _, b => !CSGVisibility.IsHidden(b)) || plane < 0) return;

        var hit = new CSGSurfaceRef(brush, plane);
        if (IsSelected(hit))
        {
            SetMaterial(material);
            return;
        }

        Remember(material);
        Undo.RecordObject(brush, "Assign CSG Material");
        SurfaceData data = hit.Data;
        data.Material = material;
        brush.SetSurface(plane, data);
        model.SetDirty();
    }

    private static readonly List<Material> _recent = [];

    /// <summary>Materials to paint with: the ones last assigned, then every other one the model already uses.</summary>
    public static List<Material> Palette()
    {
        _recent.RemoveAll(m => m.IsNotValid());
        var palette = new List<Material>(_recent);

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return palette;

        if (model!.DefaultMaterial.IsValid() && !palette.Contains(model.DefaultMaterial!)) palette.Add(model.DefaultMaterial!);
        foreach (CSGBrush brush in CSGSelection.BrushesIn(model))
            foreach (SurfaceData data in brush.Surfaces)
                if (data.Material.IsValid() && !palette.Contains(data.Material!)) palette.Add(data.Material!);
        return palette;
    }

    private static void Remember(Material? material)
    {
        if (material.IsNotValid()) return;
        _recent.Remove(material!);
        _recent.Insert(0, material!);
        if (_recent.Count > 8) _recent.RemoveAt(_recent.Count - 1);
    }

    public static void SetMaterial(Material? material)
    {
        Remember(material);
        SetMaterialOnly(material);
    }

    private static void SetMaterialOnly(Material? material)
        => Edit("Assign CSG Material", (data, _, _) => { data.Material = material; return data; });

    public static void SetOffset(Float2 offset)
        => Edit("Offset CSG Surface", (data, _, _) => { data.Offset = offset; return data; });

    public static void SetScale(Float2 scale)
        => Edit("Scale CSG Surface", (data, _, _) => { data.Scale = scale; return data; });

    public static void SetRotation(float degrees)
        => Edit("Rotate CSG Surface", (data, _, _) => { data.Rotation = degrees; return data; });

    public static void SetSmoothingGroup(int group)
        => Edit("Smooth CSG Surface", (data, _, _) => { data.SmoothingGroup = group; return data; });

    /// <summary>Puts the whole selection in one unused smoothing group.</summary>
    public static void Smooth()
    {
        int group = UnusedSmoothingGroup();
        Edit("Smooth CSG Surfaces", (data, _, _) => { data.SmoothingGroup = group; return data; });
    }

    public static void UnSmooth()
        => Edit("Unsmooth CSG Surfaces", (data, _, _) => { data.SmoothingGroup = 0; return data; });

    /// <summary>Adds to the current angle rather than replacing it.</summary>
    public static void RotateBy(float degrees)
        => Edit("Rotate CSG Texture", (data, _, _) => { data.Rotation += degrees; return data; });

    /// <summary>The lowest group number no surface in the model is already using.</summary>
    internal static int UnusedSmoothingGroup()
    {
        var used = new HashSet<int>();

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid())
        {
            foreach (GameObject go in model.GameObject.GetChildrenDeep())
            {
                CSGBrush brush = go.GetComponent<CSGBrush>();
                if (brush.IsNotValid()) continue;

                foreach (SurfaceData data in brush.Surfaces) used.Add(data.SmoothingGroup);
            }
        }

        int group = 1;
        while (used.Contains(group)) group++;
        return group;
    }

    public static void SetFlag(CSGSurfaceFlags flag, bool on)
    {
        if (flag != CSGSurfaceFlags.WorldSpaceTexture)
        {
            Edit("Set CSG Surface Flag", (data, _, _) =>
            {
                data.Flags = on ? data.Flags | flag : data.Flags & ~flag;
                return data;
            });
            return;
        }

        // Switching between brush and world space keeps the texture where it is on the face
        Prune();
        foreach (CSGSurfaceRef surface in _selected) Undo.RecordObject(surface.Brush, "Set CSG Texture Lock");
        foreach (CSGSurfaceRef surface in _selected)
        {
            int polygon = surface.PlaneIndex;
            KeepTextures(surface.Brush, (p, _) => p == polygon, () => { }, data =>
            {
                data.Flags = on ? data.Flags | flag : data.Flags & ~flag;
                return data;
            });
        }

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid()) model!.SetDirty();
    }

    public static void Fit(bool u, bool v)
        => Edit("Fit CSG Texture", (data, extent, texel) =>
            CSGSurfaceGeometry.Fit(data, extent.Min, extent.Max, u, v, texel));

    public static void Flip(bool u, bool v)
        => Edit("Flip CSG Texture", (data, extent, texel) =>
            CSGSurfaceGeometry.Flip(data, extent.Min, extent.Max, u, v, texel));

    public static void Justify(SurfaceAlign horizontal, SurfaceAlign vertical)
        => Edit("Justify CSG Texture", (data, extent, texel) =>
            CSGSurfaceGeometry.Justify(data, extent.Min, extent.Max, horizontal, vertical, texel));

    public static void ScaleBy(float factor)
        => Edit("Scale CSG Texture", (data, extent, texel) =>
            CSGSurfaceGeometry.ScaleBy(data, extent.Min, new Float2(factor, factor), texel));

    public static void Reset(bool u, bool v)
        => Edit("Reset CSG Texture", (data, _, _) => CSGSurfaceGeometry.Reset(data, u, v));

    /// <summary>Paints the active surface's look onto one other surface without disturbing the selection. Offset is not copied.</summary>
    public static void SmearOnto(CSGSurfaceRef target)
    {
        if (Active is not CSGSurfaceRef source || !source.IsValid || !target.IsValid) return;
        if (source.Equals(target)) return;

        SurfaceData from = source.Data;
        SurfaceData data = target.Data;

        data.Material = from.Material;
        data.Scale = from.Scale;
        data.Rotation = from.Rotation;
        data.Flags = from.Flags;
        data.SmoothingGroup = from.SmoothingGroup;

        Undo.RecordObject(target.Brush, "Smear CSG Material");
        target.Brush.SetSurface(target.PlaneIndex, data);

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid()) model!.SetDirty();
    }

    /// <summary>
    /// Lines the selection's textures up with the active surface's, so they run on across shared edges: every selected
    /// surface projects in world space with the active one's scale, rotation and offset.
    /// </summary>
    public static void AlignToActive()
    {
        Prune();
        if (Active is not CSGSurfaceRef source || !source.IsValid) return;

        // The active surface stays as it is, its texture expressed in world space for the others to copy
        CSGModel? model = CSGSelection.ActiveModel();
        SurfaceData template = InWorldSpace(source.Brush, source.PlaneIndex, model);

        foreach (CSGSurfaceRef surface in _selected)
            if (!surface.Equals(source)) Undo.RecordObject(surface.Brush, "Align CSG Textures");

        foreach (CSGSurfaceRef surface in _selected)
        {
            if (surface.Equals(source)) continue;
            SurfaceData data = surface.Data;
            data.Offset = template.Offset;
            data.Scale = template.Scale;
            data.Rotation = template.Rotation;
            data.Flags |= CSGSurfaceFlags.WorldSpaceTexture;
            surface.Brush.SetSurface(surface.PlaneIndex, data);
        }

        if (model.IsValid()) model!.SetDirty();
    }

    /// <summary>A face's surface as it would be projected in world space, showing the same texture where it is.</summary>
    private static SurfaceData InWorldSpace(CSGBrush brush, int polygon, CSGModel? model)
    {
        SurfaceData data = brush.Surfaces[polygon];
        if (data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture)) return data;

        float texel = Texel(model);
        (int[] points, Float2[] uvs) = CornerUVs(brush, polygon, data, model, texel);

        data.Flags |= CSGSurfaceFlags.WorldSpaceTexture;
        (Float4x4 toFrame, Float3 normal) = Frame(brush, polygon, data, model);
        return CSGSurfaceGeometry.FitUVs(data, normal, points.Select(i => Float4x4.TransformPoint(brush.ControlMesh.Points[i], toFrame)).ToList(), uvs, texel);
    }

    private static float Texel(CSGModel? model) => model.IsValid() && model!.DefaultTexelScale > 0f ? model.DefaultTexelScale : 1f;

    /// <summary>The space a face's UVs project in, reached from its brush's own space, and the normal that picks its axes.</summary>
    private static (Float4x4 BrushToFrame, Float3 Normal) Frame(CSGBrush brush, int polygon, in SurfaceData data, CSGModel? model)
    {
        if (!data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture))
            return (Float4x4.Identity, brush.Planes[polygon].Normal);

        // Column vector convention: brush local to world first, then world to model space
        Float4x4 toModel = brush.Transform.LocalToWorldMatrix;
        if (model.IsValid()) toModel = model!.Transform.WorldToLocalMatrix * toModel;

        List<Float3> corners = brush.FaceCorners(polygon);
        for (int i = 0; i < corners.Count; i++) corners[i] = Float4x4.TransformPoint(corners[i], toModel);
        return (toModel, NormalOf(corners));
    }

    /// <summary>The UVs a face shows at its corners, with the points they sit on.</summary>
    private static (int[] Points, Float2[] UVs) CornerUVs(CSGBrush brush, int polygon, in SurfaceData data, CSGModel? model, float texel)
    {
        int[] points = [.. brush.ControlMesh.PolygonPoints(polygon)];
        (Float4x4 toFrame, Float3 normal) = Frame(brush, polygon, data, model);

        var uvs = new Float2[points.Length];
        for (int i = 0; i < points.Length; i++)
            uvs[i] = CSGSurfaceGeometry.UVAt(data, normal, Float4x4.TransformPoint(brush.ControlMesh.Points[points[i]], toFrame), texel);
        return (points, uvs);
    }

    /// <summary>
    /// Runs a change to a brush, then refits the chosen faces' textures so each corner keeps the UV it had: the texture
    /// moves with the points. <paramref name="reframe"/> can change a face's projection settings as part of the change.
    /// </summary>
    internal static void KeepTextures(CSGBrush brush, Func<int, SurfaceData, bool> chosen, System.Action change, Func<SurfaceData, SurfaceData>? reframe = null)
    {
        CSGModel? model = CSGSelection.ModelOf(brush.GameObject);
        float texel = Texel(model);

        var kept = new List<(int Polygon, int[] Points, Float2[] UVs)>();
        for (int p = 0; p < brush.Surfaces.Length; p++)
        {
            if (!chosen(p, brush.Surfaces[p])) continue;
            (int[] points, Float2[] uvs) = CornerUVs(brush, p, brush.Surfaces[p], model, texel);
            kept.Add((p, points, uvs));
        }

        change();

        foreach ((int polygon, int[] points, Float2[] uvs) in kept)
        {
            if (polygon >= brush.Surfaces.Length) continue;
            SurfaceData data = reframe != null ? reframe(brush.Surfaces[polygon]) : brush.Surfaces[polygon];
            (Float4x4 toFrame, Float3 normal) = Frame(brush, polygon, data, model);
            var placed = points.Select(i => Float4x4.TransformPoint(brush.ControlMesh.Points[i], toFrame)).ToList();
            brush.SetSurface(polygon, CSGSurfaceGeometry.FitUVs(data, normal, placed, uvs, texel));
        }
    }

    /// <summary>Copies the active surface's texturing onto every other selected surface.</summary>
    public static void CopyFromActive()
    {
        if (Active is not CSGSurfaceRef source || !source.IsValid) return;

        SurfaceData template = source.Data;
        Edit("Copy CSG Surface", (data, _, _) =>
        {
            data.Material = template.Material;
            data.Offset = template.Offset;
            data.Scale = template.Scale;
            data.Rotation = template.Rotation;
            data.Flags = template.Flags;
            data.SmoothingGroup = template.SmoothingGroup;
            return data;
        });
    }

    private readonly struct Extent
    {
        public readonly Float2 Min, Max;
        public Extent(Float2 min, Float2 max) { Min = min; Max = max; }
    }

    private delegate SurfaceData EditOp(SurfaceData data, Extent extent, float texelScale);

    /// <summary>Applies an edit to every selected surface, handing each its own projected extent.</summary>
    private static void Edit(string description, EditOp op)
    {
        Prune();
        if (_selected.Count == 0) return;

        foreach (CSGSurfaceRef surface in _selected) Undo.RecordObject(surface.Brush, description);

        CSGModel? model = CSGSelection.ActiveModel();
        float texel = model.IsValid() ? model!.DefaultTexelScale : 1f;

        foreach (CSGSurfaceRef surface in _selected)
        {
            Extent extent = ExtentOf(surface, model);
            surface.Brush.SetSurface(surface.PlaneIndex, op(surface.Data, extent, texel));
        }

        if (model.IsValid()) model!.SetDirty();
    }

    /// <summary>The surface's span in projection space, measured in the space its UVs are projected in.</summary>
    private static Extent ExtentOf(CSGSurfaceRef surface, CSGModel? model)
    {
        List<Float3> corners = surface.Brush.FaceCorners(surface.PlaneIndex);
        if (corners.Count == 0) return new Extent(Float2.Zero, Float2.Zero);

        Float3 normal;
        if (surface.Data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture))
        {
            // Column vector convention: brush local to world first, then world to model space
            Float4x4 toModel = surface.Brush.Transform.LocalToWorldMatrix;
            if (model.IsValid()) toModel = model!.Transform.WorldToLocalMatrix * toModel;

            for (int i = 0; i < corners.Count; i++)
                corners[i] = Float4x4.TransformPoint(corners[i], toModel);
            normal = NormalOf(corners);
        }
        else
        {
            // Texture lock projects in the brush's own space from its plane, as the model does
            normal = surface.Brush.Planes[surface.PlaneIndex].Normal;
        }

        CSGSurfaceGeometry.TryExtent(normal, surface.Data.Rotation, corners, out Float2 min, out Float2 max);
        return new Extent(min, max);
    }

    /// <summary>Newell's method, which stays stable when a face has collinear corners.</summary>
    private static Float3 NormalOf(List<Float3> corners)
    {
        Float3 n = Float3.Zero;
        for (int i = 0; i < corners.Count; i++)
        {
            Float3 a = corners[i], b = corners[(i + 1) % corners.Count];
            n.X += (a.Y - b.Y) * (a.Z + b.Z);
            n.Y += (a.Z - b.Z) * (a.X + b.X);
            n.Z += (a.X - b.X) * (a.Y + b.Y);
        }
        return Float3.NormalizeSafe(n, Float3.UnitY);
    }

    /// <summary>The surface's corners in world space, for drawing.</summary>
    public static List<Float3> WorldCorners(CSGSurfaceRef surface)
    {
        List<Float3> corners = surface.Brush.FaceCorners(surface.PlaneIndex);

        Float4x4 toWorld = surface.Brush.Transform.LocalToWorldMatrix;
        for (int i = 0; i < corners.Count; i++)
            corners[i] = Float4x4.TransformPoint(corners[i], toWorld);

        return corners;
    }

    // ================================================================
    //  Texture drags
    // ================================================================

    /// <summary>A selected surface as a texture drag began: its data, and the space and normal its UVs project in.</summary>
    private readonly record struct DragStart(CSGSurfaceRef Surface, SurfaceData Data, Float4x4 ToProjection, Float3 Normal);

    private static readonly List<DragStart> _drag = [];
    private static float _dragTexel = 1f;

    // The model shows its faces from inside, which mirrors U, so a turn on screen is the other way in texture space
    private static bool _dragInverted;

    /// <summary>Remembers every selected surface as it is, so a drag is always applied to where it started.</summary>
    public static void BeginTextureDrag()
    {
        Prune();
        _drag.Clear();
        CSGModel? model = CSGSelection.ActiveModel();
        _dragTexel = model.IsValid() && model!.DefaultTexelScale > 0f ? model.DefaultTexelScale : 1f;
        _dragInverted = model.IsValid() && model!.SubtractiveWorkflow;

        foreach (CSGSurfaceRef surface in _selected)
        {
            SurfaceData data = surface.Data;
            if (data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture))
            {
                Float4x4 toModel = model.IsValid() ? model!.Transform.WorldToLocalMatrix : Float4x4.Identity;
                List<Float3> corners = surface.Brush.FaceCorners(surface.PlaneIndex);
                Float4x4 brushToModel = toModel * surface.Brush.Transform.LocalToWorldMatrix;
                for (int i = 0; i < corners.Count; i++) corners[i] = Float4x4.TransformPoint(corners[i], brushToModel);
                _drag.Add(new DragStart(surface, data, toModel, NormalOf(corners)));
            }
            else
            {
                _drag.Add(new DragStart(surface, data, surface.Brush.Transform.WorldToLocalMatrix, surface.Brush.Planes[surface.PlaneIndex].Normal));
            }
        }
    }

    /// <summary>Slides every dragged texture by a world distance, so the texture follows the cursor.</summary>
    public static void MoveTextures(Float3 worldDelta)
    {
        ApplyDrag("Move CSG Texture", start =>
        {
            Float3 d = Float4x4.TransformNormal(worldDelta, start.ToProjection);
            CSGSurfaceGeometry.Basis(start.Normal, start.Data.Rotation, out Float3 u, out Float3 v);
            Float2 scale = CSGSurfaceGeometry.Effective(start.Data, _dragTexel);

            SurfaceData data = start.Data;
            data.Offset = start.Data.Offset - new Float2(Float3.Dot(d, u) / scale.X, Float3.Dot(d, v) / scale.Y);
            return data;
        });
    }

    /// <summary>
    /// Turns every dragged texture about a world point, which keeps its place on the texture, by the angle the cursor
    /// swept from <paramref name="worldFrom"/> to <paramref name="worldTo"/>. The angle is measured in each surface's own
    /// projection space, so the texture turns with the cursor however that space is turned or mirrored.
    /// </summary>
    public static void RotateTextures(Float3 worldPivot, Float3 worldFrom, Float3 worldTo, float snapDegrees)
        => ApplyDrag("Rotate CSG Texture", start =>
        {
            Float3 pivot = Float4x4.TransformPoint(worldPivot, start.ToProjection);
            CSGSurfaceGeometry.Basis(start.Normal, 0f, out Float3 u, out Float3 v);
            float Angle(Float3 world)
            {
                Float3 w = Float4x4.TransformPoint(world, start.ToProjection) - pivot;
                return MathF.Atan2(Float3.Dot(w, v), Float3.Dot(w, u)) * (180f / MathF.PI);
            }

            float degrees = Angle(worldTo) - Angle(worldFrom);
            if (_dragInverted) degrees = -degrees;
            if (snapDegrees > 0f) degrees = MathF.Round(degrees / snapDegrees) * snapDegrees;

            SurfaceData data = start.Data;
            data.Rotation = start.Data.Rotation + degrees;
            return KeepPivot(start, data, worldPivot);
        });

    /// <summary>Scales every dragged texture about a world point, which keeps its place on the texture.</summary>
    public static void ScaleTextures(Float3 worldPivot, float factor)
        => ApplyDrag("Scale CSG Texture", start =>
        {
            SurfaceData data = start.Data;
            data.Scale = new Float2(NonZero(start.Data.Scale.X), NonZero(start.Data.Scale.Y)) * factor;
            return KeepPivot(start, data, worldPivot);
        });

    /// <summary>The angle of a world point about a pivot, measured on a surface's own unrotated texture axes.</summary>
    public static float AngleOnSurface(Float3 normalWorld, Float3 pivot, Float3 point)
    {
        CSGSurfaceGeometry.Basis(normalWorld, 0f, out Float3 u, out Float3 v);
        Float3 w = point - pivot;
        return MathF.Atan2(Float3.Dot(w, v), Float3.Dot(w, u)) * (180f / MathF.PI);
    }

    private static float NonZero(float value) => value == 0f ? 1f : value;

    /// <summary>Sets the offset so the pivot lands on the same texture spot as before the change.</summary>
    private static SurfaceData KeepPivot(DragStart start, SurfaceData changed, Float3 worldPivot)
    {
        Float3 p = Float4x4.TransformPoint(worldPivot, start.ToProjection);
        Float2 before = UVAt(start.Data, start.Normal, p);
        Float2 after = UVAt(changed, start.Normal, p);
        changed.Offset += before - after;
        return changed;
    }

    private static Float2 UVAt(SurfaceData data, Float3 normal, Float3 point)
    {
        CSGSurfaceGeometry.Basis(normal, data.Rotation, out Float3 u, out Float3 v);
        Float2 scale = CSGSurfaceGeometry.Effective(data, _dragTexel);
        return new Float2(Float3.Dot(point, u) / scale.X, Float3.Dot(point, v) / scale.Y) + data.Offset;
    }

    private static void ApplyDrag(string description, Func<DragStart, SurfaceData> change)
    {
        foreach (DragStart start in _drag)
        {
            if (!start.Surface.IsValid) continue;
            Undo.RecordObject(start.Surface.Brush, description);
            start.Surface.Brush.SetSurface(start.Surface.PlaneIndex, change(start));
        }
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid()) model!.SetDirty();
    }
}
