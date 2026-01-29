// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

namespace Prowl.Runtime.Graphite.OpenGL;

/// <summary>
/// OpenGL implementation of a GPU buffer.
/// </summary>
public class GLBuffer : Buffer
{
    private readonly GLGraphiteDevice _device;
    internal uint Handle { get; private set; }

    internal GLBuffer(GLGraphiteDevice device, in BufferDescriptor descriptor)
    {
        _device = device;
        SizeInBytes = descriptor.SizeInBytes;
        Usage = descriptor.Usage;
        MemoryAccess = descriptor.MemoryAccess;
        DebugName = descriptor.DebugName;

        Handle = _device.GL.GenBuffer();

        var bufferUsage = GetBufferUsage(descriptor.MemoryAccess);

        _device.GL.BindBuffer(BufferTargetARB.ArrayBuffer, Handle);

        if (descriptor.InitialData.HasValue)
        {
            var data = descriptor.InitialData.Value.Span;
            unsafe
            {
                fixed (byte* ptr = data)
                {
                    _device.GL.BufferData(BufferTargetARB.ArrayBuffer, (nuint)descriptor.SizeInBytes, ptr, bufferUsage);
                }
            }
        }
        else
        {
            unsafe
            {
                _device.GL.BufferData(BufferTargetARB.ArrayBuffer, (nuint)descriptor.SizeInBytes, null, bufferUsage);
            }
        }

        _device.GL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    internal unsafe void Update<T>(uint offsetInBytes, ReadOnlySpan<T> data) where T : unmanaged
    {
        ThrowIfDisposed();

        // Validate bounds
        uint dataSize = (uint)(data.Length * sizeof(T));
        if (offsetInBytes + dataSize > SizeInBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(data),
                $"Buffer update would exceed buffer bounds. Offset: {offsetInBytes}, Data size: {dataSize}, Buffer size: {SizeInBytes}");
        }

        _device.GL.BindBuffer(BufferTargetARB.ArrayBuffer, Handle);
        fixed (T* ptr = data)
        {
            _device.GL.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)offsetInBytes, (nuint)dataSize, ptr);
        }
        _device.GL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    private static BufferUsageARB GetBufferUsage(MemoryAccess access) => access switch
    {
        MemoryAccess.GpuOnly => BufferUsageARB.StaticDraw,
        MemoryAccess.CpuToGpu => BufferUsageARB.DynamicDraw,
        MemoryAccess.GpuToCpu => BufferUsageARB.StreamRead,
        _ => BufferUsageARB.StaticDraw,
    };

    internal BufferTargetARB GetTarget()
    {
        if (Usage.HasFlag(BufferUsage.Vertex)) return BufferTargetARB.ArrayBuffer;
        if (Usage.HasFlag(BufferUsage.Index)) return BufferTargetARB.ElementArrayBuffer;
        if (Usage.HasFlag(BufferUsage.Uniform)) return BufferTargetARB.UniformBuffer;
        if (Usage.HasFlag(BufferUsage.Storage)) return BufferTargetARB.ShaderStorageBuffer;
        if (Usage.HasFlag(BufferUsage.Indirect)) return BufferTargetARB.DrawIndirectBuffer;
        return BufferTargetARB.ArrayBuffer;
    }

    protected override void DisposeResources()
    {
        if (Handle != 0)
        {
            _device.GL.DeleteBuffer(Handle);
            Handle = 0;
        }
    }
}
