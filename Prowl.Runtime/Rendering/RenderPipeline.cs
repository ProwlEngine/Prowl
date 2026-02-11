// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

public struct RenderingData
{
    public bool DisplayGizmo;
    public Float4x4 GridMatrix;
    public Color GridColor;
    public Float3 GridSizes;
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
    /// Gets the rendering data for this renderable.
    /// </summary>
    /// <param name="viewer">Camera viewing data for culling/LOD</param>
    /// <param name="properties">Shader properties (per-object or shared for instances)</param>
    /// <param name="mesh">Mesh to render</param>
    /// <param name="model">Model matrix (only used for single-instance rendering)</param>
    /// <param name="instanceData">Instance data array for GPU instancing, or null for single-instance rendering</param>
    public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData);

    public void GetCullingData(out bool isRenderable, out AABB bounds);
}

public enum LightType
{
    Directional,
    Spot,
    Point,
    //Area
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
    /// Renders the light's contribution to the scene.
    /// Similar to ImageEffect.OnRenderImage, lights control their own drawing.
    /// </summary>
    /// <param name="gBuffer">GBuffer containing scene geometry data</param>
    /// <param name="destination">Destination render texture to draw light contribution to</param>
    /// <param name="css">Camera snapshot containing view/projection matrices and other camera data</param>
    public void OnRenderLight(RenderTexture gBuffer, RenderTexture destination, RenderPipeline.CameraSnapshot css);
}

public abstract class RenderPipeline : EngineObject
{
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
        public Float4x4 PreviousViewProj = camera.PreviousViewProjectionMatrix;
        public Frustum WorldFrustum = Frustum.FromMatrix(camera.ProjectionMatrix * camera.ViewMatrix);
        public DepthTextureMode DepthTextureMode = camera.DepthTextureMode; // Flags, Can be None, Normals, MotionVectors
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

    private void TrackModelMatrix(int objectId, Float4x4 currentModel)
    {
        // Mark this object ID as active this frame
        ActiveObjectIds.Add(objectId);

        // Store current model matrix for next frame
        if (s_prevModelMatrices.TryGetValue(objectId, out Float4x4 prevModel))
            PropertyState.SetGlobalMatrix("prowl_PrevObjectToWorld", prevModel);
        else
            PropertyState.SetGlobalMatrix("prowl_PrevObjectToWorld", currentModel); // First frame, use current matrix

        s_prevModelMatrices[objectId] = currentModel;
    }

    public virtual void Render(Camera camera, in RenderingData data)
    {
        // Clean up unused matrices after rendering
        CleanupUnusedModelMatrices();
    }

