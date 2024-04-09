using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime.Rendering
{
    public enum BufferType { VertexBuffer, ElementsBuffer, UniformBuffer, StructuredBuffer, Count }

    public abstract class GraphicsBuffer : IDisposable
    {
        public abstract bool IsDisposed { get; protected set; }
        public abstract void Dispose();
    }

    public struct RasterizerState
    {
        public enum DepthMode { Never, Less, Equal, Lequal, Greater, Notequal, Gequal, Always }
        public enum Blending { Zero, One, SrcColor, OneMinusSrcColor, DstColor, OneMinusDstColor, SrcAlpha, OneMinusSrcAlpha, DstAlpha, OneMinusDstAlpha, ConstantColor, OneMinusConstantColor, ConstantAlpha, OneMinusConstantAlpha, SrcAlphaSaturate, Src1Color, OneMinusSrc1Color, Src1Alpha, OneMinusSrc1Alpha }
        public enum BlendMode { Add, Subtract, ReverseSubtract, Min, Max }
        public enum PolyFace { Front, Back, FrontAndBack }
        public enum WindingOrder { CW, CCW }

        public bool depthTest = true;
        public bool depthWrite = true;
        public DepthMode depthMode = DepthMode.Lequal;

        public bool doBlend = true;
        public Blending blendSrc = Blending.SrcAlpha;
        public Blending blendDst = Blending.OneMinusSrcAlpha;
        public BlendMode blendMode = BlendMode.Add;

        public bool doCull = true;
        public PolyFace cullFace = PolyFace.Back;

        public WindingOrder winding = WindingOrder.CW;

        public RasterizerState() { }
    }

    public abstract class GraphicsVertexArray : IDisposable
    {
        public abstract bool IsDisposed { get; protected set; }
        public abstract void Dispose();
    }

    public abstract class GraphicsTexture : IDisposable
    {
        public abstract TextureTarget Type { get; protected set; }
        public abstract bool IsDisposed { get; protected set; }
        public abstract void Dispose();
    }

    public abstract class GraphicsDevice
    {
        public abstract void Initialize(bool debug);

        public abstract void Viewport(int x, int y, uint width, uint height);
        public abstract void Clear(float r, float g, float b, float a, ClearFlags v);
        public abstract void SetState(RasterizerState state, bool force = false);
        public abstract RasterizerState GetState();

        #region Buffers

        /// <summary> Create a graphics buffer with the given type and data. </summary>
        public abstract GraphicsBuffer CreateBuffer<T>(BufferType bufferType, T[] data, bool dynamic = false) where T : unmanaged;

        /// <summary> Set the data of the given buffer with the given data. </summary>
        public abstract void SetBuffer<T>(GraphicsBuffer buffer, T[] data, bool dynamic = false);

        /// <summary> Update the given buffer with the given data at the given offset in bytes. </summary>
        public abstract void UpdateBuffer<T>(GraphicsBuffer buffer, uint offsetInBytes, T[] data) where T : unmanaged;

        public abstract void BindBuffer(GraphicsBuffer buffer);

        #endregion

        #region Vertex Arrays

        public abstract GraphicsVertexArray CreateVertexArray(VertexFormat format, GraphicsBuffer vertices, GraphicsBuffer? indices);
        public abstract void BindVertexArray(GraphicsVertexArray? vertexArrayObject);

        #endregion

        #region FrameBuffers

        //public abstract GraphicsFrameBuffer CreateFrameBuffer();
        //public abstract void BindFrameBuffer(GraphicsFrameBuffer frameBuffer);
        //public abstract void ReadFrameBuffer(int attachment);
        //public abstract void BlitFramebuffer(int v1, int v2, int width, int height, int v3, int v4, int v5, int v6, ClearBufferMask depthBufferBit, BlitFramebufferFilter nearest);
        //public abstract T ReadPixels<T>(int x, int y, uint v1, uint v2, PixelFormat red, PixelType @float) where T : unmanaged;
        //public abstract unsafe void ReadPixels(int x, int y, uint v1, uint v2, PixelFormat rgba, PixelType @float, float* ptr);

        public abstract uint GenFramebuffer();
        public abstract void DrawBuffers(uint count, GLEnum[] buffers);
        public abstract void ReadBuffer(ReadBufferMode colorAttachment5);
        public abstract void BindFramebuffer(FramebufferTarget readFramebuffer, uint fboId);
        public abstract void BlitFramebuffer(int v1, int v2, int width, int height, int v3, int v4, int v5, int v6, ClearBufferMask depthBufferBit, BlitFramebufferFilter nearest);
        public abstract void FramebufferTexture2D(FramebufferTarget framebuffer, FramebufferAttachment framebufferAttachment, TextureTarget type, GraphicsTexture handle, int v);
        public abstract GLEnum CheckFramebufferStatus(FramebufferTarget framebuffer);
        public abstract void DeleteFramebuffer(uint fboId);
        public abstract T ReadPixels<T>(int x, int y, uint v1, uint v2, PixelFormat red, PixelType @float) where T : unmanaged;
        public abstract unsafe void ReadPixels(int x, int y, uint v1, uint v2, PixelFormat rgba, PixelType @float, float* ptr);

        #endregion

        #region Shaders

        public abstract uint CreateProgram();
        public abstract void UseProgram(uint program);
        public abstract void AttachShader(uint shaderProgram, uint vertexShader);
        public abstract void LinkProgram(uint shaderProgram);
        public abstract void GetProgramInfoLog(uint shaderProgram, out string info);
        public abstract void GetProgram(uint shaderProgram, ProgramPropertyARB linkStatus, out int statusCode);

        public abstract uint CreateShader(ShaderType vertexShader);
        public abstract void ShaderSource(uint vertexShader, string vertexSource);
        public abstract void CompileShader(uint vertexShader);
        public abstract void GetShaderInfoLog(uint vertexShader, out string info);
        public abstract void GetShader(uint fragmentShader, ShaderParameterName compileStatus, out int statusCode);

        public abstract void DeleteShader(uint vertexShader);
        public abstract void DeleteProgram(uint shaderProgram);

        public abstract void ActiveTexture(TextureUnit textureUnit);
        public abstract int GetUniformLocation(uint shader, string name);
        public abstract void SetUniformF(int loc, float value);
        public abstract void SetUniformI(int loc, int value);
        public abstract void SetUniformV2(int loc, Vector2 value);
        public abstract void SetUniformV3(int loc, Vector3 value);
        public abstract void SetUniformV4(int loc, Vector4 value);
        public abstract void SetUniformMatrix(int loc, uint length, bool v, in float m11);


        #endregion

        #region Textures

        public abstract GraphicsTexture CreateTexture(TextureTarget type);
        public abstract void BindTexture(GraphicsTexture texture);
        public abstract void TexParameter(GraphicsTexture texture, TextureParameterName textureWrapS, int clampToEdge);
        public abstract void GenerateMipmap(GraphicsTexture texture);

        public abstract unsafe void GetTexImage(GraphicsTexture texture, int mip, PixelFormat pixelFormat, PixelType pixelType, void* data);

        public abstract unsafe void TexImage2D(GraphicsTexture texture, int v1, int pixelInternalFormat, uint size1, uint size2, int v2, PixelFormat pixelFormat, PixelType pixelType, void* v3);
        public abstract unsafe void TexImage2D(GraphicsTexture texture, TextureCubemap.CubemapFace face, int v1, int pixelInternalFormat, uint size1, uint size2, int v2, PixelFormat pixelFormat, PixelType pixelType, void* v3);
        public abstract unsafe void TexSubImage2D(GraphicsTexture texture, int v, int rectX, int rectY, uint rectWidth, uint rectHeight, PixelFormat pixelFormat, PixelType pixelType, void* ptr);
        public abstract unsafe void TexSubImage2D(GraphicsTexture texture, TextureCubemap.CubemapFace face, int v, int rectX, int rectY, uint rectWidth, uint rectHeight, PixelFormat pixelFormat, PixelType pixelType, void* ptr);
        public abstract unsafe void TexSubImage3D(GraphicsTexture texture, int v, int rectX, int rectY, int rectZ, uint rectWidth, uint rectHeight, uint rectDepth, PixelFormat pixelFormat, PixelType pixelType, void* ptr);
        public abstract unsafe void TexImage3D(GraphicsTexture texture, int v1, int pixelInternalFormat, uint width, uint height, uint depth, int v2, PixelFormat pixelFormat, PixelType pixelType, void* v3);

        #endregion

        public abstract void DrawArrays(PrimitiveType primitiveType, int v, uint count);
        public abstract unsafe void DrawElements(PrimitiveType triangles, uint indexCount, DrawElementsType drawElementsType, void* value);

        public abstract void Flush();
        public abstract void Dispose();
    }
}
