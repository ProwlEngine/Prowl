using Silk.NET.Core.Native;
using Silk.NET.OpenGL;
using Silk.NET.Vulkan;
using System;
using System.Reflection.Metadata;
using static VertexFormat;

namespace Prowl.Runtime.Rendering.OpenGL
{
    public sealed unsafe class GLVertexArray : GraphicsVertexArray
    {
        public uint Handle { get; private set; }

        public GLVertexArray(VertexFormat format, GraphicsBuffer vertices, GraphicsBuffer? indices)
        {
            Handle = GLDevice.GL.GenVertexArray();
            GLDevice.GL.BindVertexArray(Handle);

            BindFormat(format);

            GLDevice.GL.BindBuffer(BufferTargetARB.ArrayBuffer, (vertices as GLBuffer).Handle);
            if (indices != null)
                GLDevice.GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, (indices as GLBuffer).Handle);
        }

        void BindFormat(VertexFormat format)
        {
            for (var i = 0; i < format.Elements.Length; i++)
            {
                var element = format.Elements[i];
                var index = element.Semantic;
                GLDevice.GL.EnableVertexAttribArray(index);
                int offset = (int)element.Offset;
                unsafe
                {
                    if (element.Type == VertexType.Float)
                        GLDevice.GL.VertexAttribPointer(index, element.Count, (GLEnum)element.Type, element.Normalized, (uint)format.Size, (void*)offset);
                    else
                        GLDevice.GL.VertexAttribIPointer(index, element.Count, (GLEnum)element.Type, (uint)format.Size, (void*)offset);
                }
            }
        }

        public override bool IsDisposed { get; protected set; }

        public override void Dispose()
        {
            if (IsDisposed)
                return;

            GLDevice.GL.DeleteVertexArray(Handle);
            IsDisposed = true;
        }

