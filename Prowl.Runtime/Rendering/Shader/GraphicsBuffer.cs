using System;
using Veldrid;

namespace Prowl.Runtime
{
    public sealed class GraphicsBuffer(BufferUsage usage, uint sizeInBytes) : IDisposable
    {
        private readonly DeviceBuffer buffer = Graphics.Factory.CreateBuffer(new BufferDescription(sizeInBytes, usage));

        public DeviceBuffer Buffer => buffer;

        /// <summary>
        /// Set the data of the buffer.
        /// </summary>
        /// <typeparam name="T">The type of the data.</typeparam>
        /// <param name="data">An array containing the data to upload.</param>
        /// <param name="bufferOffsetInBytes">
        /// An offset, in bytes, from the beginning of the <see cref="GraphicsBuffer"/>'s storage, at
        /// which new data will be uploaded.
        /// </param>
        public void SetData<T>(T[] data, uint bufferOffsetInBytes = 0) where T : unmanaged 
            => Graphics.Device.UpdateBuffer(buffer, bufferOffsetInBytes, data);

        public void Dispose() => buffer.Dispose();
    }
}