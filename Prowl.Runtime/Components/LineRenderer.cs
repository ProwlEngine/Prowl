// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

public class LineRenderer : MonoBehaviour, IRenderable
{
    public Material Material;
    public float StartWidth = 0.1f;
    public float EndWidth = 0.1f;
    public List<Float3> Points = [];
    public bool Loop = false;
    public Color StartColor = Color.White;
    public Color EndColor = Color.White;
    public TextureWrapMode TextureMode = TextureWrapMode.Stretch;
    public float TextureTiling = 1.0f; // Controls UV tiling for Tile mode
    public bool RecalculateNormals = false;

    private Mesh? _cachedMesh;
    private bool _isDirty = true;
    private AABB _bounds;

    // Cached state for change detection
    private List<Float3> _lastPoints;
    private float _lastStartWidth;
    private float _lastEndWidth;
    private bool _lastLoop;
    private Color _lastStartColor;
    private Color _lastEndColor;
    private TextureWrapMode _lastTextureMode;
    private float _lastTextureTiling;

    public override void OnEnable()
    {
        _lastPoints = [];
        _lastStartColor = StartColor;
        _lastEndColor = EndColor;
        _lastStartWidth = StartWidth;
        _lastEndWidth = EndWidth;
        _lastTextureMode = TextureMode;
        _lastTextureTiling = TextureTiling;
    }

    public override void Update()
    {
        if (Material.IsValid() && Points != null && Points.Count >= 2)
        {
            // Check if we need to regenerate
            bool needsUpdate = _isDirty ||
                               _cachedMesh.IsNotValid() ||
                               StartWidth != _lastStartWidth ||
                               EndWidth != _lastEndWidth ||
                               Loop != _lastLoop ||
                               !StartColor.Equals(_lastStartColor) ||
                               !EndColor.Equals(_lastEndColor) ||
                               TextureMode != _lastTextureMode ||
                               Maths.Abs(TextureTiling - _lastTextureTiling) > 0.001f ||
                               !PointsEqual(_lastPoints, Points);

            if (needsUpdate)
            {
                // Update cached state
                _lastPoints = [.. Points];
                _lastStartWidth = StartWidth;
                _lastEndWidth = EndWidth;
                _lastLoop = Loop;
                _lastStartColor = StartColor;
                _lastEndColor = EndColor;
                _lastTextureMode = TextureMode;
                _lastTextureTiling = TextureTiling;
                _isDirty = true; // Flag for next render

                CalculateBounds();
            }

            // Always push the renderable (IRenderable interface handles actual rendering)
            GameObject.Scene.PushRenderable(this);
        }
    }

    private bool PointsEqual(List<Float3> a, List<Float3> b)
    {
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }

