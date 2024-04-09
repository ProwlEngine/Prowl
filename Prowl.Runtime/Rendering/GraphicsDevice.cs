using Silk.NET.OpenGL;

namespace Prowl.Runtime.Rendering
{

    public abstract class GraphicsDevice
    {
        public abstract void Initialize(bool debug);

        public abstract void Viewport(int x, int y, uint width, uint height);
        public abstract void Clear(float r, float g, float b, float a, ClearFlags v);
        public abstract void SetState(RasterizerState state, bool force = false);
        public abstract RasterizerState GetState();

        public abstract GraphicsProgram CurrentProgram { get; }

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

        public abstract GraphicsFrameBuffer CreateFramebuffer(GraphicsFrameBuffer.Attachment[] attachments);
        public abstract void UnbindFramebuffer();
        public abstract void BindFramebuffer(GraphicsFrameBuffer frameBuffer, FramebufferTarget readFramebuffer = FramebufferTarget.Framebuffer);
        public abstract void BlitFramebuffer(int v1, int v2, int width, int height, int v3, int v4, int v5, int v6, ClearBufferMask depthBufferBit, BlitFramebufferFilter nearest);
        public abstract T ReadPixel<T>(int attachment, int x, int y, PixelFormat red, PixelType @float) where T : unmanaged;

        #endregion

        #region Shaders

        public abstract GraphicsProgram CompileProgram(string fragment, string vertex, string geometry);
        public abstract void BindProgram(GraphicsProgram program);


        public abstract int GetUniformLocation(GraphicsProgram program, string name);
        public abstract int GetAttribLocation(GraphicsProgram program, string name);
        public abstract void SetUniformF(GraphicsProgram program, int loc, float value);
        public abstract void SetUniformI(GraphicsProgram program, int loc, int value);
        public abstract void SetUniformV2(GraphicsProgram program, int loc, Vector2 value);
        public abstract void SetUniformV3(GraphicsProgram program, int loc, Vector3 value);
        public abstract void SetUniformV4(GraphicsProgram program, int loc, Vector4 value);
        public abstract void SetUniformMatrix(GraphicsProgram program, int loc, uint length, bool v, in float m11);
        public abstract void SetUniformTexture(GraphicsProgram program, int loc, int slot, GraphicsTexture texture);


        #endregion

        #region Textures

        public abstract GraphicsTexture CreateTexture(TextureTarget type);
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

        public abstract void Dispose();
    }
}