        public override string ToString()
        {
            return Handle.ToString();
        }
    }

    public sealed unsafe class GLTexture : GraphicsTexture
    {
        public uint Handle { get; private set; }
        public override TextureTarget Type { get; protected set; }

        public GLTexture(TextureTarget type)
        {
            Handle = GLDevice.GL.GenTexture();
            Type = type;
        }
        
        private static uint? currentlyBound = null;
        public void Bind(bool force = true)
        {
            if (!force && currentlyBound == Handle)
                return;

            GLDevice.GL.BindTexture(Type, Handle);
            currentlyBound = Handle;
        }

        public void GenerateMipmap()
        {
            Bind(false);
            GLDevice.GL.GenerateMipmap(Type);
        }

        public void TexParameter(TextureParameterName textureWrapS, int clampToEdge)
        {
            Bind(false);
            GLDevice.GL.TexParameter(Type, textureWrapS, clampToEdge);
        }

        public void GetTexImage(int level, PixelFormat pixelFormat, PixelType pixelType, void* ptr)
        {
            Bind(false);
            GLDevice.GL.GetTexImage(Type, level, pixelFormat, pixelType, ptr);
        }

        public override bool IsDisposed { get; protected set; }

        public override void Dispose()
        {
            if (IsDisposed)
                return;

            if(currentlyBound == Handle)
                currentlyBound = null;

            GLDevice.GL.DeleteTexture(Handle);
            IsDisposed = true;
        }

        public override string ToString()
        {
            return Handle.ToString();
        }

        public void TexImage2D(TextureTarget type, int mip, int pixelInternalFormat, uint width, uint height, int v2, PixelFormat pixelFormat, PixelType pixelType, void* data)
        {
            Bind(false);
            GLDevice.GL.TexImage2D(type, mip, pixelInternalFormat, width, height, v2, pixelFormat, pixelType, data);
        }

        public void TexImage3D(TextureTarget type, int mip, int pixelInternalFormat, uint width, uint height, uint depth, int v2, PixelFormat pixelFormat, PixelType pixelType, void* data)
        {
            Bind(false);
            GLDevice.GL.TexImage3D(type, mip, pixelInternalFormat, width, height, depth, v2, pixelFormat, pixelType, data);
        }

        internal void TexSubImage2D(TextureTarget type, int mip, int x, int y, uint width, uint height, PixelFormat pixelFormat, PixelType pixelType, void* data)
        {
            Bind(false);
            GLDevice.GL.TexSubImage2D(type, mip, x, y, width, height, pixelFormat, pixelType, data);
        }

        internal void TexSubImage3D(TextureTarget type, int mip, int x, int y, int z, uint width, uint height, uint depth, PixelFormat pixelFormat, PixelType pixelType, void* data)
        {
            Bind(false);
            GLDevice.GL.TexSubImage3D(type, mip, x, y, z, width, height, depth, pixelFormat, pixelType, data);
        }

    }

    public sealed unsafe class GLFrameBuffer : GraphicsFrameBuffer
    {
        public uint Handle { get; private set; }

        static readonly GLEnum[] buffers =
        {
            GLEnum.ColorAttachment0,  GLEnum.ColorAttachment1,  GLEnum.ColorAttachment2,
            GLEnum.ColorAttachment3,  GLEnum.ColorAttachment4,  GLEnum.ColorAttachment5,
            GLEnum.ColorAttachment6,  GLEnum.ColorAttachment7,  GLEnum.ColorAttachment8,
            GLEnum.ColorAttachment9,  GLEnum.ColorAttachment10, GLEnum.ColorAttachment11,
            GLEnum.ColorAttachment12, GLEnum.ColorAttachment13, GLEnum.ColorAttachment14,
            GLEnum.ColorAttachment15, GLEnum.ColorAttachment16, GLEnum.ColorAttachment16,
            GLEnum.ColorAttachment17, GLEnum.ColorAttachment18, GLEnum.ColorAttachment19,
            GLEnum.ColorAttachment20, GLEnum.ColorAttachment21, GLEnum.ColorAttachment22,
            GLEnum.ColorAttachment23, GLEnum.ColorAttachment24, GLEnum.ColorAttachment25,
            GLEnum.ColorAttachment26, GLEnum.ColorAttachment27, GLEnum.ColorAttachment28,
            GLEnum.ColorAttachment29, GLEnum.ColorAttachment30, GLEnum.ColorAttachment31
        };

        public GLFrameBuffer(Attachment[] attachments)
        {
            int numTextures = attachments.Length;
            if (numTextures < 0 || numTextures > Graphics.MaxFramebufferColorAttachments)
                throw new Exception("[FrameBuffer] Invalid number of textures! [0-" + Graphics.MaxFramebufferColorAttachments + "]");

            // Generate FBO
            Handle = GLDevice.GL.GenFramebuffer();
            if (Handle <= 0)
                throw new Exception($"[FrameBuffer] Failed to generate new FrameBuffer.");

            GLDevice.GL.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);

            unsafe
            {
                // Generate textures
                if (numTextures > 0)
                {
                    for (int i = 0; i < numTextures; i++)
                    {
                        if (!attachments[i].isDepth)
                        {
                            //InternalTextures[i].SetTextureFilters(TextureMinFilter.Linear, TextureMagFilter.Linear);
                            //InternalTextures[i].SetWrapModes(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
                            GLDevice.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, attachments[i].texture.Type, (attachments[i].texture as GLTexture)!.Handle, 0);
                        }
                        else
                        {
                            GLDevice.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, (attachments[i].texture as GLTexture)!.Handle, 0);
                        }
                    }
                    GLDevice.GL.DrawBuffers((uint)numTextures, buffers);
                }

                if (GLDevice.GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
                    throw new Exception("RenderTexture: [ID {fboId}] RenderTexture object creation failed.");

                // Unbind FBO
                GLDevice.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }
        }

        public override bool IsDisposed { get; protected set; }

        public override void Dispose()
        {
            if (IsDisposed)
                return;

            GLDevice.GL.DeleteFramebuffer(Handle);
            IsDisposed = true;
        }
        public override string ToString()
        {
            return Handle.ToString();
        }
    }

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

        private bool doCull = true;
        private RasterizerState.PolyFace cullFace = RasterizerState.PolyFace.Back;

        private RasterizerState.WindingOrder winding = RasterizerState.WindingOrder.CW;

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
            var msg = SilkMarshal.PtrToString(message, NativeStringEncoding.UTF8);
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
            if (depthTest != state.depthTest || force)
            {
                if (state.depthTest)
                    GL.Enable(EnableCap.DepthTest);
                else
                    GL.Disable(EnableCap.DepthTest);
                depthTest = state.depthTest;
            }

            if (depthWrite != state.depthWrite || force)
            {
                GL.DepthMask(state.depthWrite);
                depthWrite = state.depthWrite;
            }

            if (depthMode != state.depthMode || force)
            {
                GL.DepthFunc(DepthModeToGL(state.depthMode));
                depthMode = state.depthMode;
            }

            if (doBlend != state.doBlend || force)
            {
                if (state.doBlend)
                    GL.Enable(EnableCap.Blend);
                else
                    GL.Disable(EnableCap.Blend);
                doBlend = state.doBlend;
            }

            if (blendSrc != state.blendSrc || blendDst != state.blendDst || force)
            {
                GL.BlendFunc(BlendingToGL(state.blendSrc), BlendingToGL(state.blendDst));
                blendSrc = state.blendSrc;
                blendDst = state.blendDst;
            }

            if (blendEquation != state.blendMode || force)
            {
                GL.BlendEquation(BlendModeToGL(state.blendMode));
                blendEquation = state.blendMode;
            }

            if (doCull != state.doCull || force)
            {
                if (state.doCull)
                    GL.Enable(EnableCap.CullFace);
                else
                    GL.Disable(EnableCap.CullFace);
                doCull = state.doCull;
            }

            if (cullFace != state.cullFace || force)
            {
                GL.CullFace(CullFaceToGL(state.cullFace));
                cullFace = state.cullFace;
            }

            if (winding != state.winding || force)
            {
                GL.FrontFace(WindingToGL(state.winding));
                winding = state.winding;
            }
        }

        public override RasterizerState GetState()
        {
            return new RasterizerState {
                depthTest = depthTest,
                depthWrite = depthWrite,
                depthMode = depthMode,
                doBlend = doBlend,
                blendSrc = blendSrc,
                blendDst = blendDst,
                blendMode = blendEquation,
                doCull = doCull,
                cullFace = cullFace
            };
        }

        public override GraphicsProgram CurrentProgram => GLProgram.currentProgram;

        #region Buffers

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

        #endregion

        #region Vertex Arrays

        public override GraphicsVertexArray CreateVertexArray(VertexFormat format, GraphicsBuffer vertices, GraphicsBuffer? indices)
        {
            return new GLVertexArray(format, vertices, indices);
        }

        public override void BindVertexArray(GraphicsVertexArray? vertexArrayObject)
        {
            GL.BindVertexArray((vertexArrayObject as GLVertexArray)?.Handle ?? 0);
            //if (vertexArrayObject == null)
            //{
            //    GLDevice.GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
            //    GLDevice.GL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            //}
        }


        #endregion


        #region Frame Buffers

        public override GraphicsFrameBuffer CreateFramebuffer(GraphicsFrameBuffer.Attachment[] attachments) => new GLFrameBuffer(attachments);
        public override void UnbindFramebuffer() => GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        public override void BindFramebuffer(GraphicsFrameBuffer frameBuffer, FramebufferTarget readFramebuffer) => GL.BindFramebuffer(readFramebuffer, (frameBuffer as GLFrameBuffer)!.Handle);
        public override void BlitFramebuffer(int v1, int v2, int width, int height, int v3, int v4, int v5, int v6, ClearBufferMask depthBufferBit, BlitFramebufferFilter nearest) => GL.BlitFramebuffer(v1, v2, width, height, v3, v4, v5, v6, depthBufferBit, nearest);
        public override T ReadPixel<T>(int attachment, int x, int y, PixelFormat red, PixelType @float)
        {
            GL.ReadBuffer((ReadBufferMode)attachment);
            return GL.ReadPixels<T>(x, y, 1, 1, red, @float);
        }

        #endregion

        #region Shaders

        public override GraphicsProgram CompileProgram(string fragment, string vertex, string geometry) => new GLProgram(fragment, vertex, geometry);
        public override void BindProgram(GraphicsProgram program) => (program as GLProgram)!.Use();

        public override int GetUniformLocation(GraphicsProgram program, string name)
        {
            BindProgram(program);
            return GL.GetUniformLocation((program as GLProgram).Handle, name);
        }

        public override int GetAttribLocation(GraphicsProgram program, string name)
        {
            BindProgram(program);
            return GL.GetAttribLocation((program as GLProgram).Handle, name);
        }

        public override void SetUniformF(GraphicsProgram program, int loc, float value)
        {
            BindProgram(program);
            GL.Uniform1(loc, value);
        }

        public override void SetUniformI(GraphicsProgram program, int loc, int value)
        {
            BindProgram(program);
            GL.Uniform1(loc, value);
        }

        public override void SetUniformV2(GraphicsProgram program, int loc, Vector2 value)
        {
            BindProgram(program);
            GL.Uniform2(loc, value);
        }

        public override void SetUniformV3(GraphicsProgram program, int loc, Vector3 value)
        {
            BindProgram(program);
            GL.Uniform3(loc, value);
        }

        public override void SetUniformV4(GraphicsProgram program, int loc, Vector4 value)
        {
            BindProgram(program);
            GL.Uniform4(loc, value);
        }

        public override void SetUniformMatrix(GraphicsProgram program, int loc, uint length, bool v, in float m11)
        {
            BindProgram(program);
            GL.UniformMatrix4(loc, length, v, m11);
        }

        public override void SetUniformTexture(GraphicsProgram program, int loc, int slot, GraphicsTexture texture)
        {
            BindProgram(program);
            GL.ActiveTexture((TextureUnit)((uint)TextureUnit.Texture0 + slot));
            GL.BindTexture((texture as GLTexture).Type, (texture as GLTexture).Handle);
            GL.Uniform1(loc, slot);
        }

        #endregion

        #region Textures

        public override GraphicsTexture CreateTexture(TextureTarget type) => new GLTexture(type);
        public override void TexParameter(GraphicsTexture texture, TextureParameterName textureWrapS, int clampToEdge) => (texture as GLTexture)!.TexParameter(textureWrapS, clampToEdge);
        public override void GenerateMipmap(GraphicsTexture texture) => (texture as GLTexture)!.GenerateMipmap();

        public override unsafe void GetTexImage(GraphicsTexture texture, int mip, PixelFormat pixelFormat, PixelType pixelType, void* data) => (texture as GLTexture)!.GetTexImage(mip, pixelFormat, pixelType, data);

        public override unsafe void TexImage2D(GraphicsTexture texture, int mip, int pixelInternalFormat, uint width, uint height, int v2, PixelFormat pixelFormat, PixelType pixelType, void* data)
            => (texture as GLTexture)!.TexImage2D((texture as GLTexture).Type, mip, pixelInternalFormat, width, height, v2, pixelFormat, pixelType, data);
        public override unsafe void TexImage2D(GraphicsTexture texture, TextureCubemap.CubemapFace face, int mip, int pixelInternalFormat, uint width, uint height, int v2, PixelFormat pixelFormat, PixelType pixelType, void* data)
            => (texture as GLTexture)!.TexImage2D((TextureTarget)face, mip, pixelInternalFormat, width, height, v2, pixelFormat, pixelType, data);
        public override unsafe void TexImage3D(GraphicsTexture texture, int mip, int pixelInternalFormat, uint width, uint height, uint depth, int v2, PixelFormat pixelFormat, PixelType pixelType, void* data)
            => (texture as GLTexture)!.TexImage3D((texture as GLTexture).Type, mip, pixelInternalFormat, width, height, depth, v2, pixelFormat, pixelType, data);
        public override unsafe void TexSubImage2D(GraphicsTexture texture, int mip, int x, int y, uint width, uint height, PixelFormat pixelFormat, PixelType pixelType, void* data)
            => (texture as GLTexture)!.TexSubImage2D((texture as GLTexture).Type, mip, x, y, width, height, pixelFormat, pixelType, data);
        public override unsafe void TexSubImage2D(GraphicsTexture texture, TextureCubemap.CubemapFace face, int mip, int x, int y, uint width, uint height, PixelFormat pixelFormat, PixelType pixelType, void* data)
            => (texture as GLTexture)!.TexSubImage2D((TextureTarget)face, mip, x, y, width, height, pixelFormat, pixelType, data);
        public override unsafe void TexSubImage3D(GraphicsTexture texture, int mip, int x, int y, int z, uint width, uint height, uint depth, PixelFormat pixelFormat, PixelType pixelType, void* data)
            => (texture as GLTexture)!.TexSubImage3D((texture as GLTexture).Type, mip, x, y, z, width, height, depth, pixelFormat, pixelType, data);

        #endregion


        public override void DrawArrays(PrimitiveType primitiveType, int v, uint count) => GL.DrawArrays(primitiveType, v, count);
        public override unsafe void DrawElements(PrimitiveType triangles, uint indexCount, DrawElementsType drawElementsType, void* value) => GL.DrawElements(triangles, indexCount, drawElementsType, value);

        public override void Dispose()
        {
            GL.Dispose();
        }

        #region Private

        private DepthFunction DepthModeToGL(RasterizerState.DepthMode depthMode)
        {
            return depthMode switch {
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
            return blending switch {
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
            return blendMode switch {
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
            return cullFace switch {
                RasterizerState.PolyFace.Front => TriangleFace.Front,
                RasterizerState.PolyFace.Back => TriangleFace.Back,
                RasterizerState.PolyFace.FrontAndBack => TriangleFace.FrontAndBack,
                _ => throw new ArgumentOutOfRangeException(nameof(cullFace), cullFace, null),
            };
        }

        private FrontFaceDirection WindingToGL(RasterizerState.WindingOrder winding)
        {
            return winding switch {
                RasterizerState.WindingOrder.CW => FrontFaceDirection.CW,
                RasterizerState.WindingOrder.CCW => FrontFaceDirection.Ccw,
                _ => throw new ArgumentOutOfRangeException(nameof(winding), winding, null),
            };
        }

        #endregion

    }
}