    public HashSet<int> CullRenderables(IReadOnlyList<IRenderable> renderables, Frustum? worldFrustum, LayerMask cullingMask)
    {
        HashSet<int> culledRenderableIndices = [];
        for (int renderIndex = 0; renderIndex < renderables.Count; renderIndex++)
        {
            IRenderable renderable = renderables[renderIndex];

            if (worldFrustum != null && CullRenderable(renderable, worldFrustum.Value))
            {
                culledRenderableIndices.Add(renderIndex);
                continue;
            }

            if (cullingMask.HasLayer(renderable.GetLayer()) == false)
            {
                culledRenderableIndices.Add(renderIndex);
                continue;
            }
        }
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
    public List<IRenderable> SortRenderables(IReadOnlyList<IRenderable> renderables, HashSet<int> culledRenderableIndices, Float3 cameraPosition, SortMode mode)
    {
        int count = renderables?.Count ?? 0;
        if (count == 0)
            return new List<IRenderable>();

        // Preallocate to the maximum possible count to avoid reallocation
        var pairs = new List<(IRenderable renderable, float distSq)>(count);

        // Collect only non-culled renderables
        for (int i = 0; i < count; i++)
        {
            if (culledRenderableIndices != null && culledRenderableIndices.Contains(i))
                continue;

            var renderable = renderables[i];
            float distSq = Float3.DistanceSquared(renderable.GetPosition(), cameraPosition);
            pairs.Add((renderable, distSq));
        }

        // Sort by distance squared (avoid sqrt)
        pairs.Sort((a, b) => mode switch
        {
            SortMode.FrontToBack => a.distSq.CompareTo(b.distSq),
            SortMode.BackToFront => b.distSq.CompareTo(a.distSq),
            _ => 0
        });

        // Extract sorted renderables into result list
        var result = new List<IRenderable>(pairs.Count);
        for (int i = 0; i < pairs.Count; i++)
            result.Add(pairs[i].renderable);

        return result;
    }

    public void SetupGlobalUniforms(CameraSnapshot css)
    {
        // Set View Rect
        //buffer.SetViewports((int)(camera.Viewrect.x * target.Width), (int)(camera.Viewrect.y * target.Height), (int)(camera.Viewrect.width * target.Width), (int)(camera.Viewrect.height * target.Height), 0, 1000);

        GlobalUniforms.SetPrevViewProj(css.PreviousViewProj);

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
        GlobalUniforms.SetMatrixV(view);
        GlobalUniforms.SetMatrixIV(view.Invert());
        GlobalUniforms.SetMatrixP(projection);
        GlobalUniforms.SetMatrixVP(projection * view);

        // Upload the global uniform buffer
        GlobalUniforms.Upload();
    }

    #region Immediate Rendering (DrawMeshNow & Blit)

    /// <summary>
    /// Immediately draws a mesh without queuing. Used internally by the render pipeline.
    /// For queued rendering, use Graphics.DrawMesh() instead.
    /// </summary>
    public static void DrawMeshNow(Mesh mesh, Material mat, int passIndex = 0)
    {
        if (mesh.VertexCount <= 0) return;

        // Mesh data can vary between meshes, so we need to let the shader know which attributes are in use
        mat.SetKeyword("HAS_NORMALS", mesh.HasNormals);
        mat.SetKeyword("HAS_TANGENTS", mesh.HasTangents);
        mat.SetKeyword("HAS_UV", mesh.HasUV);
        mat.SetKeyword("HAS_UV2", mesh.HasUV2);
        mat.SetKeyword("HAS_COLORS", mesh.HasColors || mesh.HasColors32);
        mat.SetKeyword("HAS_BONEINDICES", mesh.HasBoneIndices);
        mat.SetKeyword("HAS_BONEWEIGHTS", mesh.HasBoneWeights);
        mat.SetKeyword("SKINNED", mesh.HasBoneIndices && mesh.HasBoneWeights);

        Shaders.ShaderPass pass = mat.Shader.GetPass(passIndex);

        if (!pass.TryGetVariantProgram(mat._localKeywords, out GraphicsProgram? variant))
            throw new System.Exception($"Failed to set shader pass {pass.Name}. No variant found for the current keyword state.");

        Graphics.SetState(pass.State);

        PropertyState.Apply(mat._properties, variant);

        mesh.Upload();

        unsafe
        {
            Graphics.BindVertexArray(mesh.VertexArrayObject);
            Graphics.DrawIndexed(mesh.MeshTopology, (uint)mesh.IndexCount, mesh.IndexFormat == IndexFormat.UInt32, null);
            Graphics.BindVertexArray(null);
        }
    }

    private static Shader? s_blitShader;
    private static Material? s_blitMaterial;
    public static Material BlitMaterial
    {
        get
        {
            if (s_blitShader.IsNotValid())
                s_blitShader = Shader.LoadDefault(DefaultShader.Blit);

            if (s_blitMaterial.IsNotValid())
                s_blitMaterial = new Material(s_blitShader);

            return s_blitMaterial;
        }
    }

    public static void Blit(Texture2D source, Material? mat = null, int pass = 0)
    {
        mat ??= BlitMaterial;
        mat.SetTexture("_MainTex", source);
        Blit(mat, pass);
    }

    public static void Blit(RenderTexture source, RenderTexture target, Material? mat = null, int pass = 0, bool clearDepth = false, bool clearColor = false, Color color = default)
    {
        mat ??= BlitMaterial;
        mat.SetTexture("_MainTex", source.MainTexture);
        Blit(target, mat, pass, clearDepth, clearColor, color);
    }

    public static void Blit(Texture2D source, RenderTexture target, Material? mat = null, int pass = 0, bool clearDepth = false, bool clearColor = false, Color color = default)
    {
        mat ??= BlitMaterial;
        mat.SetTexture("_MainTex", source);
        Blit(target, mat, pass, clearDepth, clearColor, color);
    }

    public static void Blit(RenderTexture target, Material? mat = null, int pass = 0, bool clearDepth = false, bool clearColor = false, Color color = default)
    {
        mat ??= BlitMaterial;
        if (target.IsValid())
        {
            Graphics.BindFramebuffer(target.frameBuffer);
        }
        else
        {
            Graphics.UnbindFramebuffer();
            Graphics.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
        }
        if (clearDepth || clearColor)
        {
            ClearFlags clear = 0;
            if (clearDepth) clear |= ClearFlags.Depth;
            if (clearColor) clear |= ClearFlags.Color;
            Graphics.Clear((float)color.R, (float)color.G, (float)color.B, (float)color.A, clear | ClearFlags.Stencil);
        }
        Blit(mat, pass);
    }

    public static void Blit(Material? mat = null, int pass = 0)
    {
        mat ??= BlitMaterial;
        DrawMeshNow(Mesh.GetFullscreenQuad(), mat, pass);
    }

    #endregion

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
    public void DrawRenderables(IReadOnlyList<IRenderable> renderables, string shaderTag, string tagValue, ViewerData viewer, HashSet<int> culledRenderableIndices, bool updatePreviousMatrices)
    {
        bool hasRenderOrder = !string.IsNullOrWhiteSpace(shaderTag);
        bool hasSortOffsets = false;

        // ========== PHASE 1: Build Batches ==========
        // Group renderables by (material hash, shader pass, mesh) for efficient rendering
        List<RenderBatch> batches = new();
        Dictionary<(ulong, int, Mesh), int> batchLookup = new();

        for (int renderIndex = 0; renderIndex < renderables.Count; renderIndex++)
        {
            // Skip culled objects
            if (culledRenderableIndices?.Contains(renderIndex) ?? false)
                continue;

            IRenderable renderable = renderables[renderIndex];

            Material material = renderable.GetMaterial();
            if (material.Shader.IsNotValid()) continue;

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
                    RenderBatch newBatch = new()
                    {
                        Material = material,
                        Mesh = mesh,
                        PassIndex = passIndex,
                        MaterialHash = materialHash,
                        SortKey = sortKey,
                        RenderableIndices = new() { renderIndex }
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

        // ========== PHASE 2: Draw Batches ==========
        // For each batch, bind state once then draw all objects in that batch
        foreach (RenderBatch batch in batches)
        {
            // Handle instanced batches separately
            if (batch.IsInstanced)
            {
                IRenderable instancedRenderable = renderables[batch.InstancedRenderableIndex];
                DrawInstancedRenderablePass(instancedRenderable, batch.Material, batch.Mesh, batch.PassIndex, viewer);
                continue;
            }

            Material material = batch.Material;
            Mesh mesh = batch.Mesh;
            int passIndex = batch.PassIndex;
            RenderTexture grabRT = null;

            // Configure shader keywords based on mesh attributes (normals, UVs, skinning, etc.)
            // Since all objects in the batch share the same mesh, this is done once per batch
            material.SetKeyword("HAS_NORMALS", mesh.HasNormals);
            material.SetKeyword("HAS_TANGENTS", mesh.HasTangents);
            material.SetKeyword("HAS_UV", mesh.HasUV);
            material.SetKeyword("HAS_UV2", mesh.HasUV2);
            material.SetKeyword("HAS_COLORS", mesh.HasColors || mesh.HasColors32);
            material.SetKeyword("HAS_BONEINDICES", mesh.HasBoneIndices);
            material.SetKeyword("HAS_BONEWEIGHTS", mesh.HasBoneWeights);
            material.SetKeyword("SKINNED", mesh.HasBoneIndices && mesh.HasBoneWeights);

            // Get shader pass and compiled variant for current keyword state
            ShaderPass pass = material.Shader.GetPass(passIndex);
            if (!pass.TryGetVariantProgram(material._localKeywords, out GraphicsProgram? variantNullable) || variantNullable == null)
                continue;

            GraphicsProgram variant = variantNullable;

            // Handle GrabTexture if this pass requests it
            // NOTE: GrabTexture captures whatever is currently bound, so this works for any render target
            if (pass.HasGrabTexture)
            {
                // Get the currently bound framebuffer so we can restore it
                GraphicsFrameBuffer? currentFB = Graphics.GetCurrentFramebuffer(FBOTarget.Draw);

                if (currentFB != null)
                {
                    // Get framebuffer dimensions from the current render target
                    int fbWidth = (int)currentFB.Width;
                    int fbHeight = (int)currentFB.Height;

                    // Create temporary RT for grabbed texture
                    grabRT = RenderTexture.GetTemporaryRT(fbWidth, fbHeight, false, [TextureImageFormat.Color4b]);

                    // Setup blit: currentFB (read) -> grabRT (draw)
                    Graphics.BindFramebuffer(currentFB, FBOTarget.Read);
                    Graphics.BindFramebuffer(grabRT.frameBuffer, FBOTarget.Draw);
                    Graphics.BlitFramebuffer(0, 0, fbWidth, fbHeight, 0, 0, fbWidth, fbHeight, ClearFlags.Color, BlitFilter.Nearest);

                    // Restore the original framebuffer (for both read and draw)
                    Graphics.BindFramebuffer(currentFB, FBOTarget.Framebuffer);

                    // Set as global texture for this and subsequent passes
                    PropertyState.SetGlobalTexture(pass.GrabTextureName, grabRT.MainTexture);
                }
            }

            // Bind GlobalUniforms buffer (contains camera matrices, time, lighting data, etc.)
            // This is done per-batch because each shader variant is a separate GPU program object,
            // and uniform buffer bindings are per-program in OpenGL.
            //
            // TODO: Could be optimized with glBindBufferBase() for global binding points (OpenGL >=4.2)
            // Researched: We're limited to OpenGL <=4.1 for macOS support, which doesn't support
            // persistent uniform buffer bindings across programs. Current approach is correct for <=4.1.
            GraphicsBuffer? globalBuffer = GlobalUniforms.GetBuffer();
            if (globalBuffer != null)
            {
                Graphics.BindUniformBuffer(variant, "GlobalUniforms", globalBuffer, 0);
            }

            // Apply global properties (lighting, fog, shadow maps, etc.)
            // Must be done per-batch because different shader variants may need different globals
            GraphicsProgram.UniformCache cache = variant.uniformCache;
            int texSlot = 0;
            PropertyState.ApplyGlobals(variant, cache, ref texSlot);

            // *** BATCHING OPTIMIZATION: Bind material uniforms ONCE for entire batch ***
            // All objects in this batch share the same material state
            PropertyState.ApplyMaterialUniforms(material._properties, variant, ref texSlot);

            // Set render state (depth test, blend mode, cull mode, etc.) once per batch
            Graphics.SetState(pass.State);

            // Upload mesh data to GPU once per batch (shared by all objects)
            mesh.Upload();

            // ========== PHASE 3: Draw Objects in Batch ==========
            // Material/mesh state is already bound - only per-object uniforms change
            foreach (int renderIndex in batch.RenderableIndices)
            {
                IRenderable renderable = renderables[renderIndex];

                // Get per-object data (transform, instance properties)
                // Note: mesh and instanceData are discarded (we already have them from the batch)
                renderable.GetRenderingData(viewer, out PropertyState properties, out Mesh _, out Float4x4 model, out InstanceData[]? _);

                // Track model matrix for motion vectors (used in temporal effects like TAA)
                int instanceId = properties.GetInt("_ObjectID");
                if (updatePreviousMatrices && instanceId != 0)
                    TrackModelMatrix(instanceId, model);

                // Apply instance-specific uniforms (tint colors, bone matrices, etc.)
                // Texture slot counter continues from where material textures left off
                int instanceTexSlot = texSlot;
                PropertyState.ApplyInstanceUniforms(properties, variant, ref instanceTexSlot);

                // Directly bind per-object transform uniforms after all other uniforms to gaurantee they are set correctly
                var fModel = (Float4x4)model;
                Graphics.SetUniformMatrix(variant, "prowl_ObjectToWorld", false, fModel);
                Graphics.SetUniformMatrix(variant, "prowl_WorldToObject", false, fModel.Invert());

                // Execute draw call (mesh VAO already uploaded, just bind and draw)
                unsafe
                {
                    Graphics.BindVertexArray(mesh.VertexArrayObject);
                    Graphics.DrawIndexed(mesh.MeshTopology, (uint)mesh.IndexCount, mesh.IndexFormat == IndexFormat.UInt32, null);
                    Graphics.BindVertexArray(null);
                }
            }

            // Release grab texture RT if used
            if (grabRT != null)
            {
                PropertyState.SetGlobalTexture(pass.GrabTextureName, null);
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
    private void DrawInstancedRenderablePass(IRenderable renderable, Material material, Mesh mesh, int passIndex, ViewerData viewer)
    {
        // Get rendering data (mesh, properties, instance data)
        renderable.GetRenderingData(viewer, out PropertyState sharedProperties, out Mesh _, out Float4x4 __, out InstanceData[]? instanceData);

        if (instanceData == null || instanceData.Length == 0)
            return;

        // Get instanced VAO from mesh (creates and caches on first use)
        GraphicsVertexArray vao = mesh.GetOrCreateInstanceVAO(instanceData, instanceData.Length);

        if (vao == null)
            return;

        // Get rendering info from mesh
        int instanceCount = instanceData.Length;
        int indexCount = mesh.IndexCount;
        bool useIndex32 = mesh.IndexFormat == IndexFormat.UInt32;

        // Enable GPU instancing keyword
        material.SetKeyword("GPU_INSTANCING", true);

        // Get the specific shader pass
        Shaders.ShaderPass pass = material.Shader.GetPass(passIndex);

        // Get shader variant
        if (!pass.TryGetVariantProgram(material._localKeywords, out GraphicsProgram? variantNullable) || variantNullable == null)
        {
            material.SetKeyword("GPU_INSTANCING", false);
            return;
        }

        GraphicsProgram variant = variantNullable;

        // Bind GlobalUniforms buffer
        GraphicsBuffer? globalBuffer = GlobalUniforms.GetBuffer();
        if (globalBuffer != null)
        {
            Graphics.BindUniformBuffer(variant, "GlobalUniforms", globalBuffer, 0);
        }

        // Apply global properties
        GraphicsProgram.UniformCache cache = variant.uniformCache;
        int texSlot = 0;
        PropertyState.ApplyGlobals(variant, cache, ref texSlot);

        // Apply material uniforms
        PropertyState.ApplyMaterialUniforms(material._properties, variant, ref texSlot);

        // Apply shared instance properties
        int instanceTexSlot = texSlot;
        PropertyState.ApplyInstanceUniforms(sharedProperties, variant, ref instanceTexSlot);

        // Set render state
        Graphics.SetState(pass.State);

        // Draw with TRUE GPU instancing!
        unsafe
        {
            Graphics.BindVertexArray(vao);
            Graphics.DrawIndexedInstanced(
                Topology.Triangles,
                (uint)indexCount,
                (uint)instanceCount,
                useIndex32
            );
            Graphics.BindVertexArray(null);
        }

        material.SetKeyword("GPU_INSTANCING", false);
    }
}
