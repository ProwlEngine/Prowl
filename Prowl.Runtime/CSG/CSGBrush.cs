// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

[Flags]
public enum CSGBrushFlags
{
    None = 0,
    NoRender = 1 << 0,
    NoCollision = 1 << 1,
}

[Flags]
public enum CSGSurfaceFlags
{
    None = 0,
    NoRender = 1 << 0,
    NoCollision = 1 << 1,
    /// <summary>Project texture coordinates in model space, so the texture stays put as the brush moves.</summary>
    WorldSpaceTexture = 1 << 2,
}

/// <summary>How one face of a brush is textured and whether it contributes to the output.</summary>
public struct SurfaceData
{
    public Material? Material;
    public Float2 Offset;
    public Float2 Scale;
    public float Rotation;
    public CSGSurfaceFlags Flags;

    /// <summary>Faces sharing a nonzero group and a corner get averaged normals.</summary>
    public int SmoothingGroup;

    public static SurfaceData Default => new() { Scale = Float2.One, Flags = CSGSurfaceFlags.WorldSpaceTexture };
}

/// <summary>One brush: a closed solid given by its points and faces. It can be any shape, concave or with bent faces.</summary>
[AddComponentMenu("CSG/CSG Brush")]
public sealed class CSGBrush : Component
{
    [SerializeField] private CSGOperation operation = CSGOperation.Additive;
    [SerializeField, HideInInspector] private CSGControlMesh controlMesh = CSGControlMesh.FromPlanes(GeometryGenerator.BoxPlanes(Float3.One))!;

    /// <summary>Texture and flag data, one per face of <see cref="controlMesh"/>.</summary>
    [SerializeField, HideInInspector] private SurfaceData[] surfaces = [];

    public CSGBrushFlags Flags;

    /// <summary>Hidden in the editor's viewport. It still shapes the model and is never hidden in a game.</summary>
    public bool HiddenInEditor;

    [SerializeIgnore] private GeometryData? _shape;
    [SerializeIgnore] private Plane[]? _planes;
    [SerializeIgnore] private int[] _faceToPlane = [];

    // The points the CSG builds from: the control mesh itself when it encloses a solid, else a repaired copy of it
    [SerializeIgnore] private CSGControlMesh? _buildMesh;
    [SerializeIgnore] private bool _valid;

    public CSGOperation Operation
    {
        get => operation;
        set
        {
            if (operation == value) return;
            operation = value;
            Version++;
        }
    }

    /// <summary>The points and faces that are the brush. Change them, then call <see cref="ApplyControlMesh"/>.</summary>
    public CSGControlMesh ControlMesh => controlMesh;

    /// <summary>One outward plane per face, fitted through its corners. Derived from the points, for texturing and face directions.</summary>
    public Plane[] Planes => _planes ??= controlMesh.ToPlanes();

    /// <summary>Texture and flag data, one per face.</summary>
    public SurfaceData[] Surfaces
    {
        get
        {
            if (surfaces.Length != controlMesh.Polygons.Count) ResizeSurfaces();
            return surfaces;
        }
    }

    /// <summary>Bumped by any edit that changes the CSG result.</summary>
    public int Version { get; private set; } = 1;

    /// <summary>Whether the faces enclose a solid, as they are or once repaired. A brush that does not is left out of the CSG.</summary>
    public bool IsShapeValid
    {
        get { EnsureShape(); return _valid; }
    }

    /// <summary>Whether the faces had stray faces clipped or holes filled to enclose a solid. The control mesh keeps them as made.</summary>
    public bool IsRepaired
    {
        get { EnsureShape(); return _valid && !ReferenceEquals(_buildMesh, controlMesh); }
    }

    /// <summary>The solid as a mesh in brush local space, built from the repaired faces when they needed it, or null when it encloses nothing.</summary>
    public GeometryData? Shape
    {
        get { EnsureShape(); return _shape; }
    }

    /// <summary>Maps a face of <see cref="Shape"/> to the brush face, and so the surface, it came from.</summary>
    public int[] FaceToPlane
    {
        get { EnsureShape(); return _faceToPlane; }
    }

    /// <summary>Corners of one face in brush local space.</summary>
    public List<Float3> FaceCorners(int face)
    {
        var result = new List<Float3>();
        foreach (int i in controlMesh.PolygonPoints(face)) result.Add(controlMesh.Points[i]);
        return result;
    }

    /// <summary>
    /// Makes the brush the convex solid a plane set encloses. Surfaces follow their planes where given, and the result
    /// says which plane each face lies on. False, and nothing changed, when the planes enclose nothing.
    /// </summary>
    public bool SetPlanes(IReadOnlyList<Plane> planes, IReadOnlyList<SurfaceData>? surfacesByPlane, out int[] planeOfFace)
    {
        CSGControlMesh? built = CSGControlMesh.FromPlanes(planes, out planeOfFace);
        if (built == null) return false;

        var resurfaced = new SurfaceData[built.Polygons.Count];
        for (int face = 0; face < resurfaced.Length; face++)
        {
            int plane = planeOfFace[face];
            resurfaced[face] = surfacesByPlane != null && plane < surfacesByPlane.Count ? surfacesByPlane[plane] : SurfaceData.Default;
        }

        SetShape(built, resurfaced);
        return true;
    }

    /// <summary>Makes the brush the convex solid a plane set encloses, every face with the default surface.</summary>
    public bool SetPlanes(IReadOnlyList<Plane> planes) => SetPlanes(planes, null, out _);

    /// <summary>Called after the points or faces of <see cref="ControlMesh"/> were changed in place.</summary>
    public void ApplyControlMesh()
    {
        ResizeSurfaces();
        InvalidateShape();
    }

    /// <summary>Puts a whole shape in place, its points and its surfaces, as an edit or a cancelled edit does.</summary>
    public void SetShape(CSGControlMesh points, SurfaceData[] newSurfaces)
    {
        controlMesh = points;
        surfaces = newSurfaces;
        ResizeSurfaces();
        InvalidateShape();
    }

    public void SetSurface(int index, SurfaceData surface)
    {
        if (index < 0 || index >= Surfaces.Length) throw new ArgumentOutOfRangeException(nameof(index));
        surfaces[index] = surface;
        Version++;
    }

    /// <summary>Makes the brush a box of the given size about its origin.</summary>
    public void ResetToBox(Float3 size) => SetPlanes(GeometryGenerator.BoxPlanes(size));

    public override void OnValidate() => InvalidateShape();

    private void InvalidateShape()
    {
        _shape = null;
        _planes = null;
        Version++;
    }

    private void EnsureShape()
    {
        if (_shape != null) return;

        int[]? sourceOfPolygon = null;
        _buildMesh = controlMesh.IsValid() ? controlMesh : controlMesh.Repaired(out sourceOfPolygon);
        _valid = _buildMesh != null;
        _shape = _buildMesh?.ToMesh(out _faceToPlane);
        if (_shape == null) _faceToPlane = [];

        // The repaired polygons name the surfaces of the polygons they came from
        if (sourceOfPolygon != null)
            for (int i = 0; i < _faceToPlane.Length; i++) _faceToPlane[i] = sourceOfPolygon[_faceToPlane[i]];
    }

    private void ResizeSurfaces()
    {
        int count = controlMesh.Polygons.Count;
        if (surfaces.Length == count) return;

        var resized = new SurfaceData[count];
        for (int i = 0; i < resized.Length; i++)
            resized[i] = i < surfaces.Length ? surfaces[i] : SurfaceData.Default;
        surfaces = resized;
    }
}
