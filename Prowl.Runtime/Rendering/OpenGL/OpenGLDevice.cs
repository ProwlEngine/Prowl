using Prowl.Runtime.Rendering;
using Silk.NET.Core.Native;
using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime.Rendering.OpenGL
{
    public sealed class OpenGLDevice : GraphicsDevice
    {
        public GL GL;
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

            Graphics.GLMajorVersion = GL.GetInteger(GLEnum.MajorVersion);
            Graphics.GLMinorVersion = GL.GetInteger(GLEnum.MinorVersion);

            // Textures
            Graphics.MaxSamples = GL.GetInteger(GLEnum.MaxSamples);
            Graphics.MaxTextureSize = GL.GetInteger(GLEnum.MaxTextureSize);
            Graphics.MaxCubeMapTextureSize = GL.GetInteger(GLEnum.MaxCubeMapTextureSize);
            Graphics.MaxArrayTextureLayers = GL.GetInteger(GLEnum.MaxArrayTextureLayers);

            Graphics.MaxRenderbufferSize = GL.GetInteger(GLEnum.MaxRenderbufferSize);
            Graphics.MaxFramebufferColorAttachments = GL.GetInteger(GLEnum.MaxColorAttachments);
            Graphics.MaxDrawBuffers = GL.GetInteger(GLEnum.MaxDrawBuffers);
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

        public override void ActiveTexture(TextureUnit textureUnit) => GL.ActiveTexture(textureUnit);

        public override void AttachShader(uint shaderProgram, uint vertexShader) => GL.AttachShader(shaderProgram, vertexShader);

        public override void BindBuffer(BufferTargetARB arrayBuffer, uint vbo) => GL.BindBuffer(arrayBuffer, vbo);

        public override void BindFramebuffer(FramebufferTarget readFramebuffer, uint fboId) => GL.BindFramebuffer(readFramebuffer, fboId);

        public override void BindTexture(TextureTarget type, uint handle) => GL.BindTexture(type, handle);

        public override void BindVertexArray(uint vertexArrayObject) => GL.BindVertexArray(vertexArrayObject);

        public override void BlendEquation(BlendEquationModeEXT equation) => GL.BlendEquation(equation);

        public override void BlendFunc(BlendingFactor srcAlpha, BlendingFactor oneMinusSrcAlpha) => GL.BlendFunc(srcAlpha, oneMinusSrcAlpha);

        public override void BlitFramebuffer(int v1, int v2, int width, int height, int v3, int v4, int v5, int v6, ClearBufferMask depthBufferBit, BlitFramebufferFilter nearest) => GL.BlitFramebuffer(v1, v2, width, height, v3, v4, v5, v6, depthBufferBit, nearest);

        public override void BufferData<T>(BufferTargetARB arrayBuffer, ReadOnlySpan<T> readOnlySpan, BufferUsageARB staticDraw) => GL.BufferData(arrayBuffer, readOnlySpan, staticDraw);

        public override GLEnum CheckFramebufferStatus(FramebufferTarget framebuffer) => GL.CheckFramebufferStatus(framebuffer);

        public override void Clear(uint v) => GL.Clear(v);

        public override void ClearColor(float r, float g, float b, float a) => GL.ClearColor(r, g, b, a);

        public override void CompileShader(uint vertexShader) => GL.CompileShader(vertexShader);

        public override uint CreateProgram() => GL.CreateProgram();

        public override uint CreateShader(ShaderType vertexShader) => GL.CreateShader(vertexShader);

        public override void CullFace(TriangleFace back) => GL.CullFace(back);

        public override void DeleteBuffer(uint vertexBuffer) => GL.DeleteBuffer(vertexBuffer);

        public override void DeleteFramebuffer(uint fboId) => GL.DeleteFramebuffer(fboId);

        public override void DeleteProgram(uint shaderProgram) => GL.DeleteProgram(shaderProgram);

        public override void DeleteShader(uint vertexShader) => GL.DeleteShader(vertexShader);

        public override void DeleteTexture(uint handle) => GL.DeleteTexture(handle);

        public override void DeleteVertexArray(uint vertexArrayObject) => GL.DeleteVertexArray(vertexArrayObject);

        public override void DepthFunc(DepthFunction lequal) => GL.DepthFunc(lequal);

        public override void DepthMask(bool v) => GL.DepthMask(v);

        public override void Disable(EnableCap cullFace) => GL.Disable(cullFace);

        public override void Dispose() => GL.Dispose();

        public override void DrawArrays(PrimitiveType primitiveType, int v, uint count) => GL.DrawArrays(primitiveType, v, count);

        public override void Enable(EnableCap depthTest) => GL.Enable(depthTest);

        public override void EnableVertexAttribArray(uint index) => GL.EnableVertexAttribArray(index);

        public override void Flush() => GL.Flush();

        public override void FramebufferTexture2D(FramebufferTarget framebuffer, FramebufferAttachment framebufferAttachment, TextureTarget type, uint handle, int v) => GL.FramebufferTexture2D(framebuffer, framebufferAttachment, type, handle, v);

        public override void FrontFace(FrontFaceDirection cW) => GL.FrontFace(cW);

        public override uint GenBuffer() => GL.GenBuffer();

        public override void GenerateMipmap(TextureTarget type) => GL.GenerateMipmap(type);

        public override uint GenFramebuffer() => GL.GenFramebuffer();

        public override uint GenTexture() => GL.GenTexture();

        public override uint GenVertexArray() => GL.GenVertexArray();

        public override void GetProgram(uint shaderProgram, ProgramPropertyARB linkStatus, out int statusCode) => GL.GetProgram(shaderProgram, linkStatus, out statusCode);

        public override void GetProgramInfoLog(uint shaderProgram, out string info) => GL.GetProgramInfoLog(shaderProgram, out info);

        public override void GetShader(uint fragmentShader, ShaderParameterName compileStatus, out int statusCode) => GL.GetShader(fragmentShader, compileStatus, out statusCode);

        public override void GetShaderInfoLog(uint vertexShader, out string info) => GL.GetShaderInfoLog(vertexShader, out info);

        public override unsafe void GetTexImage(TextureTarget face, int v, PixelFormat pixelFormat, PixelType pixelType, void* ptr) => GL.GetTexImage(face, v, pixelFormat, pixelType, ptr);

        public override int GetUniformLocation(uint shader, string name) => GL.GetUniformLocation(shader, name);

        public override void LinkProgram(uint shaderProgram) => GL.LinkProgram(shaderProgram);

        public override void ReadBuffer(ReadBufferMode colorAttachment5) => GL.ReadBuffer(colorAttachment5);

        public override T ReadPixels<T>(int x, int y, uint v1, uint v2, PixelFormat red, PixelType @float) => GL.ReadPixels<T>(x, y, v1, v2, red, @float);

        public override void ShaderSource(uint vertexShader, string vertexSource) => GL.ShaderSource(vertexShader, vertexSource);

        public override unsafe void TexImage2D(TextureTarget textureCubeMapPositiveX, int v1, int pixelInternalFormat, uint size1, uint size2, int v2, PixelFormat pixelFormat, PixelType pixelType, void* v3)
            => GL.TexImage2D(textureCubeMapPositiveX, v1, pixelInternalFormat, size1, size2, v2, pixelFormat, pixelType, v3);

        public override unsafe void TexImage3D(TextureTarget type, int v1, int pixelInternalFormat, uint width, uint height, uint depth, int v2, PixelFormat pixelFormat, PixelType pixelType, void* v3)
            => GL.TexImage3D(type, v1, pixelInternalFormat, width, height, depth, v2, pixelFormat, pixelType, v3);

        public override void TexParameter(TextureTarget type, TextureParameterName textureWrapS, int clampToEdge) => GL.TexParameter(type, textureWrapS, clampToEdge);

        public override unsafe void TexSubImage2D(TextureTarget face, int v, int rectX, int rectY, uint rectWidth, uint rectHeight, PixelFormat pixelFormat, PixelType pixelType, void* ptr)
            => GL.TexSubImage2D(face, v, rectX, rectY, rectWidth, rectHeight, pixelFormat, pixelType, ptr);

        public override unsafe void TexSubImage3D(TextureTarget type, int v, int rectX, int rectY, int rectZ, uint rectWidth, uint rectHeight, uint rectDepth, PixelFormat pixelFormat, PixelType pixelType, void* ptr)
            => GL.TexSubImage3D(type, v, rectX, rectY, rectZ, rectWidth, rectHeight, rectDepth, pixelFormat, pixelType, ptr);

        public override void Uniform1(int loc, float value) => GL.Uniform1(loc, value);
        public override void Uniform1(int loc, int value) => GL.Uniform1(loc, value);

        public override void Uniform2(int loc, Vector2 value) => GL.Uniform2(loc, value);

        public override void Uniform3(int loc, Vector3 value) => GL.Uniform3(loc, value);

        public override void Uniform4(int loc, Vector4 value) => GL.Uniform4(loc, value);

        public override void UniformMatrix4(int loc, uint length, bool v, in float m11) => GL.UniformMatrix4(loc, length, v, m11);

        public override void UseProgram(uint program) => GL.UseProgram(program);

        public override unsafe void VertexAttribIPointer(uint index, byte count, GLEnum type, uint size, void* offset) => GL.VertexAttribIPointer(index, count, type, size, offset);

        public override unsafe void VertexAttribPointer(uint index, byte count, GLEnum type, bool normalized, uint size, void* offset) => GL.VertexAttribPointer(index, count, type, normalized, size, offset);

        public override void Viewport(int v1, int v2, uint width, uint height) => GL.Viewport(v1, v2, width, height);

        public override unsafe void DrawElements(PrimitiveType triangles, uint indexCount, DrawElementsType drawElementsType, void* value) => GL.DrawElements(triangles, indexCount, drawElementsType, value);

        public override void DrawBuffers(uint count, GLEnum[] buffers) => GL.DrawBuffers(count, buffers);

        public override unsafe void ReadPixels(int x, int y, uint v1, uint v2, PixelFormat rgba, PixelType @float, float* ptr) => GL.ReadPixels(x, y, v1, v2, rgba, @float, ptr);

        public override void PixelStore(PixelStoreParameter unpackAlignment, int v) => GL.PixelStore(unpackAlignment, v);
    }
}
