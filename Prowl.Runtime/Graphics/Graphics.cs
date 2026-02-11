// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Silk.NET.Core.Native;
using Silk.NET.OpenGL;

namespace Prowl.Runtime;

public static unsafe class Graphics
{
    public static int MaxTextureSize { get; internal set; }
    public static int MaxCubeMapTextureSize { get; internal set; }
    public static int MaxArrayTextureLayers { get; internal set; }
    public static int MaxFramebufferColorAttachments { get; internal set; }

    #region Renderable Draw API

    // ============================================================================
    // QUEUED RENDERING API - Unity-style Graphics.DrawMesh/DrawMeshInstanced
    // ============================================================================

    /// <summary>
    /// Queues a single mesh to be rendered by pushing it to the scene's render queue.
    /// The mesh will be rendered during the next frame with the specified material and transform.
    /// </summary>
    /// <param name="scene">Scene to push the renderable to</param>
    /// <param name="mesh">Mesh to render</param>
    /// <param name="transform">World transform matrix</param>
    /// <param name="material">Material to render with</param>
    /// <param name="layer">Layer index for culling and sorting (default: 0)</param>
    /// <param name="properties">Optional per-object property overrides</param>
    public static void DrawMesh(Scene scene, Mesh mesh, Float4x4 transform, Material material, int layer = 0, PropertyState? properties = null)
    {
        if (scene == null || mesh == null || material == null) return;

        var renderable = new MeshRenderable(mesh, material, transform, layer, properties);
        scene.PushRenderable(renderable);
    }

    /// <summary>
    /// Queues multiple instances of a mesh to be rendered with GPU instancing.
    /// Automatically handles batching for large instance counts (>1023 instances).
    /// </summary>
    /// <param name="scene">Scene to push the renderable to</param>
    /// <param name="mesh">Mesh to render</param>
    /// <param name="transforms">Array of world transforms (one per instance)</param>
    /// <param name="material">Material to render with</param>
    /// <param name="worldOrigin">World-space origin for depth sorting (e.g., particle system transform position, terrain chunk center)</param>
    /// <param name="layer">Layer index for culling and sorting (default: 0)</param>
    /// <param name="properties">Optional shared properties for all instances</param>
    /// <param name="bounds">Optional custom bounds for culling. If null, computed from mesh bounds.</param>
    /// <param name="maxBatchSize">Maximum instances per batch (default: 1023)</param>
    public static void DrawMeshInstanced(Scene scene, Mesh mesh, Float4x4[] transforms, Material material, Float3 worldOrigin, int layer = 0, PropertyState? properties = null, AABB? bounds = null, int maxBatchSize = 1023)
    {
        if (scene == null || mesh == null || material == null || transforms == null || transforms.Length == 0) return;

        // Automatic batching for >1023 instances by default
        int remainingInstances = transforms.Length;
        int offset = 0;

        while (remainingInstances > 0)
        {
            int batchSize = Maths.Min(remainingInstances, maxBatchSize);

            // Create instance data for this batch
            var instanceData = new Rendering.InstanceData[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                instanceData[i] = new Rendering.InstanceData(transforms[offset + i]);
            }

            // Push batch to scene
            var renderable = new InstancedMeshRenderable(mesh, material, instanceData, worldOrigin, layer, properties, bounds);
            scene.PushRenderable(renderable);

            remainingInstances -= batchSize;
            offset += batchSize;
        }
    }

    /// <summary>
    /// Queues multiple instances with per-instance colors.
    /// Automatically handles batching for large instance counts (>1023 instances).
    /// </summary>
    /// <param name="worldOrigin">World-space origin for depth sorting (e.g., particle system transform position, terrain chunk center)</param>
    public static void DrawMeshInstanced(Scene scene, Mesh mesh, Float4x4[] transforms, Material material, Float4[] colors, Float3 worldOrigin, int layer = 0, PropertyState? properties = null, AABB? bounds = null, int maxBatchSize = 1023)
    {
        if (scene == null || mesh == null || material == null || transforms == null || transforms.Length == 0) return;

        // Automatic batching for >1023 instances by default
        int remainingInstances = transforms.Length;
        int offset = 0;

        while (remainingInstances > 0)
        {
            int batchSize = Maths.Min(remainingInstances, maxBatchSize);

            // Create instance data for this batch with colors
            var instanceData = new Rendering.InstanceData[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                int idx = offset + i;
                Float4 color = idx < colors.Length ? colors[idx] : new Float4(1, 1, 1, 1);
                instanceData[i] = new Rendering.InstanceData(transforms[idx], color);
            }

            // Push batch to scene
            var renderable = new InstancedMeshRenderable(mesh, material, instanceData, worldOrigin, layer, properties, bounds);
            scene.PushRenderable(renderable);

            remainingInstances -= batchSize;
            offset += batchSize;
        }
    }