        return true;
    }

    private void CalculateBounds()
    {
        if (Points == null || Points.Count == 0)
        {
            _bounds = new AABB();
            return;
        }

        // Transform points to world space for bounds calculation
        Float3 min = Transform.TransformPoint(Points[0]);
        Float3 max = min;

        foreach (Float3 point in Points)
        {
            Float3 worldPoint = Transform.TransformPoint(point);
            min = Maths.Min(min, worldPoint);
            max = Maths.Max(max, worldPoint);
        }

        // Expand bounds by maximum line width
        float maxWidth = Maths.Max(StartWidth, EndWidth);
        Float3 expansion = new(maxWidth, maxWidth, maxWidth);
        min -= expansion;
        max += expansion;

        _bounds = new AABB(min, max);
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

    public override void OnDisable()
    {
        // Clean up the mesh when disabled
        _cachedMesh?.OnDispose();
        _cachedMesh = null;
    }

    #region IRenderable Implementation

    public Material GetMaterial() => Material;
    public int GetLayer() => GameObject.LayerIndex;
    Float3 IRenderable.GetPosition() => Transform.Position;

    public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh drawData, out Float4x4 model, out InstanceData[]? instanceData)
    {
        // Create mesh only once
        if (_cachedMesh.IsNotValid())
        {
            _cachedMesh = new Mesh();
        }

        // Always regenerate for smooth billboarding (billboard lines always face camera)
        UpdateBillboardedMesh(_cachedMesh, viewer);

        _isDirty = false;

        // Setup properties
        properties = new PropertyState();
        properties.SetInt("_ObjectID", InstanceID);
        properties.SetColor("_StartColor", StartColor);
        properties.SetColor("_EndColor", EndColor);

        drawData = _cachedMesh!;
        model = Float4x4.Identity; // Vertices are already in world space
        instanceData = null; // Single instance rendering
    }

    public void GetCullingData(out bool isRenderable, out AABB bounds)
    {
        isRenderable = Points != null && Points.Count >= 2 && Material.IsValid();
        bounds = _bounds;
    }

    #endregion

    private void UpdateBillboardedMesh(Mesh mesh, ViewerData viewer)
    {
        if (Points.Count < 2)
            return;

        // Transform points to world space
        List<Float3> worldPoints = new(Points.Count);
        foreach (Float3 point in Points)
        {
            worldPoints.Add(Transform.TransformPoint(point));
        }

        // Add loop point if needed
        if (Loop && worldPoints.Count > 2)
        {
            worldPoints.Add(worldPoints[0]);
        }

        int segmentCount = worldPoints.Count - 1;
        int vertexCount = worldPoints.Count * 2;
        int triangleCount = segmentCount * 2;

        Float3[] vertices = new Float3[vertexCount];
        Float2[] uvs = new Float2[vertexCount];
        Color[] colors = new Color[vertexCount];
        uint[] indices = new uint[triangleCount * 3];

        // Calculate total line length for distance-based UV mapping
        float totalLength = 0f;
        float[] segmentLengths = new float[segmentCount];

        if (TextureMode == TextureWrapMode.RepeatPerSegment || TextureMode == TextureWrapMode.Tile)
        {
            for (int i = 0; i < segmentCount; i++)
            {
                float length = (float)Float3.Distance(worldPoints[i], worldPoints[i + 1]);
                segmentLengths[i] = length;
                totalLength += length;
            }
        }

        // Generate vertices
        float accumulatedLength = 0f;

        for (int i = 0; i < worldPoints.Count; i++)
        {
            Float3 point = worldPoints[i];

            // Calculate line direction
            Float3 lineDir;
            if (i == 0)
            {
                lineDir = Float3.Normalize(worldPoints[i + 1] - point);
            }
            else if (i == worldPoints.Count - 1)
            {
                lineDir = Float3.Normalize(point - worldPoints[i - 1]);
            }
            else
            {
                lineDir = Float3.Normalize((worldPoints[i + 1] - worldPoints[i - 1]) * 0.5f);
            }

            // Calculate perpendicular vector (billboard direction towards camera)
            Float3 toCamera = Float3.Normalize(viewer.Position - point);
            Float3 right = Float3.Normalize(Float3.Cross(toCamera, lineDir));

            // If cross product is near zero (line points at camera), use camera up vector
            if (Float3.LengthSquared(right) < 0.001)
            {
                right = Float3.Normalize(Float3.Cross(viewer.Up, lineDir));
            }

            // Interpolate width along the line
            float t = i / (float)(worldPoints.Count - 1);
            float width = (float)Maths.Lerp(StartWidth, EndWidth, t);
            float halfWidth = width * 0.5f;

            // Create offset vertices
            Float3 offset = right * halfWidth;

            vertices[i * 2] = (Float3)(point - offset);
            vertices[i * 2 + 1] = (Float3)(point + offset);

            // Calculate U coordinate based on texture mode
            float u = CalculateUCoordinate(i, worldPoints.Count, accumulatedLength, totalLength);

            uvs[i * 2] = new Float2(u, 0);
            uvs[i * 2 + 1] = new Float2(u, 1);

            // Update accumulated length for next iteration
            if (i < segmentCount)
            {
                accumulatedLength += segmentLengths[i];
            }

            // Colors (interpolate from start to end)
            Color color = Maths.Lerp(StartColor, EndColor, t);
            colors[i * 2] = color;
            colors[i * 2 + 1] = color;
        }

        // Generate triangles
        int triIndex = 0;
        for (int i = 0; i < segmentCount; i++)
        {
            uint baseVertex = (uint)(i * 2);

            // First triangle
            indices[triIndex++] = baseVertex;
            indices[triIndex++] = baseVertex + 2;
            indices[triIndex++] = baseVertex + 1;

            // Second triangle
            indices[triIndex++] = baseVertex + 1;
            indices[triIndex++] = baseVertex + 2;
            indices[triIndex++] = baseVertex + 3;
        }

        // Update the existing mesh instead of creating a new one
        mesh.Vertices = vertices;
        mesh.UV = uvs;
        mesh.Colors = colors;
        mesh.Indices = indices;

        if (RecalculateNormals)
        {
            mesh.RecalculateNormals();
        }

        mesh.RecalculateBounds();
    }

    private float CalculateUCoordinate(int index, int totalPoints, float accumulatedLength, float totalLength)
    {
        float t = index / (float)(totalPoints - 1);

        return TextureMode switch
        {
            TextureWrapMode.Stretch => t, // 0 to 1 stretched across entire line

            TextureWrapMode.Tile => totalLength > 0 ? (accumulatedLength / totalLength) * (float)TextureTiling : t, // Repeat based on world distance

            TextureWrapMode.RepeatPerSegment => index, // Each segment gets 0 to 1

            _ => t
        };
    }
}

public enum TextureWrapMode
{
    /// <summary>Stretch texture from start to end (0 to 1)</summary>
    Stretch,

    /// <summary>Tile texture based on world-space distance</summary>
    Tile,

    /// <summary>Repeat texture for each point (creates 0,1,2,3... pattern)</summary>
    RepeatPerSegment
}
