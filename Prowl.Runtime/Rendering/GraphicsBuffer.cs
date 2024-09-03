using System;

using Veldrid;

namespace Prowl.Runtime
{
    public class GraphicsBuffer
    {
        public static readonly GraphicsBuffer Empty = new GraphicsBuffer(0, 0, false);
        public static readonly GraphicsBuffer EmptyRW = new GraphicsBuffer(0, 0, true);


        public DeviceBuffer Buffer { get; private set; }

        public uint Count { get; private set; }
        public uint Stride { get; private set; }


        public GraphicsBuffer(uint count, uint stride, bool writable)
        {
            this.Count = count;
            this.Stride = stride;
            Buffer = Graphics.Factory.CreateBuffer(new BufferDescription(count * stride,
                writable ? BufferUsage.StructuredBufferReadWrite : BufferUsage.StructuredBufferReadOnly));
        }


        public unsafe void SetData<T>(Span<T> data, uint offset) where T : unmanaged
        {
            Graphics.Device.UpdateBuffer(Buffer, offset, data);
        }
    }
}
