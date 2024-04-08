using Silk.NET.Core.Native;
using Silk.NET.OpenGL;
using System;
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

        public override uint GenFramebuffer() => GL.GenFramebuffer();
        public override GLEnum CheckFramebufferStatus(FramebufferTarget framebuffer) => GL.CheckFramebufferStatus(framebuffer);
        public override void BindFramebuffer(FramebufferTarget readFramebuffer, uint fboId) => GL.BindFramebuffer(readFramebuffer, fboId);
        public override void DrawBuffers(uint count, GLEnum[] buffers) => GL.DrawBuffers(count, buffers);
        public override void FramebufferTexture2D(FramebufferTarget framebuffer, FramebufferAttachment framebufferAttachment, TextureTarget type, GraphicsTexture handle, int v)
            => GL.FramebufferTexture2D(framebuffer, framebufferAttachment, type, (handle as GLTexture)!.Handle, v);
        public override void BlitFramebuffer(int v1, int v2, int width, int height, int v3, int v4, int v5, int v6, ClearBufferMask depthBufferBit, BlitFramebufferFilter nearest)
            => GL.BlitFramebuffer(v1, v2, width, height, v3, v4, v5, v6, depthBufferBit, nearest);
        public override void ReadBuffer(ReadBufferMode colorAttachment5) => GL.ReadBuffer(colorAttachment5);
        public override void DeleteFramebuffer(uint fboId) => GL.DeleteFramebuffer(fboId);
        public override T ReadPixels<T>(int x, int y, uint v1, uint v2, PixelFormat red, PixelType @float) => GL.ReadPixels<T>(x, y, v1, v2, red, @float);
        public override unsafe void ReadPixels(int x, int y, uint v1, uint v2, PixelFormat rgba, PixelType @float, float* ptr) => GL.ReadPixels(x, y, v1, v2, rgba, @float, ptr);

        #endregion

        #region Shaders

        public override void AttachShader(uint shaderProgram, uint vertexShader) => GL.AttachShader(shaderProgram, vertexShader);
        public override void CompileShader(uint vertexShader) => GL.CompileShader(vertexShader);
        public override uint CreateProgram() => GL.CreateProgram();
        public override uint CreateShader(ShaderType vertexShader) => GL.CreateShader(vertexShader);
        public override void DeleteProgram(uint shaderProgram) => GL.DeleteProgram(shaderProgram);
        public override void DeleteShader(uint vertexShader) => GL.DeleteShader(vertexShader);
        public override void GetProgram(uint shaderProgram, ProgramPropertyARB linkStatus, out int statusCode) => GL.GetProgram(shaderProgram, linkStatus, out statusCode);
        public override void GetProgramInfoLog(uint shaderProgram, out string info) => GL.GetProgramInfoLog(shaderProgram, out info);
        public override void GetShader(uint fragmentShader, ShaderParameterName compileStatus, out int statusCode) => GL.GetShader(fragmentShader, compileStatus, out statusCode);
        public override void GetShaderInfoLog(uint vertexShader, out string info) => GL.GetShaderInfoLog(vertexShader, out info);
        public override void ActiveTexture(TextureUnit textureUnit) => GL.ActiveTexture(textureUnit);
        public override void SetUniformF(int loc, float value) => GL.Uniform1(loc, value);
        public override void SetUniformI(int loc, int value) => GL.Uniform1(loc, value);
        public override void SetUniformV2(int loc, Vector2 value) => GL.Uniform2(loc, value);
        public override void SetUniformV3(int loc, Vector3 value) => GL.Uniform3(loc, value);
        public override void SetUniformV4(int loc, Vector4 value) => GL.Uniform4(loc, value);
        public override void SetUniformMatrix(int loc, uint length, bool v, in float m11) => GL.UniformMatrix4(loc, length, v, m11);
        public override void UseProgram(uint program) => GL.UseProgram(program);
        public override void ShaderSource(uint vertexShader, string vertexSource) => GL.ShaderSource(vertexShader, vertexSource);
        public override int GetUniformLocation(uint shader, string name) => GL.GetUniformLocation(shader, name);
        public override void LinkProgram(uint shaderProgram) => GL.LinkProgram(shaderProgram);

        #endregion

        #region Textures

        public override GraphicsTexture CreateTexture(TextureTarget type) => new GLTexture(type);
        public override void BindTexture(GraphicsTexture texture) => (texture as GLTexture)!.Bind();
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

        public override void Flush() => GL.Flush();
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
