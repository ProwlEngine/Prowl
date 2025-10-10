using Prowl.Runtime.GraphicsBackend;
using Prowl.Runtime.GraphicsBackend.Primitives;
using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime.GraphicsBackend.OpenGL
{
    internal sealed class GLBuffer : GraphicsBuffer
    {
        public override bool IsDisposed { get; protected set; }

        public readonly uint Handle;
        public readonly BufferType OriginalType;
        public readonly BufferTargetARB Target;
        public readonly uint SizeInBytes;

        public unsafe GLBuffer(BufferType type, uint sizeInBytes, void* data, bool dynamic)
        {
            if (type == BufferType.Count)
                throw new ArgumentOutOfRangeException(nameof(type), type, null);

            SizeInBytes = sizeInBytes;

            OriginalType = type;
            switch (type)
            {
                case BufferType.VertexBuffer:
                    Target = BufferTargetARB.ArrayBuffer;
                    break;
                case BufferType.ElementsBuffer:
                    Target = BufferTargetARB.ElementArrayBuffer;
                    break;
                case BufferType.UniformBuffer:
                    Target = BufferTargetARB.UniformBuffer;
                    break;
                case BufferType.StructuredBuffer:
                    Target = BufferTargetARB.ShaderStorageBuffer;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }


            Handle = GLDevice.GL.GenBuffer();
            Bind();
            if (sizeInBytes != 0)
                Set(sizeInBytes, data, dynamic);
        }

        public unsafe void Set(uint sizeInBytes, void* data, bool dynamic)
        {
            Bind();
            BufferUsageARB usage = dynamic ? BufferUsageARB.DynamicDraw : BufferUsageARB.StaticDraw;
            GLDevice.GL.BufferData(Target, sizeInBytes, data, usage);
        }

        public unsafe void Update(uint offsetInBytes, uint sizeInBytes, void* data)
        {
            Bind();
            GLDevice.GL.BufferSubData(Target, (nint)offsetInBytes, sizeInBytes, data);
        }

        public override void Dispose()
        {
            if (IsDisposed)
                return;

            if (boundBuffers[(int)OriginalType] == Handle)
                boundBuffers[(int)OriginalType] = 0;

            IsDisposed = true;
            GLDevice.GL.DeleteBuffer(Handle);
        }

        public override string ToString()
        {
            return Handle.ToString();
        }

        private readonly static uint[] boundBuffers = new uint[(int)BufferType.Count];

        private void Bind()
        {
            if (boundBuffers[(int)OriginalType] == Handle)
                return;
            GLDevice.GL.BindBuffer(Target, Handle);
            boundBuffers[(int)OriginalType] = Handle;
        }
    }
}
