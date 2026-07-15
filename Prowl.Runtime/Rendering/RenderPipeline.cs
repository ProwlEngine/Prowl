// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Rendering.Shaders;
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
    public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData);

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
/// Per-frame light parameters surfaced by every <see cref="IRenderableLight"/>. Consumed by
/// <see cref="SceneLightSystem"/>, packed into the BVH leaf textures, and (for the directional
/// light + closest-N shadow casters) uploaded as conventional uniforms.
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

    public HashSet<int> ActiveObjectIds { get => s_activeObjectIds; set => s_activeObjectIds = value; }

    private Dictionary<int, Float4x4> s_prevModelMatrices = [];
    private HashSet<int> s_activeObjectIds = [];
    private const int CLEANUP_INTERVAL_FRAMES = 120; // Clean up every 120 frames
    private int s_framesSinceLastCleanup = 0;

    private void CleanupUnusedModelMatrices()
    {
        // Increment frame counter
        s_framesSinceLastCleanup++;

        // Only perform cleanup at specified interval
        if (s_framesSinceLastCleanup < CLEANUP_INTERVAL_FRAMES)
            return;

        s_framesSinceLastCleanup = 0;

        // Remove all matrices that weren't used in this frame
        var unusedKeys = s_prevModelMatrices.Keys
            .Where(key => !ActiveObjectIds.Contains(key))
            .ToList();

        foreach (int key in unusedKeys)
            s_prevModelMatrices.Remove(key);

        // Clear the active IDs set for next frame
        ActiveObjectIds.Clear();
    }

    /// <summary>
    /// Tracks an object's model matrix for motion vector computation.
    /// Returns the previous frame's model matrix (or current if first frame).
    /// </summary>
    private Float4x4 TrackModelMatrix(int objectId, Float4x4 currentModel)
    {
        // Mark this object ID as active this frame
        ActiveObjectIds.Add(objectId);

        Float4x4 prevModel;
        if (!s_prevModelMatrices.TryGetValue(objectId, out prevModel))
            prevModel = currentModel; // First frame, use current matrix

        s_prevModelMatrices[objectId] = currentModel;
        return prevModel;
    }

    /// <summary>
    /// Collects renderables and lights from the scene for the given camera.
    /// Components receive camera info for LOD/culling decisions.
    /// </summary>
    public static (List<IRenderable> renderables, List<IRenderableLight> lights) CollectRenderables(Scene scene, Camera camera)
    {
        var renderables = new List<IRenderable>();
        var lights = new List<IRenderableLight>();
        scene.CollectRenderables(camera, renderables, lights);
        return (renderables, lights);
    }

    public virtual void Render(Camera camera, in RenderingData data)
    {
        // Clean up unused matrices after rendering
        CleanupUnusedModelMatrices();
    }

    /// <summary>
    /// Returns a per-index "culled" mask (true == culled, skip this renderable) aligned to
    /// <paramref name="renderables"/>. A bool[] keyed by index is cheaper to allocate than a
    /// HashSet and turns the per-object membership test in every pass into an O(1) array read
    /// instead of a hash lookup.
    /// </summary>
    public bool[] CullRenderables(IReadOnlyList<IRenderable> renderables, Frustum? worldFrustum, LayerMask cullingMask)
    {
        EnsureWorldBounds(renderables);

        bool[] culledRenderableIndices = new bool[renderables.Count];
        int culled = 0;
        for (int renderIndex = 0; renderIndex < renderables.Count; renderIndex++)
        {
            bool frustumCull = worldFrustum != null
                && (!_boundsRenderable[renderIndex] || !worldFrustum.Value.Intersects(_worldBounds[renderIndex]));

            if (frustumCull || cullingMask.HasLayer(renderables[renderIndex].GetLayer()) == false)
            {
                culledRenderableIndices[renderIndex] = true;
                culled++;
            }
        }

        int collected = renderables.Count;
        RenderStats.AddRenderables(collected, culled, collected - culled);

        return culledRenderableIndices;
    }

    public bool CullRenderable(IRenderable renderable, Frustum cameraFrustum)
    {
        renderable.GetCullingData(out bool isRenderable, out AABB bounds);

        return !isRenderable || !cameraFrustum.Intersects(bounds);
    }

    public enum SortMode
    {
        FrontToBack,
        BackToFront
    }

    /// <summary>
    /// Sorts renderables by distance from camera and returns a new sorted list.
    /// FrontToBack: Nearest objects first (optimal for opaque objects - early Z rejection)
    /// BackToFront: Farthest objects first (required for transparent objects - correct alpha blending)
    /// </summary>
    // Cached comparators so the per-comparison branch and the per-sort delegate allocation are
    // paid once at startup instead of every SortRenderables call.
    private static readonly Comparison<(IRenderable renderable, float distSq)> s_frontToBack =
        (a, b) => a.distSq.CompareTo(b.distSq);
    private static readonly Comparison<(IRenderable renderable, float distSq)> s_backToFront =
        (a, b) => b.distSq.CompareTo(a.distSq);

    // Reused across frames; only one sort is in flight at a time (sequential encode).
    private readonly List<(IRenderable renderable, float distSq)> _sortPairs = new();
    private readonly List<IRenderable> _sortResult = new();

    // DrawRenderables scratch, reused across passes (encode is sequential, never re-entrant).
    private readonly List<RenderBatch> _batches = new();
    private readonly Dictionary<(ulong, int, Mesh), int> _batchLookup = new();
    private readonly List<List<int>> _indexListPool = new();
    private int _indexListRented;

    // Per-frame world-space AABB cache shared by the main cull and every shadow cascade cull, so each
    // renderable's bounds are transformed once per frame instead of once per frustum (main + 4 cascades).
    private AABB[] _worldBounds = System.Array.Empty<AABB>();
    private bool[] _boundsRenderable = System.Array.Empty<bool>();
    private IReadOnlyList<IRenderable> _boundsFrameList;
    private int _boundsCount;

    /// <summary>
    /// Computes (or returns the cached) per-renderable world-space bounds for this frame's list. Keyed
    /// on list identity + count: the first cull of the frame builds it, later culls reuse it. Renderables
    /// don't move between collection and drawing, so caching for the frame is safe.
    /// </summary>
    public void EnsureWorldBounds(IReadOnlyList<IRenderable> renderables)
    {
        int count = renderables.Count;
        if (ReferenceEquals(_boundsFrameList, renderables) && _boundsCount == count)
            return;

        if (_worldBounds.Length < count)
        {
            _worldBounds = new AABB[count];
            _boundsRenderable = new bool[count];
        }
        for (int i = 0; i < count; i++)
        {
            renderables[i].GetCullingData(out bool isRenderable, out AABB bounds);
            _boundsRenderable[i] = isRenderable;
            _worldBounds[i] = bounds;
        }
        _boundsFrameList = renderables;
        _boundsCount = count;
    }

    // Rent a cleared per-batch index list from the pool, growing it only when a frame needs
    // more distinct batches than any previous frame.
    private List<int> RentIndexList()
    {
        List<int> list;
        if (_indexListRented < _indexListPool.Count)
        {
            list = _indexListPool[_indexListRented];
            list.Clear();
        }
        else
        {
            list = new List<int>();
            _indexListPool.Add(list);
        }
        _indexListRented++;
        return list;
    }

    public List<IRenderable> SortRenderables(IReadOnlyList<IRenderable> renderables, bool[] culledRenderableIndices, Float3 cameraPosition, SortMode mode)
    {
        _sortResult.Clear();
        int count = renderables?.Count ?? 0;
        if (count == 0)
            return _sortResult;

        _sortPairs.Clear();

        // Collect only non-culled renderables
        for (int i = 0; i < count; i++)
        {
            if (culledRenderableIndices != null && culledRenderableIndices[i])
                continue;

            var renderable = renderables[i];
            float distSq = Float3.DistanceSquared(renderable.GetPosition(), cameraPosition);
            _sortPairs.Add((renderable, distSq));
        }

        // Sort by distance squared (avoid sqrt)
        _sortPairs.Sort(mode == SortMode.BackToFront ? s_backToFront : s_frontToBack);

        for (int i = 0; i < _sortPairs.Count; i++)
            _sortResult.Add(_sortPairs[i].renderable);

        return _sortResult;
    }

    public void SetupGlobalUniforms(CameraSnapshot css)
    {
        // Set View Rect
        //buffer.SetViewports((int)(camera.Viewrect.x * target.Width), (int)(camera.Viewrect.y * target.Height), (int)(camera.Viewrect.width * target.Width), (int)(camera.Viewrect.height * target.Height), 0, 1000);

        // Previous-frame VP for motion vectors (jitter-free). Fall back to the current
        // non-jittered VP on the first frame so motion reads zero instead of garbage.
        GlobalUniforms.SetPrevViewProj(css.HasPreviousViewProj
            ? css.PreviousViewProj
            : css.NonJitteredProjection * css.View);

        // Current-frame VP without TAA jitter, so the prepass can compute motion vectors
        // jitter-free while still rasterizing with the jittered projection.
        GlobalUniforms.SetMatrixVPNonJittered(css.NonJitteredProjection * css.View);

        // Setup Default Uniforms for this frame
        // Camera
        GlobalUniforms.SetWorldSpaceCameraPos(css.CameraPosition);
        GlobalUniforms.SetProjectionParams(new Float4(1.0f, css.NearClipPlane, css.FarClipPlane, 1.0f / css.FarClipPlane));
        GlobalUniforms.SetScreenParams(new Float4(css.PixelWidth, css.PixelHeight, 1.0f + 1.0f / css.PixelWidth, 1.0f + 1.0f / css.PixelHeight));

        // Time
        GlobalUniforms.SetTime(new Float4(Time.TimeSinceStartup * 0.5f, Time.TimeSinceStartup, Time.TimeSinceStartup * 2, Time.FrameCount));
        GlobalUniforms.SetSinTime(new Float4(Maths.Sin(Time.TimeSinceStartup / 8), Maths.Sin(Time.TimeSinceStartup / 4), Maths.Sin(Time.TimeSinceStartup / 2), Maths.Sin(Time.TimeSinceStartup)));
        GlobalUniforms.SetCosTime(new Float4(Maths.Cos(Time.TimeSinceStartup / 8), Maths.Cos(Time.TimeSinceStartup / 4), Maths.Cos(Time.TimeSinceStartup / 2), Maths.Cos(Time.TimeSinceStartup)));
        GlobalUniforms.SetDeltaTime(new Float4(Time.DeltaTime, 1.0f / Time.DeltaTime, Time.SmoothDeltaTime, 1.0f / Time.SmoothDeltaTime));

        // Upload the global uniform buffer
        GlobalUniforms.Upload();
    }

    public void AssignCameraMatrices(Float4x4 view, Float4x4 projection)
    {
        Float4x4 viewProj = projection * view;

        GlobalUniforms.SetMatrixV(view);
        GlobalUniforms.SetMatrixIV(view.Invert());
        GlobalUniforms.SetMatrixP(projection);
        GlobalUniforms.SetMatrixVP(viewProj);

        // Precompute the inverse projection / view-projection so fragment shaders that
        // reconstruct view/world position from depth (GTAO, SSR, fog, shadow reprojection)
        // read them from the UBO instead of running a 4x4 inverse() per fragment.
        GlobalUniforms.SetMatrixIP(projection.Invert());
        GlobalUniforms.SetMatrixIVP(viewProj.Invert());

        // Upload the global uniform buffer
        GlobalUniforms.Upload();
    }

    // DrawMeshNow and the static Blit overloads were removed in the CommandBuffer
    // migration. Use cmd.DrawMesh / cmd.Blit on a rented CommandBuffer instead.

    /// <summary>
    /// Represents a render batch: a group of objects sharing the same material, mesh, and shader pass.
    /// Batching reduces GPU state changes by binding material uniforms once for all objects in the batch.
    /// </summary>
    private struct RenderBatch
    {
        public Material Material;      // Shared material for all objects in this batch
        public Mesh Mesh;              // Shared mesh for all objects in this batch
        public int PassIndex;          // Shader pass index
        public ulong MaterialHash;     // Hash of material uniforms (for sorting/grouping)
        public int SortKey;            // Sort order based on tag value + offset
        public List<int> RenderableIndices;  // Indices of objects in this batch
        public bool IsInstanced;       // True if this batch uses GPU instancing
        public int InstancedRenderableIndex;  // Index of the instanced renderable (if IsInstanced is true)
    }

    /// <summary>
    /// Renders all given objects with optimized batching. Objects are grouped by (material, mesh, pass)
    /// to minimize GPU state changes. This achieves:
    /// - Material uniforms bound once per batch (instead of per object)
    /// - Mesh data uploaded once per batch
    /// - Shader variant selected once per batch
    /// - Per-object uniforms still bound individually
    ///
    /// Performance: 100 objects with same material = 1 material bind (vs 100 without batching)
    /// </summary>
    /// <param name="cmd">CommandBuffer the batches encode into. The caller is responsible
    /// for binding the target framebuffer + viewport before calling this method, and
    /// for submitting the buffer after.</param>
    /// <param name="currentRT">Currently bound color render target, used for the
    /// GrabTexture handshake (read FB for the blit-into-grab-RT). Pass null if no
    /// pass in this batch will request a grab texture.</param>
    public void DrawRenderables(CommandBuffer cmd, IReadOnlyList<IRenderable> renderables, string shaderTag, string tagValue, ViewerData viewer, bool[] culledRenderableIndices, bool updatePreviousMatrices, RenderTexture? currentRT = null)
    {
        bool hasRenderOrder = !string.IsNullOrWhiteSpace(shaderTag);
        bool hasSortOffsets = false;

        // The depth-only shadow-caster pass never samples the inverse model matrix, so skip inverting
        // and encoding prowl_WorldToObject per object per cascade.
        bool needsWorldToObject = tagValue != "ShadowCaster";

        // ========== PHASE 1: Build Batches ==========
        // Group renderables by (material hash, shader pass, mesh). These two containers plus the
        // per-batch index lists are reused across calls (encode is sequential, never re-entrant)
        // so a multi-pass frame doesn't allocate a fresh dictionary + lists for every pass.
        List<RenderBatch> batches = _batches;
        Dictionary<(ulong, int, Mesh), int> batchLookup = _batchLookup;
        batches.Clear();
        batchLookup.Clear();
        _indexListRented = 0;

        for (int renderIndex = 0; renderIndex < renderables.Count; renderIndex++)
        {
            // Skip culled objects
            if (culledRenderableIndices != null && culledRenderableIndices[renderIndex])
                continue;

            IRenderable renderable = renderables[renderIndex];

            Material material = renderable.GetMaterial();
            // Skip until the material AND its shader have streamed in (async loading). Accessing
            // material.Shader (=_shader.Res) queues the shader load; the object pops in once ready.
            if (material == null || material.Shader.IsNotValid()) continue;

            // Get rendering data to determine if this is instanced or single-instance rendering
            renderable.GetRenderingData(viewer, out PropertyState _, out Mesh mesh, out Float4x4 _, out InstanceData[]? instanceData);
            if (mesh == null || mesh.VertexCount <= 0) continue;

            // Handle instanced renderables - add to batches with proper sorting (instanceData != null)
            if (instanceData != null && instanceData.Length > 0)
            {
                // Get material hash for batching
                ulong instancedMaterialHash = material.GetStateHash();

                // Find ALL shader passes matching the requested tag and add to batches
                int instancedPassIndex = -1;
                foreach (ShaderPass pass in material.Shader.Passes)
                {
                    instancedPassIndex++;

                    if (hasRenderOrder && !pass.HasTag(shaderTag, tagValue))
                        continue;

                    // Compute sort key for this pass (same as non-instanced)
                    int sortKey = hasRenderOrder ? instancedPassIndex + pass.GetTagSortOffset(shaderTag) : instancedPassIndex;
                    hasSortOffsets |= sortKey != instancedPassIndex;

                    // Create batch for instanced renderable
                    // Each instanced renderable gets its own batch since it draws all instances in one call
                    RenderBatch newBatch = new()
                    {
                        Material = material,
                        Mesh = mesh,
                        PassIndex = instancedPassIndex,
                        MaterialHash = instancedMaterialHash,
                        SortKey = sortKey,
                        IsInstanced = true,
                        InstancedRenderableIndex = renderIndex,
                        RenderableIndices = null  // Not used for instanced batches
                    };
                    batches.Add(newBatch);
                }
                continue;
            }

            // Get material hash for batching - materials with identical uniforms will batch together
            ulong materialHash = material.GetStateHash();

            // Find ALL shader passes matching the requested tag (e.g., "Opaque", "Transparent", "ShadowCaster")
            // Multi-pass rendering: materials can have multiple passes with the same tag (e.g., terrain with many texture layers)
            int passIndex = -1;
            foreach (ShaderPass pass in material.Shader.Passes)
            {
                passIndex++;

                if (hasRenderOrder && !pass.HasTag(shaderTag, tagValue))
                    continue;


                // Found matching pass - add to appropriate batch
                // Batch key: (material hash, pass index, mesh) ensures each pass gets its own batch
                var batchKey = (materialHash, passIndex, mesh);
                if (batchLookup.TryGetValue(batchKey, out int batchIndex))
                {
                    // Batch already exists - add this object to it
                    batches[batchIndex].RenderableIndices.Add(renderIndex);
                }
                else
                {
                    // Compute sort key for this pass
                    int sortKey = hasRenderOrder ? passIndex + pass.GetTagSortOffset(shaderTag) : passIndex;
                    hasSortOffsets |= sortKey != passIndex;

                    // Create new batch for this unique material+pass+mesh combination
                    List<int> indices = RentIndexList();
                    indices.Add(renderIndex);
                    RenderBatch newBatch = new()
                    {
                        Material = material,
                        Mesh = mesh,
                        PassIndex = passIndex,
                        MaterialHash = materialHash,
                        SortKey = sortKey,
                        RenderableIndices = indices
                    };
                    batchLookup[batchKey] = batches.Count;
                    batches.Add(newBatch);
                }

                // Continue to next pass - materials can have multiple passes with the same tag
                // They will execute in order they appear in the shader file (Pass 0 → Pass 1 → Pass 2, etc.)
            }
        }

        // Sort batches by their sort key (respects tag offsets like "Transparent+1000")
        if (hasSortOffsets)
        {
            batches.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
        }

        for (int i = 0; i < batches.Count; i++)
            RenderStats.AddBatch();

        // ========== PHASE 2: Draw Batches ==========
        // For each batch, bind state once then draw all objects in that batch
        foreach (RenderBatch batch in batches)
        {
            // Handle instanced batches separately
            if (batch.IsInstanced)
            {
                IRenderable instancedRenderable = renderables[batch.InstancedRenderableIndex];
                DrawInstancedRenderablePass(cmd, instancedRenderable, batch.Material, batch.Mesh, batch.PassIndex, viewer);
                continue;
            }

            Material material = batch.Material;
            Mesh mesh = batch.Mesh;
            int passIndex = batch.PassIndex;
            RenderTexture grabRT = null;

            // Configure shader keywords based on mesh attributes. Once per batch since
            // all objects in this batch share the same mesh.
            material.SetKeyword("HAS_NORMALS", mesh.HasNormals);
            material.SetKeyword("HAS_TANGENTS", mesh.HasTangents);
            material.SetKeyword("HAS_UV", mesh.HasUV);
            material.SetKeyword("HAS_UV2", mesh.HasUV2);
            material.SetKeyword("HAS_COLORS", mesh.HasColors || mesh.HasColors32);
            material.SetKeyword("HAS_BONEINDICES", mesh.HasBoneIndices);
            material.SetKeyword("HAS_BONEWEIGHTS", mesh.HasBoneWeights);
            material.SetKeyword("SKINNED", mesh.HasBoneIndices && mesh.HasBoneWeights);
            material.SetKeyword("BLENDSHAPES", mesh.HasBlendShapes);

            ShaderPass pass = material.Shader.GetPass(passIndex);
            if (!pass.TryGetVariantProgram(material._localKeywords, out GraphicsProgram? variantNullable) || variantNullable == null)
                continue;

            GraphicsProgram variant = variantNullable;

            // GrabTexture: blit current framebuffer into a temporary RT and expose it
            // as a global texture for the shader to sample. Uses the CB's read/draw
            // FB split so the blit happens in the correct order relative to the draws.
            if (pass.HasGrabTexture && currentRT != null && currentRT.IsValid())
            {
                int fbWidth = currentRT.Width;
                int fbHeight = currentRT.Height;
                bool wantDepth = pass.HasGrabDepth;
                // Match the source format instead of assuming Color4b, otherwise an HDR
                // scene color (Short4) is truncated to 8bpc on the way into the grab.
                grabRT = RenderTexture.GetTemporaryRT(fbWidth, fbHeight, wantDepth, [currentRT.MainTexture.ImageFormat]);

                cmd.SetRenderTargets(grabRT.frameBuffer, currentRT.frameBuffer);
                cmd.BlitFramebuffer(0, 0, fbWidth, fbHeight, 0, 0, fbWidth, fbHeight, ClearFlags.Color, BlitFilter.Nearest);
                if (wantDepth)
                    cmd.BlitFramebuffer(0, 0, fbWidth, fbHeight, 0, 0, fbWidth, fbHeight, ClearFlags.Depth, BlitFilter.Nearest);

                // Restore both targets back to the original RT.
                cmd.SetRenderTarget(currentRT.frameBuffer);

                cmd.GenerateMipmap(grabRT.MainTexture);
                // Filter is a sticky texture property; setting it directly is fine and
                // doesn't need to go through the CB (no ordering constraint vs draws).
                grabRT.MainTexture.SetTextureFilters(TextureMin.LinearMipmapLinear, TextureMag.Linear);

                // Encode the global set as a CB opcode so it's ordered against the
                // draws below at EXECUTE time. Writing PropertyState.SetGlobalTexture
                // directly here would set the static at encode time, but by the time
                // mainCmd actually runs we've already encoded the matching clear
                // below and the executor would see an empty global slot.
                cmd.SetGlobalTexture(pass.GrabTextureName, grabRT.MainTexture);
                if (wantDepth && grabRT.InternalDepth != null)
                    cmd.SetGlobalTexture(pass.GrabDepthTextureName, grabRT.InternalDepth);
            }

            // Bind state for the batch. Globals UBO + material uniforms (with shader
            // defaults filled in) apply once; per-object only sets instance uniforms
            // and transforms.
            cmd.SetShader(variant);
            cmd.SetRasterState(pass.State);

            // GlobalUniforms UBO is bound automatically by the executor's PrepareDraw
            // for every draw no explicit cmd.SetBuffer needed here.

            cmd.SetMaterialProperties(material);

            mesh.Upload();

            // ========== PHASE 3: Draw Objects in Batch ==========
            foreach (int renderIndex in batch.RenderableIndices)
            {
                IRenderable renderable = renderables[renderIndex];

                renderable.GetRenderingData(viewer, out PropertyState properties, out Mesh _, out Float4x4 model, out InstanceData[]? _);

                int instanceId = properties.GetInt("_ObjectID");
                Float4x4 prevModel = model;
                if (updatePreviousMatrices && instanceId != 0)
                    prevModel = TrackModelMatrix(instanceId, model);

                cmd.SetInstanceProperties(properties);

                // Per-object transform uniforms. Encoded after SetInstanceProperties so
                // they apply last and can't be clobbered by an instance property of the
                // same name (matches today's order).
                cmd.SetMatrix("prowl_ObjectToWorld", in model);
                if (needsWorldToObject)
                {
                    Float4x4 inv = renderable.GetWorldToObjectMatrix(in model);
                    cmd.SetMatrix("prowl_WorldToObject", in inv);
                }
                if (updatePreviousMatrices)
                    cmd.SetMatrix("prowl_PrevObjectToWorld", in prevModel);

                int subIdx = renderable.GetSubMeshIndex();
                bool i32 = mesh.IndexFormat == IndexFormat.UInt32;
                if (subIdx >= 0 && subIdx < mesh.SubMeshCount)
                {
                    var sub = mesh.GetSubMesh(subIdx);
                    cmd.DrawIndexed(mesh.VertexArrayObject, sub.Topology, (uint)sub.IndexCount, (uint)sub.IndexStart, 0, i32);
                }
                else
                {
                    cmd.DrawIndexed(mesh.VertexArrayObject, mesh.MeshTopology, (uint)mesh.IndexCount, 0, 0, i32);
                }
            }

            if (grabRT != null)
            {
                // Mirror the encode-time set above with an encode-time clear so the
                // grab texture is exposed ONLY for this batch's draws inside the CB.
                cmd.ClearGlobalTexture(pass.GrabTextureName);
                if (pass.HasGrabDepth)
                    cmd.ClearGlobalTexture(pass.GrabDepthTextureName);
                RenderTexture.ReleaseTemporaryRT(grabRT);
                grabRT = null;
            }
        }
    }

    /// <summary>
    /// Draws a specific pass of an instanced renderable with GPU instancing.
    /// Uses DrawIndexedInstanced to draw multiple instances in a single draw call.
    /// The mesh's cached VAO system is used for optimal performance.
    /// </summary>
    private void DrawInstancedRenderablePass(CommandBuffer cmd, IRenderable renderable, Material material, Mesh mesh, int passIndex, ViewerData viewer)
    {
        renderable.GetRenderingData(viewer, out PropertyState sharedProperties, out Mesh _, out Float4x4 __, out InstanceData[]? instanceData);

        if (instanceData == null || instanceData.Length == 0)
            return;

        // Ensure the shared instance VAO + buffer exist. The actual data upload is
        // encoded into the SAME CommandBuffer as the draw below this matters when
        // multiple instanced batches share the same mesh (e.g. all grass patches of
        // one prototype). Each batch's UpdateBuffer + DrawIndexedInstanced is a pair
        // in the stream, so the executor uploads each batch's data immediately before
        // its draw and they don't clobber each other.
        GraphicsVertexArray vao = mesh.EnsureInstanceVAO(instanceData.Length, out GraphicsBuffer instanceBuf);
        if (vao == null) return;

        int instanceCount = instanceData.Length;
        int indexCount = mesh.IndexCount;
        bool useIndex32 = mesh.IndexFormat == IndexFormat.UInt32;

        material.SetKeyword("HAS_NORMALS", mesh.HasNormals);
        material.SetKeyword("HAS_TANGENTS", mesh.HasTangents);
        material.SetKeyword("HAS_UV", mesh.HasUV);
        material.SetKeyword("HAS_UV2", mesh.HasUV2);
        material.SetKeyword("HAS_COLORS", mesh.HasColors || mesh.HasColors32);
        material.SetKeyword("HAS_BONEINDICES", mesh.HasBoneIndices);
        material.SetKeyword("HAS_BONEWEIGHTS", mesh.HasBoneWeights);
        material.SetKeyword("SKINNED", mesh.HasBoneIndices && mesh.HasBoneWeights);
        material.SetKeyword("BLENDSHAPES", false); // per-instance morph weights aren't supported
        material.SetKeyword("GPU_INSTANCING", true);

        Shaders.ShaderPass pass = material.Shader.GetPass(passIndex);
        if (!pass.TryGetVariantProgram(material._localKeywords, out GraphicsProgram? variantNullable) || variantNullable == null)
        {
            material.SetKeyword("GPU_INSTANCING", false);
            return;
        }

        GraphicsProgram variant = variantNullable;

        cmd.SetShader(variant);
        cmd.SetRasterState(pass.State);

        // GlobalUniforms UBO bound by PrepareDraw automatically.

        cmd.SetMaterialProperties(material);

        if (sharedProperties != null)
            cmd.SetInstanceProperties(sharedProperties);

        // Upload THIS batch's instance data immediately before the draw so the
        // shared instance buffer holds the right contents when the draw executes.
        cmd.UpdateBuffer<InstanceData>(instanceBuf, new System.ReadOnlySpan<InstanceData>(instanceData, 0, instanceCount));

        int subIdx = renderable.GetSubMeshIndex();
        if (subIdx >= 0 && subIdx < mesh.SubMeshCount)
        {
            var sub = mesh.GetSubMesh(subIdx);
            cmd.DrawIndexedInstanced(vao, sub.Topology, (uint)sub.IndexCount, (uint)instanceCount, (uint)sub.IndexStart, 0, useIndex32);
        }
        else
        {
            cmd.DrawIndexedInstanced(vao, Topology.Triangles, (uint)indexCount, (uint)instanceCount, 0, 0, useIndex32);
        }

        material.SetKeyword("GPU_INSTANCING", false);
    }
}
