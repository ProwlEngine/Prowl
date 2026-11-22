// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Operation = Prowl.Vector.Geometry.CSGOperation;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Whole brush operations on the current selection, shared by the scene tool and the CSG panel.</summary>
public static class CSGSelection
{
    /// <summary>The CSG model the selection belongs to, walking up from the active object.</summary>
    public static CSGModel? ActiveModel()
    {
        foreach (GameObject selected in Selection.GetSelected<GameObject>())
        {
            CSGModel? model = ModelOf(selected);
            if (model.IsValid()) return model;
        }
        return null;
    }

    /// <summary>The model an object belongs to, itself included.</summary>
    public static CSGModel? ModelOf(GameObject? go)
    {
        for (GameObject? current = go; current.IsValid(); current = current!.Parent)
        {
            CSGModel model = current!.GetComponent<CSGModel>();
            if (model.IsValid()) return model;
        }
        return null;
    }

    /// <summary>A brush plane's outward normal in world space, right under non uniform scale.</summary>
    public static Float3 WorldNormal(CSGBrush brush, int plane)
    {
        Float4x4 toLocal = brush.Transform.WorldToLocalMatrix;
        Float3 n = brush.Planes[plane].Normal;
        return Float3.NormalizeSafe(new Float3(Float3.Dot(toLocal.c0.XYZ, n), Float3.Dot(toLocal.c1.XYZ, n), Float3.Dot(toLocal.c2.XYZ, n)), Float3.UnitY);
    }

    /// <summary>Creates an empty CSG model at the scene root and selects it.</summary>
    public static CSGModel CreateModel()
    {
        GameObject go = CreateObject("CSG Model", null);
        CSGModel model = go.AddComponent<CSGModel>();

        Undo.RegisterCreatedObjectUndo(go, "Create CSG Model");
        Selection.Select(go);
        return model;
    }

    /// <summary>A new GameObject in the edited scene, under <paramref name="parent"/> when given. The caller registers the undo.</summary>
    public static GameObject CreateObject(string name, GameObject? parent)
    {
        Scene scene = EditorSceneManager.EditedWorld;
        var go = new GameObject(name);
        scene.Add(go, HierarchyPanel.SectionForNew(scene, parent));
        if (parent.IsValid()) go.SetParent(parent, worldPositionStays: false);
        return go;
    }

    /// <summary>Records objects and their CSG components before an edit, so the edit undoes as one step.</summary>
    public static void Record(string name, IEnumerable<GameObject> objects)
    {
        foreach (GameObject go in objects)
        {
            if (go.IsNotValid()) continue;
            Undo.RecordObject(go, name);
            foreach (Component component in go.GetComponents<Component>())
                if (component is CSGBrush or CSGGroup or CSGModel) Undo.RecordObject(component, name);
        }
    }

    public static List<CSGBrush> Brushes() => Components<CSGBrush>();

    /// <summary>
    /// The brush under a ray. Generated surfaces come first, so the walls of a carved room pick their subtractive brush.
    /// Brushes that produce no visible surface fall back to their own volume. Hidden brushes are seen through.
    /// </summary>
    public static bool PickBrush(CSGModel model, IEnumerable<CSGBrush> brushes, Ray ray, out CSGBrush brush, out float distance)
    {
        if (model.Raycast(ray, out brush, out _, out Float3 point, b => !CSGVisibility.IsHidden(b)))
        {
            distance = Float3.Distance(ray.Origin, point);
            return true;
        }

        distance = float.MaxValue;
        foreach (CSGBrush candidate in brushes)
        {
            if (CSGVisibility.IsHidden(candidate) || !candidate.IsShapeValid) continue;

            Float4x4 toLocal = candidate.Transform.WorldToLocalMatrix;
            Float3 origin = Float4x4.TransformPoint(ray.Origin, toLocal);
            Float3 direction = Float4x4.TransformNormal(ray.Direction, toLocal);
            if (!CSGBrushGeometry.Raycast(candidate.Planes, origin, direction, out float t) || t >= distance) continue;

            distance = t;
            brush = candidate;
        }
        return distance < float.MaxValue;
    }

    /// <summary>The surface of the plane among the first <paramref name="count"/> facing most like <paramref name="normal"/>.</summary>
    public static SurfaceData NearestSurface(CSGBrush brush, Float3 normal, int count)
    {
        Plane[] planes = brush.Planes;
        SurfaceData[] surfaces = brush.Surfaces;
        int best = -1;
        float bestDot = float.MinValue;
        for (int i = 0; i < count && i < planes.Length; i++)
        {
            float dot = Float3.Dot(planes[i].Normal, normal);
            if (dot <= bestDot) continue;
            bestDot = dot;
            best = i;
        }
        return best >= 0 ? surfaces[best] : SurfaceData.Default;
    }

    /// <summary>Moves a new object to just after another in their parent, since sibling order is CSG order.</summary>
    public static void PlaceAfter(GameObject created, GameObject source)
    {
        if (ReferenceEquals(created.Parent, source.Parent)) created.SiblingIndex = source.SiblingIndex + 1;
    }

    /// <summary>Every brush under a model, nested models excluded.</summary>
    public static List<CSGBrush> BrushesIn(CSGModel model)
    {
        var result = new List<CSGBrush>();
        Collect(model.GameObject, result);
        return result;
    }

    private static void Collect(GameObject parent, List<CSGBrush> result)
    {
        foreach (GameObject child in parent.Children)
        {
            if (child.GetComponent<CSGModel>().IsValid()) continue;
            CSGBrush brush = child.GetComponent<CSGBrush>();
            if (brush.IsValid()) result.Add(brush);
            Collect(child, result);
        }
    }