    /// <summary>
    /// Queues multiple instances with optional per-instance colors and custom data.
    /// This is the most flexible overload for custom per-instance data (UV offsets, lifetimes, etc.)
    /// Automatically handles batching for large instance counts.
    /// </summary>
    /// <param name="scene">Scene to push the renderable to</param>
    /// <param name="mesh">Mesh to render</param>
    /// <param name="transforms">Array of world transforms (one per instance)</param>
    /// <param name="material">Material to render with</param>
    /// <param name="worldOrigin">World-space origin for depth sorting (e.g., particle system transform position, terrain chunk center)</param>
    /// <param name="colors">Optional per-instance colors (RGBA). If null, defaults to white.</param>
    /// <param name="customData">Optional per-instance custom data (4 floats). Useful for UV offsets, lifetimes, etc.</param>
    /// <param name="layer">Layer index for culling and sorting (default: 0)</param>
    /// <param name="properties">Optional shared properties for all instances</param>
    /// <param name="bounds">Optional custom bounds for culling. If null, computed from mesh bounds.</param>
    /// <param name="maxBatchSize">Maximum instances per batch (default: 1023)</param>
    public static void DrawMeshInstanced(
        Scene scene,
        Mesh mesh,
        Float4x4[] transforms,
        Material material,
        Float3 worldOrigin,
        Float4[]? colors = null,
        Float4[]? customData = null,
        int layer = 0,
        PropertyState? properties = null,
        AABB? bounds = null,
        int maxBatchSize = 1023)
    {
        if (scene == null || mesh == null || material == null || transforms == null || transforms.Length == 0) return;

        // Automatic batching for >maxBatchSize instances
        int remainingInstances = transforms.Length;
        int offset = 0;

        while (remainingInstances > 0)
        {
            int batchSize = Maths.Min(remainingInstances, maxBatchSize);

            // Build InstanceData from separate arrays
            var instanceData = new Rendering.InstanceData[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                int idx = offset + i;
                Float4 color = colors != null && idx < colors.Length ? colors[idx] : new Float4(1, 1, 1, 1);
                Float4 custom = customData != null && idx < customData.Length ? customData[idx] : Float4.Zero;
                instanceData[i] = new Rendering.InstanceData(transforms[idx], color, custom);
            }

            // Push batch to scene
            var renderable = new InstancedMeshRenderable(mesh, material, instanceData, worldOrigin, layer, properties, bounds);
            scene.PushRenderable(renderable);

            remainingInstances -= batchSize;
            offset += batchSize;
        }
    }

    #endregion

    #region Graphics Backend

    public static GL GL;

    public static GraphicsProgram CurrentProgram => GraphicsProgram.currentProgram;

    // Current OpenGL State
    private static bool depthTest = true;
    private static bool depthWrite = true;
    private static RasterizerState.DepthMode depthMode = RasterizerState.DepthMode.Lequal;

    private static bool doBlend = true;
    private static RasterizerState.Blending blendSrc = RasterizerState.Blending.SrcAlpha;
    private static RasterizerState.Blending blendDst = RasterizerState.Blending.OneMinusSrcAlpha;
    private static RasterizerState.BlendMode blendEquation = RasterizerState.BlendMode.Add;

    private static RasterizerState.PolyFace cullFace = RasterizerState.PolyFace.Back;

    private static RasterizerState.WindingOrder winding = RasterizerState.WindingOrder.CW;

    private static GraphicsFrameBuffer? currentFramebuffer = null;
    private static GraphicsFrameBuffer? currentReadFramebuffer = null;
    private static GraphicsFrameBuffer? currentDrawFramebuffer = null;

