// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.GraphicsBackend.Primitives;
using Prowl.Vector;

using Silk.NET.Core.Native;
using Silk.NET.OpenGL;

namespace Prowl.Runtime.GraphicsBackend.OpenGL;


public sealed unsafe class GLDevice : GraphicsDevice
{
    public static GL GL;

    // Current OpenGL State
    private bool depthTest = true;
    private bool depthWrite = true;
    private RasterizerState.DepthMode depthMode = RasterizerState.DepthMode.Lequal;

    private bool doBlend = true;
    private RasterizerState.Blending blendSrc = RasterizerState.Blending.SrcAlpha;
    private RasterizerState.Blending blendDst = RasterizerState.Blending.OneMinusSrcAlpha;
    private RasterizerState.BlendMode blendEquation = RasterizerState.BlendMode.Add;

    private RasterizerState.PolyFace cullFace = RasterizerState.PolyFace.Back;

    private RasterizerState.WindingOrder winding = RasterizerState.WindingOrder.CW;

    private GraphicsFrameBuffer? currentFramebuffer = null;
    private GraphicsFrameBuffer? currentReadFramebuffer = null;
    private GraphicsFrameBuffer? currentDrawFramebuffer = null;

    public override void Initialize(bool debug)
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

    public override void Viewport(int x, int y, uint width, uint height) => GL.Viewport(x, y, width, height);

    public override void Clear(float r, float g, float b, float a, ClearFlags v)
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

    public override void SetState(RasterizerState state, bool force = false)
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

    public override RasterizerState GetState()
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

    public override GraphicsProgram CurrentProgram => GLProgram.currentProgram;

    // Helper method to combine program ID and string hash into a unique ulong key
    private static ulong CombineKey(int programId, string name)
    {
        // Combine program ID (upper 32 bits) and string hash (lower 32 bits)
        uint nameHash = unchecked((uint)name.GetHashCode());
        return ((ulong)programId << 32) | nameHash;
    }

    #region Buffers

    public static Dictionary<ulong, uint> cachedBlockLocations = [];

    public override GraphicsBuffer CreateBuffer<T>(BufferType bufferType, T[] data, bool dynamic = false)
    {
        fixed (void* dat = data)
            return new GLBuffer(bufferType, (uint)(data.Length * sizeof(T)), dat, dynamic);
    }

    public override void SetBuffer<T>(GraphicsBuffer buffer, T[] data, bool dynamic = false)
    {
        fixed (void* dat = data)
            (buffer as GLBuffer)!.Set((uint)(data.Length * sizeof(T)), dat, dynamic);
    }

    public override void UpdateBuffer<T>(GraphicsBuffer buffer, uint offsetInBytes, T[] data)
    {
        fixed (void* dat = data)
            (buffer as GLBuffer)!.Update(offsetInBytes, (uint)(data.Length * sizeof(T)), dat);
    }

    public override void BindBuffer(GraphicsBuffer buffer)
    {
        if (buffer is GLBuffer glBuffer)
            GL.BindBuffer(glBuffer.Target, glBuffer.Handle);
    }

    public override uint GetBlockIndex(GraphicsProgram program, string blockName)
    {
        ulong key = CombineKey(program.ID, blockName);
        if (cachedBlockLocations.TryGetValue(key, out uint loc))
            return loc;

        BindProgram(program);
        uint newLoc = GL.GetUniformBlockIndex((program as GLProgram).Handle, blockName);
        cachedBlockLocations[key] = newLoc;
        return newLoc;
    }

    public override void BindUniformBuffer(GraphicsProgram program, string blockName, GraphicsBuffer buffer, uint bindingPoint = 0)
    {
        uint blockIndex = GetBlockIndex(program, blockName);
        // 0xFFFFFFFF (GL_INVALID_INDEX) means the block was not found in the shader
        if (blockIndex == 0xFFFFFFFF) return;

        BindProgram(program);

        // Connect the shader's uniform block index to the specified binding point
        GL.UniformBlockBinding((program as GLProgram).Handle, blockIndex, bindingPoint);

        // Now bind the buffer to that binding point
        GL.BindBufferBase(BufferTargetARB.UniformBuffer, bindingPoint, (buffer as GLBuffer)!.Handle);
    }

    #endregion

    #region Vertex Arrays

    public override GraphicsVertexArray CreateVertexArray(
        VertexFormat format,
        GraphicsBuffer vertices,
        GraphicsBuffer? indices,
        VertexFormat? instanceFormat = null,
        GraphicsBuffer? instanceBuffer = null)
    {
        return new GLVertexArray(format, vertices, indices, instanceFormat, instanceBuffer);
    }

