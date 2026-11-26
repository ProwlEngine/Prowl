// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Modeling;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Operation = Prowl.Vector.Geometry.CSGOperation;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Whole brush operations on the current selection, shared by the scene tool and the CSG panel.</summary>
public static class CSGSelection
{
    /// <summary>The CSG model the selection belongs to, walking up from the active object.</summary>
    /// <remarks>
    /// The last model worked on stays active while nothing is selected, so a click into empty space does not end the CSG
    /// session. Selecting anything outside a model does.
    /// </remarks>
    public static CSGModel? ActiveModel()
    {
        bool any = false;
        foreach (GameObject selected in Selection.GetSelected<GameObject>())
        {
            any = true;
            CSGModel? model = ModelOf(selected);
            if (model.IsValid() && IsEdited(model!)) return _pinned = model;
        }

        if (any || _pinned.IsNotValid() || !IsEdited(_pinned!)) _pinned = null;
        return _pinned;
    }

    // A model asleep under a model or prefab being edited is out of reach, its world is not the one being edited
    private static bool IsEdited(CSGModel model) => model.GameObject.Scene == EditorSceneManager.EditedWorld;

    private static CSGModel? _pinned;

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

        // An empty model has nothing to select, so the first thing to do is draw
        CSGEditMode.Current = CSGMode.Create;
        return model;
    }

    /// <summary>A new GameObject of unit world scale in the edited scene, under <paramref name="parent"/> when given. The caller registers the undo.</summary>
    public static GameObject CreateObject(string name, GameObject? parent)
    {
        Scene scene = EditorSceneManager.EditedWorld;
        var go = new GameObject(name);
        scene.Add(go, HierarchyPanel.SectionForNew(scene, parent));
        if (parent.IsValid())
        {
            go.SetParent(parent, worldPositionStays: false);

            // Unit size in the world, so what is drawn under a scaled model or group is not scaled again
            Float3 parentScale = parent!.Transform.LossyScale;
            go.Transform.LocalScale = new Float3(Inverse(parentScale.X), Inverse(parentScale.Y), Inverse(parentScale.Z));
        }
        return go;
    }

    private static float Inverse(float scale) => MathF.Abs(scale) > 1e-6f ? 1f / scale : 1f;

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

    private static CSGBrush? _single;

    static CSGSelection() => Selection.OnSelectionChanged += () => _single = null;

    /// <summary>The brush when exactly one is selected, else null. Kept until the selection changes or the brush goes.</summary>
    public static CSGBrush? SingleBrush()
    {
        if (_single.IsValid()) return _single;
        List<CSGBrush> brushes = Brushes();
        _single = brushes.Count == 1 ? brushes[0] : null;
        return _single;
    }

    /// <summary>The selected brushes and groups, without any that sit inside another selected one.</summary>
    public static List<GameObject> Roots()
        => GameObjectClipboard.FilterToRoots(Selection.GetSelected<GameObject>()
            .Where(go => go.IsValid() && (go.GetComponent<CSGBrush>().IsValid() || go.GetComponent<CSGGroup>().IsValid())));

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
            if (!candidate.ControlMesh.Raycast(origin, direction, out float t, out _) || t >= distance) continue;

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

    /// <summary>
    /// Swaps every additive brush and group under a model for subtractive and back, intersecting ones left alone, so a
    /// model can move between the additive and subtractive workflows.
    /// </summary>
    public static void InvertOperations(CSGModel model)
    {
        var changed = new List<EngineObject>();
        var groups = new List<CSGGroup>();
        CollectGroups(model.GameObject, groups);
        foreach (CSGBrush brush in BrushesIn(model)) if (brush.Operation != Operation.Intersect) changed.Add(brush);
        foreach (CSGGroup group in groups) if (group.Operation != Operation.Intersect) changed.Add(group);
        if (changed.Count == 0) return;

        Undo.RecordObjects(changed, "Invert CSG Brushes");
        foreach (EngineObject item in changed)
        {
            if (item is CSGBrush brush) brush.Operation = Inverted(brush.Operation);
            else if (item is CSGGroup group) group.Operation = Inverted(group.Operation);
        }
        model.SetDirty();
    }

    private static Operation Inverted(Operation operation) => operation == Operation.Additive ? Operation.Subtractive : Operation.Additive;

    private static void CollectGroups(GameObject parent, List<CSGGroup> result)
    {
        foreach (GameObject child in parent.Children)
        {
            if (child.GetComponent<CSGModel>().IsValid()) continue;
            CSGGroup group = child.GetComponent<CSGGroup>();
            if (group.IsValid()) result.Add(group);
            CollectGroups(child, result);
        }
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
        List<GameObject> roots = Roots();
        if (roots.Count == 0) return;

        // Every brush inside a selected group mirrors too, about the centre of the whole selection
        List<CSGBrush> brushes = BrushesUnder(roots);
        Record("Flip CSG Brush", brushes.Select(b => b.GameObject));

        Float3 n = Float3.Normalize(axis);
        Float3 center = Centre(roots);

        // Every pivot's mirrored place is worked out before any moves, since a brush moving takes its children along
        var targets = new List<Float3>(brushes.Count);
        foreach (CSGBrush brush in brushes)
        {
            Float3 offset = brush.Transform.Position - center;
            targets.Add(center + offset - n * (2f * Float3.Dot(offset, n)));
        }

        for (int i = 0; i < brushes.Count; i++)
        {
            CSGBrush brush = brushes[i];
            Float4x4 toWorld = brush.Transform.LocalToWorldMatrix;
            Float3 target = targets[i];

            // Locked textures mirror with the points they are fixed to, each corner keeping its UV
            CSGSurfaceSelection.KeepTextures(brush, (_, data) => !data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture), () =>
            {
                if (CSGBrushGeometry.TryMirrorMatrix(axis, toWorld, out Float4x4 local))
                {
                    brush.ControlMesh.TransformPoints(local);
                    brush.ApplyControlMesh();
                }

                brush.Transform.Position = target;
            });
        }

        MarkDirty();
    }

    /// <summary>Moves a brush's pivot to a point in its own space, the shape and any children staying where they are in the world.</summary>
    public static void MovePivot(CSGBrush brush, Float3 local)
    {
        if (Float3.LengthSquared(local) < 1e-9f) return;

        var children = brush.GameObject.Children.Select(c => (c, c.Transform.Position, c.Transform.Rotation)).ToList();

        Float3 pivot = Float4x4.TransformPoint(local, brush.Transform.LocalToWorldMatrix);

        // Locked textures project in brush space, so they are refitted to stay where they were on the faces
        CSGSurfaceSelection.KeepTextures(brush, (_, data) => !data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture), () =>
        {
            brush.ControlMesh.TransformPoints(Float4x4.CreateTranslation(-local));
            brush.ApplyControlMesh();
            brush.Transform.Position = pivot;
        });

        foreach ((GameObject child, Float3 position, Quaternion rotation) in children)
        {
            child.Transform.Position = position;
            child.Transform.Rotation = rotation;
        }
    }

    /// <summary>Moves each selected pivot onto its solid's centre without moving the geometry.</summary>
    public static void CentrePivot() => CentrePivots(Brushes(), Groups().Where(g => g.GetComponent<CSGBrush>().IsNotValid()).ToList(), "Centre CSG Pivot");

    /// <summary>Moves the pivot of every brush and group in a model onto its solid's centre without moving the geometry.</summary>
    public static void CentreAllPivots(CSGModel model)
    {
        var groups = new List<CSGGroup>();
        CollectGroups(model.GameObject, groups);
        CentrePivots(BrushesIn(model), [.. groups.Where(g => g.GetComponent<CSGBrush>().IsNotValid())], "Centre All CSG Pivots");
    }

    private static void CentrePivots(List<CSGBrush> brushes, List<CSGGroup> groups, string name)
    {
        brushes.RemoveAll(b => b.Shape == null);
        if (brushes.Count == 0 && groups.Count == 0) return;

        Record(name, brushes.Select(b => b.GameObject).Concat(groups.Select(g => g.GameObject))
            .SelectMany(go => go.Children.Prepend(go)).Distinct());

        foreach (CSGBrush brush in brushes)
            MovePivot(brush, CSGBrushGeometry.Centroid(brush.Shape));

        // A group's pivot goes to the middle of the brushes inside it, which stay where they are
        foreach (CSGGroup group in groups)
        {
            List<CSGBrush> inside = BrushesUnder([group.GameObject]);
            if (inside.Count == 0) continue;

            Float3 centre = Float3.Zero;
            foreach (CSGBrush brush in inside)
                centre += Float4x4.TransformPoint(CSGBrushGeometry.Centroid(brush.Shape), brush.Transform.LocalToWorldMatrix);
            centre /= inside.Count;

            var children = group.GameObject.Children.Select(c => (c, c.Transform.Position, c.Transform.Rotation)).ToList();
            group.Transform.Position = centre;
            foreach ((GameObject child, Float3 position, Quaternion rotation) in children)
            {
                child.Transform.Position = position;
                child.Transform.Rotation = rotation;
            }
        }

        MarkDirty();
    }

    /// <summary>
    /// Bakes the rotation and/or scale of some objects and everything under them into their brushes' points, every pivot
    /// staying where it is. Textures keep their density. False, with nothing changed, when anything but brushes has geometry.
    /// </summary>
    public static bool ApplyTransform(IEnumerable<GameObject> objects, ModelEditingMode.TransformParts parts)
    {
        List<GameObject> nodes = [.. GameObjectClipboard.FilterToRoots(objects).SelectMany(r => r.GetChildrenDeep().Prepend(r))];
        if (nodes.Count == 0) return false;

        foreach (GameObject go in nodes)
        {
            if (!HoldsGeometry(go)) continue;
            Debug.LogWarning($"Apply Transform: {go.Name} has a mesh or collider that cannot take the transform, nothing was changed.");
            return false;
        }

        var positions = nodes.Select(n => n.Transform.Position).ToList();
        var rotations = nodes.Select(n => n.Transform.Rotation).ToList();
        var brushes = nodes.Select(n => n.GetComponent<CSGBrush>()).Where(b => b.IsValid()).Select(b => (b, b.Transform.LocalToWorldMatrix)).ToList();
        Record("Apply CSG Transform", nodes);

        // Parents come before their children, so each child is placed against its parent's final transform
        for (int i = 0; i < nodes.Count; i++)
        {
            Transform t = nodes[i].Transform;
            if (parts.HasFlag(ModelEditingMode.TransformParts.Rotation)) t.LocalRotation = Quaternion.Identity;
            else t.Rotation = rotations[i];
            if (parts.HasFlag(ModelEditingMode.TransformParts.Scale)) t.LocalScale = Float3.One;
            t.Position = positions[i];
        }

        var models = new HashSet<CSGModel>();
        foreach ((CSGBrush brush, Float4x4 before) in brushes)
        {
            Bake(brush, brush.Transform.WorldToLocalMatrix * before);
            CSGModel? model = ModelOf(brush.GameObject);
            if (model.IsValid()) models.Add(model!);
        }
        foreach (CSGModel model in models) model.SetDirty();
        return true;
    }

    private static bool HoldsGeometry(GameObject go)
    {
        if (go.GetComponent<ModelObject>().IsValid()) return true;
        if (go.GetComponent<CSGModel>().IsValid()) return false;
        return go.GetComponents<Component>().Any(c => c is IMaterialRenderer or Collider);
    }

    private static void Bake(CSGBrush brush, Float4x4 change)
    {
        Float4x4 linear = change;
        linear.c3 = new Float4(0, 0, 0, 1);
        if (MathF.Abs(Float4x4.Determinant(linear)) < 1e-12f) return;

        // The stretch goes in unrefitted so locked textures keep their density, the turn refitted so they turn with the faces
        change.Decompose(out _, out Quaternion turn, out _);
        Float4x4 rigid = Float4x4.CreateFromQuaternion(turn);
        brush.ControlMesh.TransformPoints(rigid.Invert() * change);
        brush.ApplyControlMesh();

        CSGSurfaceSelection.KeepTextures(brush, (_, data) => !data.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture), () =>
        {
            brush.ControlMesh.TransformPoints(rigid);
            brush.ApplyControlMesh();
        });
    }

    /// <summary>A brush's solid as a box: its centre in the world and its size along its own axes in world units.</summary>
    public static bool TryBox(CSGBrush brush, out Float3 centre, out Float3 size)
    {
        centre = size = Float3.Zero;
        if (!brush.IsShapeValid) return false;

        AABB local = CSGBrushGeometry.Bounds(brush.Shape);
        Float3 scale = brush.Transform.LossyScale;
        centre = Float4x4.TransformPoint((local.Min + local.Max) * 0.5f, brush.Transform.LocalToWorldMatrix);
        Float3 extent = local.Max - local.Min;
        size = new Float3(extent.X * MathF.Abs(scale.X), extent.Y * MathF.Abs(scale.Y), extent.Z * MathF.Abs(scale.Z));
        return true;
    }

    /// <summary>Resizes a brush's solid about its own centre, reshaping it rather than scaling its transform.</summary>
    public static void Resize(CSGBrush brush, Float3 size)
    {
        if (!TryBox(brush, out _, out Float3 current)) return;

        AABB local = CSGBrushGeometry.Bounds(brush.Shape);
        Float3 c = (local.Min + local.Max) * 0.5f;
        Float3 factor = new(Ratio(size.X, current.X), Ratio(size.Y, current.Y), Ratio(size.Z, current.Z));
        Float4x4 reshape = Float4x4.CreateTranslation(c) * Float4x4.CreateScale(factor) * Float4x4.CreateTranslation(-c);

        Record("Resize CSG Brush", [brush.GameObject]);
        brush.ControlMesh.TransformPoints(reshape);
        brush.ApplyControlMesh();
        MarkDirty();
    }

    private static float Ratio(float wanted, float current) => wanted > 1e-4f && current > 1e-6f ? wanted / current : 1f;

    /// <summary>Moves a brush so the centre of its solid lands on <paramref name="centre"/>.</summary>
    public static void MoveCentre(CSGBrush brush, Float3 centre)
    {
        if (!TryBox(brush, out Float3 current, out _)) return;
        Record("Move CSG Brush", [brush.GameObject]);
        brush.Transform.Position += centre - current;
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
        if (clone) _lastClone = offset;
        if (Roots().Count == 0) return;
        if (clone) Duplicate();

        List<GameObject> roots = Roots();
        Record(clone ? "Clone and Move CSG" : "Move CSG", roots);

        foreach (GameObject go in roots)
            go.Transform.Position += offset;

        MarkDirty();
    }

    /// <summary>Rotates the selection about a pivot, orbiting positions too, optionally leaving a copy behind.</summary>
    public static void RotateBy(Float3 eulerDegrees, Float3 pivot, bool clone)
    {
        if (Roots().Count == 0) return;
        if (clone) Duplicate();

        List<GameObject> roots = Roots();
        Record(clone ? "Clone and Rotate CSG" : "Rotate CSG", roots);

        Quaternion rotation = Quaternion.FromEuler(eulerDegrees);

        foreach (GameObject go in roots)
        {
            go.Transform.Position = pivot + rotation * (go.Transform.Position - pivot);
            go.Transform.Rotation = rotation * go.Transform.Rotation;
        }

        MarkDirty();
    }

    /// <summary>Moves the selection one place earlier or later among its siblings, which is where it falls in the CSG order.</summary>
    public static void MoveInOrder(int direction)
    {
        List<GameObject> roots = Roots();
        roots.Sort((a, b) => direction > 0 ? b.SiblingIndex.CompareTo(a.SiblingIndex) : a.SiblingIndex.CompareTo(b.SiblingIndex));

        foreach (GameObject go in roots)
        {
            int count = go.Parent.IsValid() ? go.Parent!.Children.Count : int.MaxValue;
            int index = go.SiblingIndex + direction;
            if (index < 0 || index >= count) continue;
            Undo.SetSiblingIndex(go, index, direction < 0 ? "Move CSG Earlier" : "Move CSG Later");
        }
        MarkDirty();
    }

    /// <summary>
    /// A cut piece of a brush as points, faces and surfaces: each face keeps the surface of the face it came from, and
    /// the cap the cut made takes <paramref name="capSurface"/>. Null when the piece encloses nothing.
    /// </summary>
    internal static (CSGControlMesh Mesh, SurfaceData[] Surfaces)? PieceOf(GeometryData piece, SurfaceData[] surfaces, int[] faceToSurface, SurfaceData capSurface)
    {
        CSGControlMesh? mesh = CSGControlMesh.FromMesh(piece);
        if (mesh == null || !mesh.IsValid()) return null;

        var result = new List<SurfaceData>(mesh.Polygons.Count);
        foreach (GeometryData.Face face in piece.Faces)
        {
            if (face.VertCount < 3) continue;
            int source = GeometryOperators.SliceSourceFace(face);
            int surface = source >= 0 && source < faceToSurface.Length ? faceToSurface[source] : -1;
            result.Add(surface >= 0 && surface < surfaces.Length ? surfaces[surface] : capSurface);
        }
        return (mesh, [.. result]);
    }

    /// <summary>
    /// A brush's shape with everything in front of a plane cut away and the cut closed with a cap of
    /// <paramref name="capSurface"/>. Null when nothing would be left.
    /// </summary>
    internal static (CSGControlMesh Mesh, SurfaceData[] Surfaces)? CutAway(CSGControlMesh mesh, SurfaceData[] surfaces, Plane plane, SurfaceData capSurface)
    {
        GeometryData shape = mesh.ToMesh(out int[] faceToPolygon);
        GeometryOperators.Slice(shape, plane, out GeometryData back, out _);
        return back.Faces.Count == 0 ? null : PieceOf(back, surfaces, faceToPolygon, capSurface);
    }

    /// <summary>Puts a generated brush's curved faces in one smoothing group of their own.</summary>
    internal static void ApplySmoothing(CSGBrush brush, int[] planeOfFace, int smoothPlanes)
    {
        if (smoothPlanes <= 0) return;

        int group = CSGSurfaceSelection.UnusedSmoothingGroup();
        SurfaceData[] surfaces = brush.Surfaces;
        for (int face = 0; face < surfaces.Length && face < planeOfFace.Length; face++)
        {
            if (planeOfFace[face] >= smoothPlanes) continue;
            SurfaceData data = surfaces[face];
            data.SmoothingGroup = group;
            brush.SetSurface(face, data);
        }
    }

    private static Float3? _lastClone;

    /// <summary>The step a clone was dragged by, which <see cref="RepeatClone"/> repeats.</summary>
    public static void RememberClone(Float3 step) => _lastClone = step;

    /// <summary>Clones the selection once more by the last clone's step.</summary>
    public static void RepeatClone()
    {
        if (_lastClone is Float3 step) MoveBy(step, clone: true);
    }

    /// <summary>Centre of the selected brushes and groups, the default pivot for a rotation.</summary>
    public static Float3 SelectionCentre()
    {
        List<GameObject> roots = Roots();
        return roots.Count == 0 ? Float3.Zero : Centre(roots);
    }

    /// <summary>Moves each selected brush and group so its bounds land on the world grid.</summary>
    public static void SnapToGrid()
    {
        List<GameObject> roots = Roots();
        if (roots.Count == 0) return;

        Record("Snap CSG to Grid", roots);

        foreach (GameObject go in roots)
            go.Transform.Position += SceneViewPanel.SnapBoundsToGrid(WorldPoints([go]), Float3.Zero, CSGSnap.Grid, Quaternion.Identity, everyAxis: true);

        MarkDirty();
    }

    /// <summary>The points of every brush in the objects and under them, in the world.</summary>
    public static List<Float3> WorldPoints(IEnumerable<GameObject> objects)
    {
        var points = new List<Float3>();
        foreach (GameObject root in objects)
            foreach (GameObject go in root.GetChildrenDeep().Prepend(root))
            {
                CSGBrush brush = go.GetComponent<CSGBrush>();
                if (brush.IsNotValid() || brush.Shape == null) continue;
                Float4x4 toWorld = go.Transform.LocalToWorldMatrix;
                foreach (GeometryData.Vertex vertex in brush.Shape.Vertices) points.Add(Float4x4.TransformPoint(vertex.Point, toWorld));
            }
        return points;
    }

    public static void Duplicate()
    {
        List<GameObject> sources = Roots();
        if (sources.Count == 0) return;

        List<GameObject> copies = GameObjectClipboard.Duplicate(sources);
        for (int i = 0; i < copies.Count && i < sources.Count; i++) PlaceAfter(copies[i], sources[i]);
        foreach (GameObject copy in copies) Undo.RegisterCreatedObjectUndo(copy, "Duplicate CSG Brush");

        MarkDirty();
    }

    private static Float3 Centre(List<GameObject> objects)
    {
        Float3 sum = Float3.Zero;
        foreach (GameObject go in objects) sum += go.Transform.Position;
        return sum / objects.Count;
    }

    /// <summary>The brushes on and inside some objects, nested models excluded.</summary>
    private static List<CSGBrush> BrushesUnder(List<GameObject> roots)
    {
        var result = new List<CSGBrush>();
        foreach (GameObject root in roots)
        {
            CSGBrush brush = root.GetComponent<CSGBrush>();
            if (brush.IsValid()) result.Add(brush);
            Collect(root, result);
        }
        return result;
    }

    private static void MarkDirty()
    {
        CSGModel? model = ActiveModel();
        if (model.IsValid()) model!.SetDirty();
    }
}