    public static void Initialize(bool debug)
    {
        GL = GL.GetApi(Window.InternalWindow);

        if (debug)
        {
            unsafe
            {
                if (OperatingSystem.IsWindows())
                {
                    GL.DebugMessageCallback(DebugCallback, null);
                    GL.Enable(EnableCap.DebugOutput);
                    GL.Enable(EnableCap.DebugOutputSynchronous);
                }
            }
        }

        // Smooth lines
        GL.Enable(EnableCap.LineSmooth);

        // Textures
        Graphics.MaxTextureSize = GL.GetInteger(GLEnum.MaxTextureSize);
        Graphics.MaxCubeMapTextureSize = GL.GetInteger(GLEnum.MaxCubeMapTextureSize);
        Graphics.MaxArrayTextureLayers = GL.GetInteger(GLEnum.MaxArrayTextureLayers);
        Graphics.MaxFramebufferColorAttachments = GL.GetInteger(GLEnum.MaxColorAttachments);
    }

    private static void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
    {
        string? msg = SilkMarshal.PtrToString(message, NativeStringEncoding.UTF8);
        if (type == GLEnum.DebugTypeError || type == GLEnum.DebugTypeUndefinedBehavior)
            Debug.LogError($"OpenGL Error: {msg}");
        else if (type == GLEnum.DebugTypePerformance || type == GLEnum.DebugTypeMarker || type == GLEnum.DebugTypePortability)
            Debug.LogWarning($"OpenGL Warning: {msg}");
        //else
        //    Debug.Log($"OpenGL Message: {msg}");
    }

    public static void Viewport(int x, int y, uint width, uint height) => GL.Viewport(x, y, width, height);

    public static void Clear(float r, float g, float b, float a, ClearFlags v)
    {
        GL.ClearColor(r, g, b, a);

        ClearBufferMask clearBufferMask = 0;
        if (v.HasFlag(ClearFlags.Color))
            clearBufferMask |= ClearBufferMask.ColorBufferBit;
        if (v.HasFlag(ClearFlags.Depth))
            clearBufferMask |= ClearBufferMask.DepthBufferBit;
        if (v.HasFlag(ClearFlags.Stencil))
            clearBufferMask |= ClearBufferMask.StencilBufferBit;
        GL.Clear(clearBufferMask);
    }

    public static void SetState(RasterizerState state, bool force = false)
    {
        if (depthTest != state.DepthTest || force)
        {
            if (state.DepthTest)
                GL.Enable(EnableCap.DepthTest);
            else
                GL.Disable(EnableCap.DepthTest);
            depthTest = state.DepthTest;
        }

        if (depthWrite != state.DepthWrite || force)
        {
            GL.DepthMask(state.DepthWrite);
            depthWrite = state.DepthWrite;
        }

        if (depthMode != state.Depth || force)
        {
            GL.DepthFunc(DepthModeToGL(state.Depth));
            depthMode = state.Depth;
        }

        if (doBlend != state.DoBlend || force)
        {
            if (state.DoBlend)
                GL.Enable(EnableCap.Blend);
            else
                GL.Disable(EnableCap.Blend);
            doBlend = state.DoBlend;
        }

        if (blendSrc != state.BlendSrc || blendDst != state.BlendDst || force)
        {
            GL.BlendFunc(BlendingToGL(state.BlendSrc), BlendingToGL(state.BlendDst));
            blendSrc = state.BlendSrc;
            blendDst = state.BlendDst;
        }

        if (blendEquation != state.Blend || force)
        {
            GL.BlendEquation(BlendModeToGL(state.Blend));
            blendEquation = state.Blend;
        }

        if (cullFace != state.CullFace || force)
        {
            if (state.CullFace != RasterizerState.PolyFace.None)
            {
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(CullFaceToGL(state.CullFace));
            }
            else
                GL.Disable(EnableCap.CullFace);
            cullFace = state.CullFace;
        }

        if (winding != state.Winding || force)
        {
            GL.FrontFace(WindingToGL(state.Winding));
            winding = state.Winding;
        }
    }

    public static RasterizerState GetState()
    {
        return new RasterizerState
        {
            DepthTest = depthTest,
            DepthWrite = depthWrite,
            Depth = depthMode,
            DoBlend = doBlend,
            BlendSrc = blendSrc,
            BlendDst = blendDst,
            Blend = blendEquation,
            CullFace = cullFace
        };
    }

    // Helper method to combine program ID and string hash into a unique ulong key
    private static ulong CombineKey(int programId, string name)
    {
        // Combine program ID (upper 32 bits) and string hash (lower 32 bits)
        uint nameHash = unchecked((uint)name.GetHashCode());
        return ((ulong)programId << 32) | nameHash;
    }

    #region Buffers

