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

    // Hashed on reference identity so a destroyed brush still hashes the same.
    public override int GetHashCode()
        => HashCode.Combine(RuntimeHelpers.GetHashCode(Brush), PlaneIndex);
}

/// <summary>The selected surfaces and the edits on them, shared by the surface tool and the CSG panel.</summary>
public static class CSGSurfaceSelection
{
    private static readonly List<CSGSurfaceRef> _selected = new();

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
                if (found.Exists(f => f.Surface.Equals(other.Surface))) continue;
                if (Float3.Dot(other.Normal, found[i].Normal) < 0.7f || !SharesEdge(found[i], other)) continue;
                found.Add(other);
            }
        }

        _selected.Clear();
        foreach (ModelSurface surface in found.Skip(1)) _selected.Add(surface.Surface);
        _selected.Add(active.Surface);
    }

    private readonly record struct ModelSurface(CSGSurfaceRef Surface, Float3 Normal, List<Float3> Corners);

    private static List<ModelSurface> ModelSurfaces()
    {
        var result = new List<ModelSurface>();
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return result;

        foreach (CSGBrush brush in CSGSelection.BrushesIn(model!))
        {
            if (!brush.EnabledInHierarchy || CSGVisibility.IsHidden(brush)) continue;
            for (int plane = 0; plane < brush.Planes.Length; plane++)
            {
                var surface = new CSGSurfaceRef(brush, plane);
                List<Float3> corners = WorldCorners(surface);
                if (corners.Count < 3) continue;
                result.Add(new ModelSurface(surface, CSGSelection.WorldNormal(brush, plane), corners));
            }
        }
        return result;
    }

    private static bool TryActiveFace(out ModelSurface active)
    {
        active = default;
        if (Active is not CSGSurfaceRef surface || !surface.IsValid) return false;
        List<Float3> corners = WorldCorners(surface);
        if (corners.Count < 3) return false;
        active = new ModelSurface(surface, CSGSelection.WorldNormal(surface.Brush, surface.PlaneIndex), corners);
        return true;
    }

    private static void SelectWhere(Func<ModelSurface, bool> keep, CSGSurfaceRef? last = null)
    {
        _selected.Clear();
        foreach (ModelSurface surface in ModelSurfaces())
            if (keep(surface) && !surface.Surface.Equals(last)) _selected.Add(surface.Surface);
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

        Undo.RecordObject(brush, "Assign CSG Material");
        SurfaceData data = hit.Data;
        data.Material = material;
        brush.SetSurface(plane, data);
        model.SetDirty();
    }

    public static void SetMaterial(Material? material)
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
        => Edit("Set CSG Surface Flag", (data, _, _) =>
        {
            data.Flags = on ? data.Flags | flag : data.Flags & ~flag;
            return data;
        });

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
}