    public override void BindVertexArray(GraphicsVertexArray? vertexArrayObject)
    {
        uint handle = 0;

        if (vertexArrayObject is GLVertexArray vao)
            handle = vao.Handle;

        GL.BindVertexArray(handle);
    }


    #endregion


    #region Frame Buffers

    public override GraphicsFrameBuffer CreateFramebuffer(GraphicsFrameBuffer.Attachment[] attachments, uint width, uint height) => new GLFrameBuffer(attachments, width, height);
    public override void UnbindFramebuffer()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        currentFramebuffer = null;
        currentReadFramebuffer = null;
        currentDrawFramebuffer = null;
    }

    public override void BindFramebuffer(GraphicsFrameBuffer frameBuffer, FBOTarget readFramebuffer)
    {
        FramebufferTarget target = readFramebuffer switch
        {
            FBOTarget.Read => FramebufferTarget.ReadFramebuffer,
            FBOTarget.Draw => FramebufferTarget.DrawFramebuffer,
            FBOTarget.Framebuffer => FramebufferTarget.Framebuffer,
            _ => throw new ArgumentOutOfRangeException(nameof(readFramebuffer), readFramebuffer, null),
        };
        GL.BindFramebuffer(target, (frameBuffer as GLFrameBuffer)!.Handle);
        //GL.DrawBuffers((uint)(frameBuffer as GLFrameBuffer).NumOfAttachments, GLFrameBuffer.buffers);

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

        Graphics.Device.Viewport(0, 0, frameBuffer.Width, frameBuffer.Height);
    }

    public override GraphicsFrameBuffer? GetCurrentFramebuffer(FBOTarget target = FBOTarget.Framebuffer)
    {
        return target switch
        {
            FBOTarget.Read => currentReadFramebuffer,
            FBOTarget.Draw => currentDrawFramebuffer,
            FBOTarget.Framebuffer => currentFramebuffer,
            _ => currentFramebuffer,
        };
    }
    public override void BlitFramebuffer(int srcX, int srcY, int srcWidth, int srcHeight, int destX, int destY, int destWidth, int destHeight, ClearFlags mask, BlitFilter filter)
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
    public override T ReadPixel<T>(int attachment, int x, int y, TextureImageFormat format)
    {
        GL.ReadBuffer((ReadBufferMode)((int)ReadBufferMode.ColorAttachment0 + attachment));
        GLTexture.GetTextureFormatEnums(format, out InternalFormat internalFormat, out PixelType pixelType, out PixelFormat pixelFormat);
        return GL.ReadPixels<T>(x, y, 1, 1, pixelFormat, pixelType);
    }

    #endregion

    #region Shaders

    public static Dictionary<ulong, int> cachedUniformLocations = [];
    public static Dictionary<ulong, int> cachedAttribLocations = [];

    public override GraphicsProgram CompileProgram(string fragment, string vertex, string geometry) => new GLProgram(fragment, vertex, geometry);
    public override void BindProgram(GraphicsProgram program) => (program as GLProgram)!.Use();

    public override int GetUniformLocation(GraphicsProgram program, string name)
    {
        ulong key = CombineKey(program.ID, name);
        if (cachedUniformLocations.TryGetValue(key, out int loc))
            return loc;

        BindProgram(program);
        int newLoc = GL.GetUniformLocation((program as GLProgram).Handle, name);
        cachedUniformLocations[key] = newLoc;
        return newLoc;
    }

    public override int GetAttribLocation(GraphicsProgram program, string name)
    {
        ulong key = CombineKey(program.ID, name);
        if (cachedAttribLocations.TryGetValue(key, out int loc))
            return loc;

        BindProgram(program);
        int newLoc = GL.GetAttribLocation((program as GLProgram).Handle, name);
        cachedAttribLocations[key] = newLoc;
        return newLoc;
    }

    public override void SetUniformF(GraphicsProgram program, string name, float value)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.Uniform1(loc, value);
    }

    public override void SetUniformI(GraphicsProgram program, string name, int value)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.Uniform1(loc, value);
    }

    public override void SetUniformV2(GraphicsProgram program, string name, Float2 value)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.Uniform2(loc, value); // Casts to System.Numerics.Vector2
    }

    public override void SetUniformV3(GraphicsProgram program, string name, Float3 value)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.Uniform3(loc, value); // Casts to System.Numerics.Vector3
    }

    public override void SetUniformV4(GraphicsProgram program, string name, Float4 value)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.Uniform4(loc, value); // Casts to System.Numerics.Vector4
    }

    public override void SetUniformMatrix(GraphicsProgram program, string name, uint count, bool transpose, in float matrix)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.UniformMatrix4(loc, count, transpose, in matrix);
    }

    public override void SetUniformTexture(GraphicsProgram program, string name, int slot, GraphicsTexture texture)
    {
        int loc = GetUniformLocation(program, name);
        if (loc == -1) return;

        BindProgram(program);
        GL.ActiveTexture((TextureUnit)((uint)TextureUnit.Texture0 + slot));
        (texture as GLTexture).Bind();
        GL.Uniform1(loc, slot);
    }

    #endregion

    #region Textures

    public override GraphicsTexture CreateTexture(TextureType type, TextureImageFormat format) => new GLTexture(type, format);
    public override void SetWrapS(GraphicsTexture texture, TextureWrap wrap) => (texture as GLTexture)!.SetWrapS(wrap);

    public override void SetWrapT(GraphicsTexture texture, TextureWrap wrap) => (texture as GLTexture)!.SetWrapT(wrap);
    public override void SetWrapR(GraphicsTexture texture, TextureWrap wrap) => (texture as GLTexture)!.SetWrapR(wrap);
    public override void SetTextureFilters(GraphicsTexture texture, TextureMin min, TextureMag mag) => (texture as GLTexture)!.SetTextureFilters(min, mag);
    public override void GenerateMipmap(GraphicsTexture texture) => (texture as GLTexture)!.GenerateMipmap();

    public override unsafe void GetTexImage(GraphicsTexture texture, int mip, void* data) => (texture as GLTexture)!.GetTexImage(mip, data);

    public override unsafe void TexImage2D(GraphicsTexture texture, int mip, uint width, uint height, int v2, void* data)
        => (texture as GLTexture)!.TexImage2D((texture as GLTexture).Target, mip, width, height, v2, data);
    public override unsafe void TexSubImage2D(GraphicsTexture texture, int mip, int x, int y, uint width, uint height, void* data)
        => (texture as GLTexture)!.TexSubImage2D((texture as GLTexture).Target, mip, x, y, width, height, data);

    public override unsafe void TexImage3D(GraphicsTexture texture, int level, uint width, uint height, uint depth, void* data)
        => (texture as GLTexture)!.TexImage3D((texture as GLTexture).Target, level, width, height, depth, data);
    public override unsafe void TexSubImage3D(GraphicsTexture texture, int level, int x, int y, int z, uint width, uint height, uint depth, void* data)
        => (texture as GLTexture)!.TexSubImage3D((texture as GLTexture).Target, level, x, y, z, width, height, depth, data);

    #endregion


    public override void Draw(Topology primitiveType, int v, uint count)
    {
        PrimitiveType mode = TopologyToGL(primitiveType);
        GL.DrawArrays(mode, v, count);
    }
    public override unsafe void DrawIndexed(Topology primitiveType, uint indexCount, bool index32bit, void* value)
    {
        PrimitiveType mode = TopologyToGL(primitiveType);
        GL.DrawElements(mode, indexCount, index32bit ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort, value);
    }

    public override unsafe void DrawIndexed(Topology primitiveType, uint indexCount, int startIndex, int baseVertex, bool index32bit)
    {
        PrimitiveType mode = TopologyToGL(primitiveType);

        DrawElementsType format = index32bit ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort;
        int formatSize = index32bit ? sizeof(uint) : sizeof(ushort);
        GL.DrawElementsBaseVertex(mode, indexCount, format, (void*)(startIndex * formatSize), baseVertex);
    }

    public override unsafe void DrawIndexedInstanced(Topology primitiveType, uint indexCount, uint instanceCount, bool index32bit)
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

    public override void Dispose()
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

    private DepthFunction DepthModeToGL(RasterizerState.DepthMode depthMode)
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

    private BlendingFactor BlendingToGL(RasterizerState.Blending blending)
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

    private BlendEquationModeEXT BlendModeToGL(RasterizerState.BlendMode blendMode)
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

    private TriangleFace CullFaceToGL(RasterizerState.PolyFace cullFace)
    {
        return cullFace switch
        {
            RasterizerState.PolyFace.Front => TriangleFace.Front,
            RasterizerState.PolyFace.Back => TriangleFace.Back,
            RasterizerState.PolyFace.FrontAndBack => TriangleFace.FrontAndBack,
            _ => throw new ArgumentOutOfRangeException(nameof(cullFace), cullFace, null),
        };
    }

    private FrontFaceDirection WindingToGL(RasterizerState.WindingOrder winding)
    {
        return winding switch
        {
            RasterizerState.WindingOrder.CW => FrontFaceDirection.CW,
            RasterizerState.WindingOrder.CCW => FrontFaceDirection.Ccw,
            _ => throw new ArgumentOutOfRangeException(nameof(winding), winding, null),
        };
    }

    #endregion

}