    public static Dictionary<ulong, uint> cachedBlockLocations = [];

    public static GraphicsBuffer CreateBuffer<T>(BufferType bufferType, T[] data, bool dynamic = false)
    {
        fixed (void* dat = data)
            return new GraphicsBuffer(bufferType, (uint)(data.Length * sizeof(T)), dat, dynamic);
    }

    public static void SetBuffer<T>(GraphicsBuffer buffer, T[] data, bool dynamic = false)
    {
        fixed (void* dat = data)
            buffer!.Set((uint)(data.Length * sizeof(T)), dat, dynamic);
    }

    public static void UpdateBuffer<T>(GraphicsBuffer buffer, uint offsetInBytes, T[] data)
    {
        fixed (void* dat = data)
            buffer!.Update(offsetInBytes, (uint)(data.Length * sizeof(T)), dat);
    }

    public static void BindBuffer(GraphicsBuffer buffer)
    {
        if (buffer is GraphicsBuffer GraphicsBuffer)
            GL.BindBuffer(GraphicsBuffer.Target, GraphicsBuffer.Handle);
    }

    public static uint GetBlockIndex(GraphicsProgram program, string blockName)
    {
        ulong key = CombineKey(program.ID, blockName);
        if (cachedBlockLocations.TryGetValue(key, out uint loc))
            return loc;

        BindProgram(program);
        uint newLoc = GL.GetUniformBlockIndex(program.Handle, blockName);
        cachedBlockLocations[key] = newLoc;
        return newLoc;
    }

    public static void BindUniformBuffer(GraphicsProgram program, string blockName, GraphicsBuffer buffer, uint bindingPoint = 0)
    {
        uint blockIndex = GetBlockIndex(program, blockName);
        // 0xFFFFFFFF (GL_INVALID_INDEX) means the block was not found in the shader
        if (blockIndex == 0xFFFFFFFF) return;

        BindProgram(program);

        // Connect the shader's uniform block index to the specified binding point
        GL.UniformBlockBinding(program.Handle, blockIndex, bindingPoint);

        // Now bind the buffer to that binding point
        GL.BindBufferBase(BufferTargetARB.UniformBuffer, bindingPoint, buffer!.Handle);
    }

    #endregion

    #region Vertex Arrays

    public static GraphicsVertexArray CreateVertexArray(
        VertexFormat format,
        GraphicsBuffer vertices,
        GraphicsBuffer? indices,
        VertexFormat? instanceFormat = null,
        GraphicsBuffer? instanceBuffer = null)
    {
        return new GraphicsVertexArray(format, vertices, indices, instanceFormat, instanceBuffer);
    }

    public static void BindVertexArray(GraphicsVertexArray? vertexArrayObject)
    {
        uint handle = 0;

        if (vertexArrayObject is GraphicsVertexArray vao)
            handle = vao.Handle;

        GL.BindVertexArray(handle);
    }


    #endregion


    #region Frame Buffers

