// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Interface for all renderable objects in the scene.
/// Supports both single-instance and GPU-instanced rendering through a unified API.
/// </summary>
public interface IRenderable
{
    public Material GetMaterial();
    public int GetLayer();

    /// <summary>
    /// Gets the world-space position of this renderable (typically the transform position).
    /// Used for depth sorting (e.g., back-to-front sorting for transparent objects).
    /// </summary>
    public Float3 GetPosition();

    /// <summary>
    /// Gets the submesh index to draw. -1 means draw the entire index buffer (no submeshes).
    /// </summary>
    public int GetSubMeshIndex() => -1;

    /// <summary>
    /// Gets the rendering data for this renderable.
    /// </summary>
    /// <param name="viewer">Camera viewing data for culling/LOD</param>
    /// <param name="properties">Shader properties (per-object or shared for instances)</param>
    /// <param name="mesh">Mesh to render</param>
    /// <param name="model">Model matrix (only used for single-instance rendering)</param>
    /// <param name="instanceData">Instance data array for GPU instancing, or null for single-instance rendering</param>
    public void GetRenderingData(ViewerData viewer, out PropertySet properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData);

    /// <summary>
    /// World-to-object matrix (the inverse of the model matrix), bound as <c>prowl_WorldToObject</c>
    /// for normal transforms. The default inverts on demand; renderables whose transform is fixed
    /// for the frame should cache the result so it isn't re-inverted once per render pass.
    /// </summary>
    public Float4x4 GetWorldToObjectMatrix(in Float4x4 model) => model.Invert();

    public void GetCullingData(out bool isRenderable, out AABB bounds);
}

public enum LightType
{
    Directional,
    Spot,
    Point,
    //Area
}

/// <summary>
/// Per-frame light parameters surfaced by every <see cref="IRenderableLight"/>.
/// </summary>
public struct ForwardLightData
{
    public LightType Type;
    public Float3 Position;
    public Float3 Direction;
    public Float3 Color;
    public float Intensity;
    public float Range;
    public float SpotAngle;       // degrees
    public float InnerSpotAngle;  // degrees

    // Shadow
    public bool ShadowEnabled;
    public float ShadowBias;
    public float ShadowNormalBias;
    public float ShadowStrength;
    public float ShadowQuality;   // 0 = Hard, 1 = Soft

    // Directional cascade data (only for LightType.Directional)
    public int CascadeCount;
    public Float4x4[] CascadeShadowMatrices; // [4]
    public Float4[] CascadeAtlasParams;      // [4]

    // Point shadow data (6 faces)
    public Float4x4[] PointShadowMatrices; // [6]
    public Float4[] PointShadowFaceParams; // [6]

    // Spot shadow data (1 matrix)
    public Float4x4 SpotShadowMatrix;
    public Float4 SpotShadowAtlasParams;
}

public interface IRenderableLight
{
    public int GetLightID();
    public int GetLayer();
    public LightType GetLightType();
    public Float3 GetLightPosition();
    public Float3 GetLightDirection();
    public bool DoCastShadows();

    /// <summary>
    /// Returns the light's data for forward rendering (position, color, shadow data, etc.)
    /// </summary>
    public ForwardLightData GetForwardLightData();
}

/// <summary>
/// Per-scene registry that every active <see cref="IRenderable"/>/<see cref="IRenderableLight"/> submits
/// itself into each frame (see <see cref="MonoBehaviour.OnRenderCollect"/>). A flat collection only - no
/// frustum/visibility culling happens here yet; the render pipeline reads <see cref="Renderables"/> and
/// <see cref="Lights"/> directly (see <see cref="Scene.Culler"/>).
/// </summary>
public sealed class SceneCuller
{
    private readonly List<IRenderable> _renderables = new();
    private readonly List<IRenderableLight> _lights = new();

    public IReadOnlyList<IRenderable> Renderables => _renderables;
    public IReadOnlyList<IRenderableLight> Lights => _lights;

    public void Add(IRenderable renderable) => _renderables.Add(renderable);
    public void Add(IRenderableLight light) => _lights.Add(light);

    public void Clear()
    {
        _renderables.Clear();
        _lights.Clear();
    }
}
