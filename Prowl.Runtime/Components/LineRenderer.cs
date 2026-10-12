// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using Material = Prowl.Runtime.Resources.Material;

namespace Prowl.Runtime;

/// <summary>
/// Draws a ribbon through <see cref="Points"/>, given in this object's space, that turns to face whichever camera draws
/// it. Widths are in world units.
/// </summary>
[AddComponentMenu("Rendering/Line Renderer")]
[ComponentIcon("\uf4d7")] // Route
public class LineRenderer : Component, IMaterialRenderer, IRenderSync
{
    int IMaterialRenderer.MaterialSlotCount => 1;
    Material? IMaterialRenderer.GetMaterial(int slot) { MaterialSlots.CheckSingle(slot); return Material; }
    void IMaterialRenderer.SetMaterial(int slot, Material? material) { MaterialSlots.CheckSingle(slot); Material = material; }

    public Material? Material;
    public float StartWidth = 0.1f;
    public float EndWidth = 0.1f;
    public List<Float3> Points = [];
    public bool Loop = false;
    public Color StartColor = Color.White;
    public Color EndColor = Color.White;
    public TextureWrapMode TextureMode = TextureWrapMode.Stretch;
    public float TextureTiling = 1.0f; // Controls UV tiling for Tile mode
    public ShadowCasting ShadowCasting = ShadowCasting.Off;

    /// <summary>Unused: the ribbon faces the view in its shader, so it has no fixed normals.</summary>
    public bool RecalculateNormals = false;

    [System.NonSerialized, NotHeld] private Mesh? _mesh;
    [System.NonSerialized, NotHeld] private readonly RenderableState _renderable = new();
    [System.NonSerialized, NotHeld] private readonly Material?[] _materialSlot = new Material?[1];
    [System.NonSerialized, NotHeld] private GMaterial[] _gpuMaterials = [];
    [System.NonSerialized] private bool _isDirty = true;
    [System.NonSerialized] private AABB _pointBounds;

    // Cached state for change detection
    [System.NonSerialized] private List<Float3> _lastPoints = [];
    [System.NonSerialized] private float _lastStartWidth;
    [System.NonSerialized] private float _lastEndWidth;
    [System.NonSerialized] private bool _lastLoop;
    [System.NonSerialized] private Color _lastStartColor;
    [System.NonSerialized] private Color _lastEndColor;
    [System.NonSerialized] private TextureWrapMode _lastTextureMode;
    [System.NonSerialized] private float _lastTextureTiling;

    /// <summary>The renderable in the scene's world, invalid while nothing draws.</summary>
    public RenderableHandle Renderable => _renderable.Handle;

    void IRenderSync.Sync(RenderWorld world)
    {
        Material? material = Material;
        if (material.IsNotValid() || Points == null || Points.Count < 2)
        {
            _renderable.Detach(world);
            return;
        }

        if (NeedsRebuild())
        {
            Snapshot();
            BuildMesh();
            _isDirty = false;
        }

        Mesh? mesh = _mesh;
        if (mesh.IsNotValid())
        {
            _renderable.Detach(world);
            return;
        }
        mesh.Upload();

        _materialSlot[0] = material;
        Float4x4 matrix = Transform.LocalToWorldMatrix;
        _renderable.Sync(world, new RenderableState.Desc
        {
            Mesh = mesh.GpuIfLoaded,
            Materials = RenderableState.MaterialsFor(_materialSlot, 1, ref _gpuMaterials),
            Transform = (Double4x4)matrix,
            TransformVersion = Transform.WorldVersion,
            Flags = RenderableState.FlagsFor(GameObject, false, false) | ShadowCasting.ToFlags(),
            LayerMask = RenderableState.LayerBit(GameObject.LayerIndex),
            ObjectId = (uint)InstanceID,
            Bounds = WidenedBounds(),
        });
    }

    void IRenderSync.Detach(RenderWorld world) => _renderable.Detach(world);

    // The points' bounds grown by the widest width, which is in world units, so it grows by more on a shrunk object
    private AABB WidenedBounds()
    {
        Float3 scale = Transform.LossyScale;
        float smallest = Maths.Max(Maths.Min(Maths.Abs(scale.X), Maths.Min(Maths.Abs(scale.Y), Maths.Abs(scale.Z))), 0.0001f);
        float pad = Maths.Max(StartWidth, EndWidth) / smallest;
        Float3 expansion = new(pad, pad, pad);
        return new AABB(_pointBounds.Min - expansion, _pointBounds.Max + expansion);
    }

    private bool NeedsRebuild() =>
        _isDirty ||
        _mesh.IsNotValid() ||
        StartWidth != _lastStartWidth ||
        EndWidth != _lastEndWidth ||
        Loop != _lastLoop ||
        !StartColor.Equals(_lastStartColor) ||
        !EndColor.Equals(_lastEndColor) ||
        TextureMode != _lastTextureMode ||
        Maths.Abs(TextureTiling - _lastTextureTiling) > 0.001f ||
        !PointsEqual(_lastPoints, Points);

    private void Snapshot()
    {
        _lastPoints.Clear();
        _lastPoints.AddRange(Points);
        _lastStartWidth = StartWidth;
        _lastEndWidth = EndWidth;
        _lastLoop = Loop;
        _lastStartColor = StartColor;
        _lastEndColor = EndColor;
        _lastTextureMode = TextureMode;
        _lastTextureTiling = TextureTiling;
    }