    public static GraphicsFrameBuffer CreateFramebuffer(GraphicsFrameBuffer.Attachment[] attachments, uint width, uint height) => new GraphicsFrameBuffer(attachments, width, height);
    public static void UnbindFramebuffer()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        currentFramebuffer = null;
        currentReadFramebuffer = null;
        currentDrawFramebuffer = null;
    }

    public static void BindFramebuffer(GraphicsFrameBuffer frameBuffer, FBOTarget readFramebuffer = FBOTarget.Framebuffer)
    {
        FramebufferTarget target = readFramebuffer switch
        {
            FBOTarget.Read => FramebufferTarget.ReadFramebuffer,
            FBOTarget.Draw => FramebufferTarget.DrawFramebuffer,
            FBOTarget.Framebuffer => FramebufferTarget.Framebuffer,
            _ => throw new ArgumentOutOfRangeException(nameof(readFramebuffer), readFramebuffer, null),
        };
        GL.BindFramebuffer(target, frameBuffer!.Handle);
        //GL.DrawBuffers((uint)frameBuffer.NumOfAttachments, GraphicsFrameBuffer.buffers);

        // Track current framebuffer
        switch (readFramebuffer)
        {
            case FBOTarget.Read:
                currentReadFramebuffer = frameBuffer;
                break;
            case FBOTarget.Draw:
                currentDrawFramebuffer = frameBuffer;
                break;
            case FBOTarget.Framebuffer:
                currentFramebuffer = frameBuffer;
                currentReadFramebuffer = frameBuffer;
                currentDrawFramebuffer = frameBuffer;
                break;
        }

        Viewport(0, 0, frameBuffer.Width, frameBuffer.Height);
    }

    public static GraphicsFrameBuffer? GetCurrentFramebuffer(FBOTarget target = FBOTarget.Framebuffer)
    {
        return target switch
        {
            FBOTarget.Read => currentReadFramebuffer,
            FBOTarget.Draw => currentDrawFramebuffer,
            FBOTarget.Framebuffer => currentFramebuffer,
            _ => currentFramebuffer,
        };
    }

    public static void BlitFramebuffer(int srcX, int srcY, int srcWidth, int srcHeight, int destX, int destY, int destWidth, int destHeight, ClearFlags mask, BlitFilter filter)
    {
        ClearBufferMask clearBufferMask = 0;
        if (mask.HasFlag(ClearFlags.Color))
            clearBufferMask |= ClearBufferMask.ColorBufferBit;
        if (mask.HasFlag(ClearFlags.Depth))
            clearBufferMask |= ClearBufferMask.DepthBufferBit;
        if (mask.HasFlag(ClearFlags.Stencil))
            clearBufferMask |= ClearBufferMask.StencilBufferBit;

        BlitFramebufferFilter nearest = filter switch
        {
            BlitFilter.Nearest => BlitFramebufferFilter.Nearest,
            BlitFilter.Linear => BlitFramebufferFilter.Linear,
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
        };

        GL.BlitFramebuffer(srcX, srcY, srcWidth, srcHeight, destX, destY, destWidth, destHeight, clearBufferMask, nearest);
    }

    public static T ReadPixel<T>(int attachment, int x, int y, TextureImageFormat format) where T : unmanaged
    {
        GL.ReadBuffer((ReadBufferMode)((int)ReadBufferMode.ColorAttachment0 + attachment));
        GraphicsTexture.GetTextureFormatEnums(format, out InternalFormat internalFormat, out PixelType pixelType, out PixelFormat pixelFormat);
        return GL.ReadPixels<T>(x, y, 1, 1, pixelFormat, pixelType);
    }

    #endregion

    #region Shaders

    public static Dictionary<ulong, int> cachedUniformLocations = [];
    public static Dictionary<ulong, int> cachedAttribLocations = [];

    public static GraphicsProgram CompileProgram(string fragment, string vertex, string geometry) => new GraphicsProgram(fragment, vertex, geometry);
    public static void BindProgram(GraphicsProgram program) => program!.Use();

    public static int GetUniformLocation(GraphicsProgram program, string name)
    {
        ulong key = CombineKey(program.ID, name);
        if (cachedUniformLocations.TryGetValue(key, out int loc))
            return loc;

        BindProgram(program);
        int newLoc = GL.GetUniformLocation(program.Handle, name);
        cachedUniformLocations[key] = newLoc;
        return newLoc;
    }

    public static int GetAttribLocation(GraphicsProgram program, string name)
    {
        ulong key = CombineKey(program.ID, name);
        if (cachedAttribLocations.TryGetValue(key, out int loc))
            return loc;

        BindProgram(program);
        int newLoc = GL.GetAttribLocation(program.Handle, name);
        cachedAttribLocations[key] = newLoc;
        return newLoc;
    }

    public static void SetUniformF(GraphicsProgram program, string name, float value)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.Uniform1(loc, value);
    }

    public static void SetUniformI(GraphicsProgram program, string name, int value)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.Uniform1(loc, value);
    }

    public static void SetUniformV2(GraphicsProgram program, string name, Float2 value)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.Uniform2(loc, value); // Casts to System.Numerics.Vector2
    }

    public static void SetUniformV3(GraphicsProgram program, string name, Float3 value)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.Uniform3(loc, value); // Casts to System.Numerics.Vector3
    }

    public static void SetUniformV4(GraphicsProgram program, string name, Float4 value)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.Uniform4(loc, value); // Casts to System.Numerics.Vector4
    }

    public static void SetUniformMatrix(GraphicsProgram program, string name, bool transpose, Float4x4 matrix)
    {
        Float4x4 fMat = matrix;
        SetUniformMatrix(program, name, 1, transpose, in fMat.c0.X);
    }
    public static void SetUniformMatrix(GraphicsProgram program, string name, bool transpose, in float matrix) => SetUniformMatrix(program, name, 1, transpose, in matrix);

    public static void SetUniformMatrix(GraphicsProgram program, string name, uint count, bool transpose, in float matrix)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.UniformMatrix4(loc, count, transpose, in matrix);
    }

    public static void SetUniformTexture(GraphicsProgram program, string name, int slot, GraphicsTexture texture)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.ActiveTexture((TextureUnit)((uint)TextureUnit.Texture0 + slot));
        texture.Bind();
        GL.Uniform1(loc, slot);
    }

    #endregion

    #region Textures

    public static GraphicsTexture CreateTexture(TextureType type, TextureImageFormat format) => new GraphicsTexture(type, format);
    public static void SetWrapS(GraphicsTexture texture, TextureWrap wrap) => texture!.SetWrapS(wrap);

    public static void SetWrapT(GraphicsTexture texture, TextureWrap wrap) => texture!.SetWrapT(wrap);
    public static void SetWrapR(GraphicsTexture texture, TextureWrap wrap) => texture!.SetWrapR(wrap);
    public static void SetTextureFilters(GraphicsTexture texture, TextureMin min, TextureMag mag) => texture!.SetTextureFilters(min, mag);
    public static void GenerateMipmap(GraphicsTexture texture) => texture!.GenerateMipmap();

    public static unsafe void GetTexImage(GraphicsTexture texture, int mip, void* data) => texture!.GetTexImage(mip, data);

    public static unsafe void TexImage2D(GraphicsTexture texture, int mip, uint width, uint height, int v2, void* data)
        => texture!.TexImage2D(texture.Target, mip, width, height, v2, data);
    public static unsafe void TexSubImage2D(GraphicsTexture texture, int mip, int x, int y, uint width, uint height, void* data)
        => texture!.TexSubImage2D(texture.Target, mip, x, y, width, height, data);

    public static unsafe void TexImage3D(GraphicsTexture texture, int level, uint width, uint height, uint depth, void* data)
        => texture!.TexImage3D(texture.Target, level, width, height, depth, data);
    public static unsafe void TexSubImage3D(GraphicsTexture texture, int level, int x, int y, int z, uint width, uint height, uint depth, void* data)
        => texture!.TexSubImage3D(texture.Target, level, x, y, z, width, height, depth, data);

    #endregion

    public static void Draw(Topology primitiveType, uint count) => Draw(primitiveType, 0, count);


    public static void Draw(Topology primitiveType, int v, uint count)
    {
        PrimitiveType mode = TopologyToGL(primitiveType);
        GL.DrawArrays(mode, v, count);
    }
    public static unsafe void DrawIndexed(Topology primitiveType, uint indexCount, bool index32bit, void* value)
    {
        PrimitiveType mode = TopologyToGL(primitiveType);
        GL.DrawElements(mode, indexCount, index32bit ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort, value);
    }

    public static unsafe void DrawIndexed(Topology primitiveType, uint indexCount, int startIndex, int baseVertex, bool index32bit)
    {
        PrimitiveType mode = TopologyToGL(primitiveType);

        DrawElementsType format = index32bit ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort;
        int formatSize = index32bit ? sizeof(uint) : sizeof(ushort);
        GL.DrawElementsBaseVertex(mode, indexCount, format, (void*)(startIndex * formatSize), baseVertex);
    }

    public static unsafe void DrawIndexedInstanced(Topology primitiveType, uint indexCount, uint instanceCount, bool index32bit)
    {
        // Verify a VAO is bound
        GL.GetInteger(GetPName.VertexArrayBinding, out int currentVAO);
        if (currentVAO == 0)
        {
            throw new System.InvalidOperationException("DrawIndexedInstanced called with no VAO bound! Bind a vertex array first.");
        }

        PrimitiveType mode = TopologyToGL(primitiveType);
        DrawElementsType format = index32bit ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort;

        GL.DrawElementsInstanced(mode, indexCount, format, null, instanceCount);
    }

    public static void Dispose()
    {
        GL.Dispose();
    }

    #region Private


    private static PrimitiveType TopologyToGL(Topology triangles)
    {
        return triangles switch
        {
            Topology.Points => PrimitiveType.Points,
            Topology.Lines => PrimitiveType.Lines,
            Topology.LineLoop => PrimitiveType.LineLoop,
            Topology.LineStrip => PrimitiveType.LineStrip,
            Topology.Triangles => PrimitiveType.Triangles,
            Topology.TriangleStrip => PrimitiveType.TriangleStrip,
            Topology.TriangleFan => PrimitiveType.TriangleFan,
            _ => throw new ArgumentOutOfRangeException(nameof(triangles), triangles, null),
        };
    }

    private static DepthFunction DepthModeToGL(RasterizerState.DepthMode depthMode)
    {
        return depthMode switch
        {
            RasterizerState.DepthMode.Never => DepthFunction.Never,
            RasterizerState.DepthMode.Less => DepthFunction.Less,
            RasterizerState.DepthMode.Equal => DepthFunction.Equal,
            RasterizerState.DepthMode.Lequal => DepthFunction.Lequal,
            RasterizerState.DepthMode.Greater => DepthFunction.Greater,
            RasterizerState.DepthMode.Notequal => DepthFunction.Notequal,
            RasterizerState.DepthMode.Gequal => DepthFunction.Gequal,
            RasterizerState.DepthMode.Always => DepthFunction.Always,
            _ => throw new ArgumentOutOfRangeException(nameof(depthMode), depthMode, null),
        };
    }

    private static BlendingFactor BlendingToGL(RasterizerState.Blending blending)
    {
        return blending switch
        {
            RasterizerState.Blending.Zero => BlendingFactor.Zero,
            RasterizerState.Blending.One => BlendingFactor.One,
            RasterizerState.Blending.SrcColor => BlendingFactor.SrcColor,
            RasterizerState.Blending.OneMinusSrcColor => BlendingFactor.OneMinusSrcColor,
            RasterizerState.Blending.DstColor => BlendingFactor.DstColor,
            RasterizerState.Blending.OneMinusDstColor => BlendingFactor.OneMinusDstColor,
            RasterizerState.Blending.SrcAlpha => BlendingFactor.SrcAlpha,
            RasterizerState.Blending.OneMinusSrcAlpha => BlendingFactor.OneMinusSrcAlpha,
            RasterizerState.Blending.DstAlpha => BlendingFactor.DstAlpha,
            RasterizerState.Blending.OneMinusDstAlpha => BlendingFactor.OneMinusDstAlpha,
            RasterizerState.Blending.ConstantColor => BlendingFactor.ConstantColor,
            RasterizerState.Blending.OneMinusConstantColor => BlendingFactor.OneMinusConstantColor,
            RasterizerState.Blending.ConstantAlpha => BlendingFactor.ConstantAlpha,
            RasterizerState.Blending.OneMinusConstantAlpha => BlendingFactor.OneMinusConstantAlpha,
            RasterizerState.Blending.SrcAlphaSaturate => BlendingFactor.SrcAlphaSaturate,
            _ => throw new ArgumentOutOfRangeException(nameof(blending), blending, null),
        };
    }

    private static BlendEquationModeEXT BlendModeToGL(RasterizerState.BlendMode blendMode)
    {
        return blendMode switch
        {
            RasterizerState.BlendMode.Add => BlendEquationModeEXT.FuncAdd,
            RasterizerState.BlendMode.Subtract => BlendEquationModeEXT.FuncSubtract,
            RasterizerState.BlendMode.ReverseSubtract => BlendEquationModeEXT.FuncReverseSubtract,
            RasterizerState.BlendMode.Min => BlendEquationModeEXT.Min,
            RasterizerState.BlendMode.Max => BlendEquationModeEXT.Max,
            _ => throw new ArgumentOutOfRangeException(nameof(blendMode), blendMode, null),
        };
    }

    private static TriangleFace CullFaceToGL(RasterizerState.PolyFace cullFace)
    {
        return cullFace switch
        {
            RasterizerState.PolyFace.Front => TriangleFace.Front,
            RasterizerState.PolyFace.Back => TriangleFace.Back,
            RasterizerState.PolyFace.FrontAndBack => TriangleFace.FrontAndBack,
            _ => throw new ArgumentOutOfRangeException(nameof(cullFace), cullFace, null),
        };
    }

    private static FrontFaceDirection WindingToGL(RasterizerState.WindingOrder winding)
    {
        return winding switch
        {
            RasterizerState.WindingOrder.CW => FrontFaceDirection.CW,
            RasterizerState.WindingOrder.CCW => FrontFaceDirection.Ccw,
            _ => throw new ArgumentOutOfRangeException(nameof(winding), winding, null),
        };
    }

    #endregion

    #endregion
}
