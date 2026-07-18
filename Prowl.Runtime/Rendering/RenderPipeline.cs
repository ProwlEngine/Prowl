// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite;

using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Resources;
using Prowl.Vector;


namespace Prowl.Runtime.Rendering;


public struct RenderingData
{
    /// <summary>Whether to draw gizmos (editor scene view).</summary>
    public bool DisplayGizmos;

    /// <summary>Whether to draw the editor grid.</summary>
    public bool DisplayGrid;

    /// <summary>Whether the render is happening from the Scene View.</summary>
    public bool IsSceneView;

    public bool SkipUI;
}

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

public abstract class RenderPipeline : EngineObject
{
    private static Shader? s_blitShader;
    private static Material? s_blitMaterial;

    /// <summary>Default material used by <c>cmd.Blit</c> when no material is supplied.
    /// Lazy-loaded on first call.</summary>
    public static Material GetBlitMaterial()
    {
        if (s_blitShader.IsNotValid())
            s_blitShader = Shader.LoadDefault(DefaultShader.Blit);
        if (s_blitMaterial.IsNotValid())
            s_blitMaterial = new Material(s_blitShader);
        return s_blitMaterial!;
    }

    public struct CameraSnapshot(Camera camera)
    {
        public Scene Scene = camera.Scene;

        public Float3 CameraPosition = camera.Transform.Position;
        public Float3 CameraRight = camera.Transform.Right;
        public Float3 CameraUp = camera.Transform.Up;
        public Float3 CameraForward = camera.Transform.Forward;
        public LayerMask CullingMask = camera.CullingMask;
        public CameraClearFlags ClearFlags = camera.ClearFlags;
        public float NearClipPlane = camera.NearClipPlane;
        public float FarClipPlane = camera.FarClipPlane;
        public uint PixelWidth = camera.PixelWidth;
        public uint PixelHeight = camera.PixelHeight;
        public float Aspect = camera.Aspect;
        public Float4x4 View = camera.ViewMatrix;
        public Float4x4 ViewInverse = camera.ViewMatrix.Invert();
        public Float4x4 Projection = camera.ProjectionMatrix;
        public Float4x4 NonJitteredProjection = camera.NonJitteredProjectionMatrix;
        public Float4x4 PreviousViewProj = camera.PreviousViewProjectionMatrix;
        public bool HasPreviousViewProj = camera.HasPreviousViewProjectionMatrix;
        public Frustum WorldFrustum = Frustum.FromMatrix(camera.ProjectionMatrix * camera.ViewMatrix);
    }

    /// <summary>
    /// Collects the renderables and lights the given camera can see from its scene. The lists are
    /// filled by every active component's render-collect callback; the pipeline then culls/sorts them.
    /// </summary>
    public static (List<IRenderable> renderables, List<IRenderableLight> lights) CollectRenderables(Scene scene, Camera camera)
    {
        var renderables = new List<IRenderable>();
        var lights = new List<IRenderableLight>();
        scene.CollectRenderables(camera, renderables, lights);
        return (renderables, lights);
    }

    /// <summary>
    /// Writes the standard camera matrices and world-space camera position into a globals property set
    /// under the names the default shaders expect (<c>prowl_MatV/P/VP/IV/IP/IVP</c>,
    /// <c>prowl_MatVP_NonJittered</c>, <c>prowl_PrevViewProj</c>, <c>_WorldSpaceCameraPos</c>).
    /// </summary>
    public static void PopulateCameraGlobals(PropertySet globals, in CameraSnapshot css)
    {
        Float4x4 view = css.View;
        Float4x4 proj = css.Projection;
        Float4x4 viewProj = proj * view;

        globals.SetMatrix("prowl_MatV", view);
        globals.SetMatrix("prowl_MatP", proj);
        globals.SetMatrix("prowl_MatVP", viewProj);
        globals.SetMatrix("prowl_MatIV", css.ViewInverse);
        globals.SetMatrix("prowl_MatIP", proj.Invert());
        globals.SetMatrix("prowl_MatIVP", viewProj.Invert());
        globals.SetMatrix("prowl_MatVP_NonJittered", css.NonJitteredProjection * view);
        globals.SetMatrix("prowl_PrevViewProj", css.HasPreviousViewProj ? css.PreviousViewProj : css.NonJitteredProjection * view);
        globals.SetFloat3("_WorldSpaceCameraPos", css.CameraPosition);
    }

    public virtual void Render(Camera camera, in RenderingData data)
    {
    }
}
