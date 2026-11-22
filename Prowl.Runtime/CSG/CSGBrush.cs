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

    public static SurfaceData Default => new() { Scale = Float2.One };
}

/// <summary>One convex brush, defined by a set of outward facing planes.</summary>
[AddComponentMenu("CSG/CSG Brush")]
public sealed class CSGBrush : Component
{
    [SerializeField] private CSGOperation operation = CSGOperation.Additive;
    [SerializeField] private Plane[] planes = GeometryGenerator.BoxPlanes(Float3.One);
    [SerializeField] private SurfaceData[] surfaces = [];

    /// <summary>Set once the brush has been point edited, its planes are then derived from it.</summary>
    [SerializeField] private CSGControlMesh? controlMesh;

    public CSGBrushFlags Flags;

    [SerializeIgnore] private GeometryData? _shape;
    [SerializeIgnore] private int[] _faceToPlane = [];
    [SerializeIgnore] private ConvexSolidStatus _status = ConvexSolidStatus.Valid;

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

    /// <summary>Outward facing half spaces defining the solid. Assigning resizes <see cref="Surfaces"/> to match.</summary>
    public Plane[] Planes
    {
        get => planes;
        set
        {
            planes = value ?? [];

            // New planes no longer match the points, so drop the control mesh to be derived again.
            controlMesh = null;

            ResizeSurfaces();
            InvalidateShape();
        }
    }

    /// <summary>Texture and flag data, parallel to <see cref="Planes"/>.</summary>
    public SurfaceData[] Surfaces
    {
        get
        {
            if (surfaces.Length != planes.Length) ResizeSurfaces();
            return surfaces;
        }
    }

    /// <summary>Bumped by any edit that changes the CSG result.</summary>
    public int Version { get; private set; } = 1;

    /// <summary>Why the plane set failed to enclose a solid, if it did.</summary>
    public ConvexSolidStatus Status
    {
        get { EnsureShape(); return _status; }
    }

    public bool IsShapeValid => Status == ConvexSolidStatus.Valid;

    /// <summary>The derived convex solid in brush local space, or null when the planes enclose nothing.</summary>
    public GeometryData? Shape
    {
        get { EnsureShape(); return _shape; }
    }

    /// <summary>Maps an output face index back to its plane. Redundant planes produce no face.</summary>
    public int[] FaceToPlane
    {
        get { EnsureShape(); return _faceToPlane; }
    }

    /// <summary>Corners of the face a plane produced in brush local space, empty when the plane is redundant.</summary>
    public List<Float3> FaceCorners(int planeIndex)
    {
        var result = new List<Float3>();

        GeometryData? shape = Shape;
        if (shape == null) return result;

        for (int face = 0; face < _faceToPlane.Length && face < shape.Faces.Count; face++)
        {
            if (_faceToPlane[face] != planeIndex) continue;

            foreach (GeometryData.Vertex v in shape.Faces[face].NeighborVertices())
                result.Add(v.Point);
            break;
        }

        return result;
    }

    /// <summary>Whether the brush already has editable points. Deriving them reorders the planes into polygon order.</summary>
    public bool HasControlMesh => controlMesh != null;

    /// <summary>Editable points, derived from the planes on first access and kept. Null when the planes enclose nothing.</summary>
    public CSGControlMesh? ControlMesh
    {
        get
        {
            if (controlMesh != null) return controlMesh;

            CSGControlMesh? derived = CSGControlMesh.FromPlanes(planes);
            if (derived == null) return null;

            EnsureShape();
            if (derived.Polygons.Count != _faceToPlane.Length) return null;

            // Same planes in face order, so the shape does not drift and every surface stays on its face
            var reorderedPlanes = new Plane[_faceToPlane.Length];
            var reorderedSurfaces = new SurfaceData[_faceToPlane.Length];
            for (int face = 0; face < _faceToPlane.Length; face++)
            {
                int plane = _faceToPlane[face];
                reorderedPlanes[face] = planes[plane];
                reorderedSurfaces[face] = plane < surfaces.Length ? surfaces[plane] : SurfaceData.Default;
            }

            controlMesh = derived;
            planes = reorderedPlanes;
            surfaces = reorderedSurfaces;

            InvalidateShape();
            return controlMesh;
        }
    }

    /// <summary>Writes edited points back, rederiving the planes. Surface indices are kept.</summary>
    public void ApplyControlMesh()
    {
        if (controlMesh == null) return;

        planes = controlMesh.ToPlanes();
        ResizeSurfaces();
        InvalidateShape();
    }

    /// <summary>Replaces one plane, keeping its surface data.</summary>
    public void SetPlane(int index, Plane plane)
    {
        if (index < 0 || index >= planes.Length) throw new ArgumentOutOfRangeException(nameof(index));
        planes[index] = plane;
        InvalidateShape();
    }

    public void SetSurface(int index, SurfaceData surface)
    {
        if (index < 0 || index >= Surfaces.Length) throw new ArgumentOutOfRangeException(nameof(index));
        surfaces[index] = surface;
        Version++;
    }

    /// <summary>Rebuilds the box's planes, discarding any face edits.</summary>
    public void ResetToBox(Float3 size)
    {
        Planes = GeometryGenerator.BoxPlanes(size);
    }

    public override void OnValidate() => InvalidateShape();

    private void InvalidateShape()
    {
        _shape = null;
        Version++;
    }

    private void EnsureShape()
    {
        if (_shape != null) return;

        ConvexSolidResult result = GeometryGenerator.ConvexFromPlanes(planes);
        _status = result.Status;
        _shape = result.Solid;
        _faceToPlane = result.IsValid ? BuildFaceMap(result) : [];
    }

    /// <summary>Each face's plane, found by matching the face against the planes rather than by counting them.</summary>
    private int[] BuildFaceMap(ConvexSolidResult result)
    {
        GeometryData solid = result.Solid!;
        var map = new int[solid.Faces.Count];

        for (int face = 0; face < map.Length; face++)
        {
            Float3 normal = solid.Faces[face].Normal();
            Float3 centre = solid.Faces[face].Center();
            float best = float.MaxValue;
            map[face] = -1;

            for (int plane = 0; plane < planes.Length; plane++)
            {
                float score = (1f - Float3.Dot(normal, planes[plane].Normal)) + MathF.Abs(planes[plane].GetSignedDistanceToPoint(centre));
                if (score >= best) continue;
                best = score;
                map[face] = plane;
            }
        }

        return map;
    }

    private void ResizeSurfaces()
    {
        var resized = new SurfaceData[planes.Length];
        for (int i = 0; i < resized.Length; i++)
            resized[i] = i < surfaces.Length ? surfaces[i] : SurfaceData.Default;
        surfaces = resized;
    }
}
