using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime.Rendering
{
    public abstract class GraphicsDevice
    {
        public abstract void Initialize(bool debug);

        public abstract void UseProgram(uint program);
        public abstract void Viewport(int v1, int v2, uint width, uint height);
        public abstract void ClearColor(float r, float g, float b, float a);
        public abstract void Clear(uint v);
        public abstract void DepthFunc(DepthFunction lequal);
        public abstract void Enable(EnableCap depthTest);
        public abstract void FrontFace(FrontFaceDirection cW);
        public abstract void BindVertexArray(uint vertexArrayObject);
        public abstract void DepthMask(bool v);
        public abstract void BindFramebuffer(FramebufferTarget readFramebuffer, uint fboId);
        public abstract void BlitFramebuffer(int v1, int v2, int width, int height, int v3, int v4, int v5, int v6, ClearBufferMask depthBufferBit, BlitFramebufferFilter nearest);
        public abstract void Dispose();
        public abstract void BlendFunc(BlendingFactor srcAlpha, BlendingFactor oneMinusSrcAlpha);
        public abstract void BlendEquation(BlendEquationModeEXT equation);
        public abstract void CullFace(TriangleFace back);
        public abstract void Disable(EnableCap cullFace);
        public abstract uint GenVertexArray();
        public abstract void BindBuffer(BufferTargetARB arrayBuffer, uint vbo);
        public abstract uint GenBuffer();
        public abstract void BindTexture(TextureTarget type, uint handle);
        public abstract void TexParameter(TextureTarget type, TextureParameterName textureWrapS, int clampToEdge);
        public abstract unsafe void TexImage2D(TextureTarget textureCubeMapPositiveX, int v1, int pixelInternalFormat, uint size1, uint size2, int v2, PixelFormat pixelFormat, PixelType pixelType, void* v3);
        public abstract unsafe void TexSubImage2D(TextureTarget face, int v, int rectX, int rectY, uint rectWidth, uint rectHeight, PixelFormat pixelFormat, PixelType pixelType, void* ptr);
        public abstract unsafe void GetTexImage(TextureTarget face, int v, PixelFormat pixelFormat, PixelType pixelType, void* ptr);
        public abstract unsafe void TexSubImage3D(TextureTarget type, int v, int rectX, int rectY, int rectZ, uint rectWidth, uint rectHeight, uint rectDepth, PixelFormat pixelFormat, PixelType pixelType, void* ptr);
        public abstract unsafe void TexImage3D(TextureTarget type, int v1, int pixelInternalFormat, uint width, uint height, uint depth, int v2, PixelFormat pixelFormat, PixelType pixelType, void* v3);
        public abstract uint GenTexture();
        public abstract void GenerateMipmap(TextureTarget type);
        public abstract void DeleteTexture(uint handle);
        public abstract uint CreateProgram();
        public abstract uint CreateShader(ShaderType vertexShader);
        public abstract void ShaderSource(uint vertexShader, string vertexSource);
        public abstract void CompileShader(uint vertexShader);
        public abstract void GetShaderInfoLog(uint vertexShader, out string info);
        public abstract void DeleteShader(uint vertexShader);
        public abstract void DeleteProgram(uint shaderProgram);
        public abstract void AttachShader(uint shaderProgram, uint vertexShader);
        public abstract void GetShader(uint fragmentShader, ShaderParameterName compileStatus, out int statusCode);
        public abstract void LinkProgram(uint shaderProgram);
        public abstract void GetProgramInfoLog(uint shaderProgram, out string info);
        public abstract void Flush();
        public abstract void GetProgram(uint shaderProgram, ProgramPropertyARB linkStatus, out int statusCode);
        public abstract uint GenFramebuffer();
        public abstract void FramebufferTexture2D(FramebufferTarget framebuffer, FramebufferAttachment framebufferAttachment, TextureTarget type, uint handle, int v);
        public abstract GLEnum CheckFramebufferStatus(FramebufferTarget framebuffer);
        public abstract void DeleteFramebuffer(uint fboId);
        public abstract void BufferData<T>(BufferTargetARB arrayBuffer, ReadOnlySpan<T> readOnlySpan, BufferUsageARB staticDraw) where T : unmanaged;
        public abstract void DeleteVertexArray(uint vertexArrayObject);
        public abstract void DeleteBuffer(uint vertexBuffer);
        public abstract void EnableVertexAttribArray(uint index);
        public abstract unsafe void VertexAttribPointer(uint index, byte count, GLEnum type, bool normalized, uint size, void* offset);
        public abstract unsafe void VertexAttribIPointer(uint index, byte count, GLEnum type, uint size, void* offset);
        public abstract void ReadBuffer(ReadBufferMode colorAttachment5);
        public abstract T ReadPixels<T>(int x, int y, uint v1, uint v2, PixelFormat red, PixelType @float) where T : unmanaged;
        public abstract int GetUniformLocation(uint shader, string name);
        public abstract void Uniform1(int loc, float value);
        public abstract void Uniform1(int loc, int value);
        public abstract void Uniform2(int loc, Vector2 value);
        public abstract void Uniform3(int loc, Vector3 value);
        public abstract void Uniform4(int loc, Vector4 value);
        public abstract void UniformMatrix4(int loc, uint length, bool v, in float m11);
        public abstract void ActiveTexture(TextureUnit textureUnit);
        public abstract void DrawArrays(PrimitiveType primitiveType, int v, uint count);
        public abstract unsafe void DrawElements(PrimitiveType triangles, uint indexCount, DrawElementsType drawElementsType, void* value);
        public abstract void DrawBuffers(uint count, GLEnum[] buffers);
        public abstract unsafe void ReadPixels(int x, int y, uint v1, uint v2, PixelFormat rgba, PixelType @float, float* ptr);
        public abstract void PixelStore(PixelStoreParameter unpackAlignment, int v);
    }
}
