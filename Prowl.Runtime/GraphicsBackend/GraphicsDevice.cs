using Prowl.Runtime.GraphicsBackend.Primitives;
using Prowl.Vector;

namespace Prowl.Runtime.GraphicsBackend
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

        public abstract uint GetBlockIndex(GraphicsProgram program, string blockName);

        public abstract void BindUniformBuffer(GraphicsProgram program, string blockName, GraphicsBuffer buffer);

        #endregion

        #region Vertex Arrays

        public abstract GraphicsVertexArray CreateVertexArray(VertexFormat format, GraphicsBuffer vertices, GraphicsBuffer? indices);
        public abstract void BindVertexArray(GraphicsVertexArray? vertexArrayObject);

        #endregion

        #region FrameBuffers

        public abstract GraphicsFrameBuffer CreateFramebuffer(GraphicsFrameBuffer.Attachment[] attachments, uint width, uint height);
        public abstract void UnbindFramebuffer();
        public abstract void BindFramebuffer(GraphicsFrameBuffer frameBuffer, FBOTarget readFramebuffer = FBOTarget.Framebuffer);
        public abstract void BlitFramebuffer(int v1, int v2, int width, int height, int v3, int v4, int v5, int v6, ClearFlags depthBufferBit, BlitFilter nearest);
        public abstract T ReadPixel<T>(int attachment, int x, int y, TextureImageFormat format) where T : unmanaged;

        #endregion

        #region Shaders

        public abstract GraphicsProgram CompileProgram(string fragment, string vertex, string geometry);
        public abstract void BindProgram(GraphicsProgram program);


        public abstract int GetUniformLocation(GraphicsProgram program, string name);
        public abstract int GetAttribLocation(GraphicsProgram program, string name);
        public abstract void SetUniformF(GraphicsProgram program, string name, float value);
        public abstract void SetUniformI(GraphicsProgram program, string name, int value);
        public abstract void SetUniformV2(GraphicsProgram program, string name, Float2 value);
        public abstract void SetUniformV3(GraphicsProgram program, string name, Float3 value);
        public abstract void SetUniformV4(GraphicsProgram program, string name, Float4 value);
        public void SetUniformMatrix(GraphicsProgram program, string name, bool transpose, Float4x4 matrix)
        {
            var fMat = matrix;
            SetUniformMatrix(program, name, 1, transpose, in fMat.c0.X);
        }
        public void SetUniformMatrix(GraphicsProgram program, string name, bool transpose, in float matrix) => SetUniformMatrix(program, name, 1, transpose, in matrix);
        public abstract void SetUniformMatrix(GraphicsProgram program, string name, uint count, bool transpose, in float matrix);
        public abstract void SetUniformTexture(GraphicsProgram program, string name, int slot, GraphicsTexture texture);


        #endregion

        #region Textures

        public abstract GraphicsTexture CreateTexture(TextureType type, TextureImageFormat format);
        public abstract void SetWrapS(GraphicsTexture texture, TextureWrap wrap);
        public abstract void SetWrapT(GraphicsTexture texture, TextureWrap wrap);
        public abstract void SetWrapR(GraphicsTexture texture, TextureWrap wrap);
        public abstract void SetTextureFilters(GraphicsTexture texture, TextureMin min, TextureMag mag);
        public abstract void GenerateMipmap(GraphicsTexture texture);

        public abstract unsafe void GetTexImage(GraphicsTexture texture, int mip, void* data);

        public abstract unsafe void TexImage2D(GraphicsTexture texture, int v1, uint size1, uint size2, int v2, void* v3);
        public abstract unsafe void TexSubImage2D(GraphicsTexture texture, int v, int rectX, int rectY, uint rectWidth, uint rectHeight, void* ptr);

        #endregion

        public void Draw(Topology primitiveType, uint count) => Draw(primitiveType, 0, count);
        public abstract void Draw(Topology primitiveType, int v, uint count);
        public unsafe void DrawIndexed(Topology primitiveType, uint indexCount, bool index32bit) => DrawIndexed(primitiveType, indexCount, index32bit, null);
        public abstract unsafe void DrawIndexed(Topology primitiveType, uint indexCount, bool index32bit, void* value);
        public abstract unsafe void DrawIndexed(Topology primitiveType, uint indexCount, int startIndex, int baseVertex, bool index32bit);
        public abstract unsafe void DrawIndexedInstanced(Topology primitiveType, uint indexCount, uint instanceCount, bool index32bit);

        public abstract void Dispose();
    }
}