    private static bool PointsEqual(List<Float3> a, List<Float3> b)
    {
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }

        return true;
    }

    public void MarkDirty()
    {
        _isDirty = true;
    }

    // Helper methods for point manipulation
    public void SetPosition(int index, Float3 position)
    {
        if (index >= 0 && index < Points.Count)
        {
            Points[index] = position;
            _isDirty = true;
        }
    }

    public Float3 GetPosition(int index)
    {
        if (index >= 0 && index < Points.Count)
            return Points[index];
        return Float3.Zero;
    }

    public void SetPositions(List<Float3> positions)
    {
        Points = [.. positions];
        _isDirty = true;
    }

    public void SetPositions(Float3[] positions)
    {
        Points = [.. positions];
        _isDirty = true;
    }

    public Material? GetMaterial() => Material;
    public int GetLayer() => GameObject.LayerIndex;

    private int _visualVersion;

    /// <summary>Kept for callers from before the renderer pushed its own changes.</summary>
    public void MarkVisuallyDirty() => _visualVersion++;

    public int VisualVersion => _visualVersion;

    public override void OnDisable()
    {
        if (_mesh.IsValid()) _mesh.Dispose();
        _mesh = null;
    }

    // Two vertices a point, both at the point. The tangent holds the line direction and the signed half width the
    // Line shader pushes each one out by, sideways to the view.
    private void BuildMesh()
    {
        List<Float3> points = new(Points);
        if (Loop && points.Count > 2)
            points.Add(points[0]);

        int segmentCount = points.Count - 1;
        int vertexCount = points.Count * 2;

        Float3[] vertices = new Float3[vertexCount];
        Float4[] tangents = new Float4[vertexCount];
        Float2[] uvs = new Float2[vertexCount];
        Color[] colors = new Color[vertexCount];
        uint[] indices = new uint[segmentCount * 6];

        float totalLength = 0f;
        float[] segmentLengths = new float[segmentCount];
        if (TextureMode == TextureWrapMode.RepeatPerSegment || TextureMode == TextureWrapMode.Tile)
        {
            for (int i = 0; i < segmentCount; i++)
            {
                float length = (float)Float3.Distance(points[i], points[i + 1]);
                segmentLengths[i] = length;
                totalLength += length;
            }
        }

        Float3 min = points[0], max = points[0];
        Float3 lastDirection = new(1, 0, 0);
        float accumulatedLength = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            Float3 point = points[i];
            min = Maths.Min(min, point);
            max = Maths.Max(max, point);

            Float3 along = i == 0 ? points[i + 1] - point
                : i == points.Count - 1 ? point - points[i - 1]
                : points[i + 1] - points[i - 1];
            float alongLength = (float)Float3.Length(along);
            Float3 direction = alongLength > 1e-6f ? along / alongLength : lastDirection;
            lastDirection = direction;

            float t = i / (float)(points.Count - 1);
            float halfWidth = (float)Maths.Lerp(StartWidth, EndWidth, t) * 0.5f;
            float u = CalculateUCoordinate(i, points.Count, accumulatedLength, totalLength);
            Color color = Color.Lerp(StartColor, EndColor, t);

            vertices[i * 2] = point;
            vertices[i * 2 + 1] = point;
            tangents[i * 2] = new Float4(direction, -halfWidth);
            tangents[i * 2 + 1] = new Float4(direction, halfWidth);
            uvs[i * 2] = new Float2(u, 0);
            uvs[i * 2 + 1] = new Float2(u, 1);
            colors[i * 2] = color;
            colors[i * 2 + 1] = color;

            if (i < segmentCount)
                accumulatedLength += segmentLengths[i];
        }

        int triIndex = 0;
        for (int i = 0; i < segmentCount; i++)
        {
            uint baseVertex = (uint)(i * 2);

            indices[triIndex++] = baseVertex;
            indices[triIndex++] = baseVertex + 2;
            indices[triIndex++] = baseVertex + 1;

            indices[triIndex++] = baseVertex + 1;
            indices[triIndex++] = baseVertex + 2;
            indices[triIndex++] = baseVertex + 3;
        }

        if (_mesh.IsNotValid()) _mesh = new Mesh();
        _mesh.Vertices = vertices;
        _mesh.Tangents = tangents;
        _mesh.UV = uvs;
        _mesh.Colors = colors;
        _mesh.Indices = indices;
        _mesh.RecalculateBounds();
        _pointBounds = new AABB(min, max);
    }

    private float CalculateUCoordinate(int index, int totalPoints, float accumulatedLength, float totalLength)
    {
        float t = index / (float)(totalPoints - 1);

        return TextureMode switch
        {
            TextureWrapMode.Stretch => t, // 0 to 1 stretched across entire line

            TextureWrapMode.Tile => totalLength > 0 ? (accumulatedLength / totalLength) * TextureTiling : t,

            TextureWrapMode.RepeatPerSegment => index, // Each segment gets 0 to 1

            _ => t
        };
    }
}

public enum TextureWrapMode
{
    /// <summary>Stretch texture from start to end (0 to 1)</summary>
    Stretch,

    /// <summary>Tile texture based on distance along the line</summary>
    Tile,

    /// <summary>Repeat texture for each point (creates 0,1,2,3... pattern)</summary>
    RepeatPerSegment
}
