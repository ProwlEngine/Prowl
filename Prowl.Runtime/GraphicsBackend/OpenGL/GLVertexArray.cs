using Prowl.Runtime.GraphicsBackend;
using Silk.NET.OpenGL;

using static Prowl.Runtime.GraphicsBackend.VertexFormat;

namespace Prowl.Runtime.GraphicsBackend.OpenGL
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
                int offset = element.Offset;
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
}