    public static List<CSGGroup> Groups() => Components<CSGGroup>();

    private static List<T> Components<T>() where T : Component
    {
        var result = new List<T>();
        foreach (GameObject go in Selection.GetSelected<GameObject>())
        {
            T component = go.GetComponent<T>();
            if (component.IsValid()) result.Add(component);
        }
        return result;
    }

    private static List<GameObject> SelectedObjects() => Selection.GetSelected<GameObject>().ToList();

    public static void SetOperation(Operation operation)
    {
        List<CSGBrush> brushes = Brushes();
        List<CSGGroup> groups = Groups();
        if (brushes.Count == 0 && groups.Count == 0) return;

        Record("Set CSG Operation", SelectedObjects());

        foreach (CSGBrush brush in brushes) brush.Operation = operation;
        foreach (CSGGroup group in groups) group.Operation = operation;

        MarkDirty();
    }

    /// <summary>Mirrors the selection about its own centre.</summary>
    public static void Flip(Float3 axis)
    {
        List<CSGBrush> brushes = Brushes();
        if (brushes.Count == 0) return;

        Record("Flip CSG Brush", SelectedObjects());

        Float3 n = Float3.Normalize(axis);
        Float3 center = Centre(brushes);

        foreach (CSGBrush brush in brushes)
        {
            brush.Planes = CSGBrushGeometry.Mirror(brush.Planes, axis, brush.Transform.LocalToWorldMatrix);

            // Mirror the pivot about the selection centre so the whole selection reflects as one.
            Float3 offset = brush.Transform.Position - center;
            brush.Transform.Position = center + offset - n * (2f * Float3.Dot(offset, n));
        }

        MarkDirty();
    }

    /// <summary>Moves each pivot onto its solid's centre without moving the geometry.</summary>
    public static void CentrePivot()
    {
        List<CSGBrush> brushes = Brushes();
        if (brushes.Count == 0) return;

        Record("Centre CSG Pivot", SelectedObjects());

        foreach (CSGBrush brush in brushes)
        {
            Float3 centroid = CSGBrushGeometry.Centroid(brush.Shape);
            if (Float3.LengthSquared(centroid) < 1e-9f) continue;

            brush.Planes = CSGBrushGeometry.Translate(brush.Planes, -centroid);
            brush.Transform.Position += brush.Transform.Rotation * (centroid * brush.Transform.LocalScale);
        }

        MarkDirty();
    }

    /// <summary>Makes the selected groups inert, so they organise the hierarchy without being a CSG boundary.</summary>
    public static void SetPassThrough()
    {
        List<CSGGroup> groups = Groups();
        if (groups.Count == 0) return;

        Record("Set CSG Pass Through", SelectedObjects());

        bool allPassing = groups.TrueForAll(g => g.PassThrough);
        foreach (CSGGroup group in groups) group.PassThrough = !allPassing;

        MarkDirty();
    }

    /// <summary>Moves the selection by an exact amount, optionally leaving a copy behind.</summary>
    public static void MoveBy(Float3 offset, bool clone)
    {
        if (Brushes().Count == 0) return;
        if (clone) Duplicate();

        Record(clone ? "Clone and Move CSG" : "Move CSG", SelectedObjects());

        foreach (CSGBrush brush in Brushes())
            brush.Transform.Position += offset;

        MarkDirty();
    }

    /// <summary>Rotates the selection about a pivot, orbiting positions too, optionally leaving a copy behind.</summary>
    public static void RotateBy(Float3 eulerDegrees, Float3 pivot, bool clone)
    {
        if (Brushes().Count == 0) return;
        if (clone) Duplicate();

        Record(clone ? "Clone and Rotate CSG" : "Rotate CSG", SelectedObjects());

        Quaternion rotation = Quaternion.FromEuler(eulerDegrees);

        foreach (CSGBrush brush in Brushes())
        {
            brush.Transform.Position = pivot + rotation * (brush.Transform.Position - pivot);
            brush.Transform.Rotation = rotation * brush.Transform.Rotation;
        }

        MarkDirty();
    }

    /// <summary>Centre of the selected brushes, the default pivot for a rotation.</summary>
    public static Float3 SelectionCentre()
    {
        List<CSGBrush> brushes = Brushes();
        return brushes.Count == 0 ? Float3.Zero : Centre(brushes);
    }

    /// <summary>Snaps each brush's pivot onto the grid.</summary>
    public static void SnapToGrid()
    {
        List<CSGBrush> brushes = Brushes();
        if (brushes.Count == 0) return;

        Record("Snap CSG to Grid", SelectedObjects());

        foreach (CSGBrush brush in brushes)
            brush.Transform.Position = CSGSnap.Round(brush.Transform.Position);

        MarkDirty();
    }

    public static void Duplicate()
    {
        List<CSGBrush> brushes = Brushes();
        if (brushes.Count == 0) return;

        List<GameObject> sources = GameObjectClipboard.FilterToRoots(brushes.Select(b => b.GameObject));
        List<GameObject> copies = GameObjectClipboard.Duplicate(sources);
        for (int i = 0; i < copies.Count && i < sources.Count; i++) PlaceAfter(copies[i], sources[i]);
        foreach (GameObject copy in copies) Undo.RegisterCreatedObjectUndo(copy, "Duplicate CSG Brush");

        MarkDirty();
    }

    private static Float3 Centre(List<CSGBrush> brushes)
    {
        Float3 sum = Float3.Zero;
        foreach (CSGBrush brush in brushes) sum += brush.Transform.Position;
        return sum / brushes.Count;
    }

    private static void MarkDirty()
    {
        CSGModel? model = ActiveModel();
        if (model.IsValid()) model!.SetDirty();
    }
}
