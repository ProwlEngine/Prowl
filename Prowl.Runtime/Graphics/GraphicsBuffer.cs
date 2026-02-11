// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

namespace Prowl.Runtime;

public class GraphicsBuffer : IDisposable
{
    public bool IsDisposed { get; protected set; }

    public readonly uint Handle;
    public readonly BufferType OriginalType;
    public readonly BufferTargetARB Target;
    public readonly uint SizeInBytes;

    public unsafe GraphicsBuffer(BufferType type, uint sizeInBytes, void* data, bool dynamic)
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


        Handle = Graphics.GL.GenBuffer();
        Bind();
        if (sizeInBytes != 0)
            Set(sizeInBytes, data, dynamic);
    }

    public unsafe void Set(uint sizeInBytes, void* data, bool dynamic)
    {
        Bind();
        BufferUsageARB usage = dynamic ? BufferUsageARB.DynamicDraw : BufferUsageARB.StaticDraw;
        Graphics.GL.BufferData(Target, sizeInBytes, data, usage);
    }

    public unsafe void Update(uint offsetInBytes, uint sizeInBytes, void* data)
    {
        Bind();
        Graphics.GL.BufferSubData(Target, (nint)offsetInBytes, sizeInBytes, data);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        if (boundBuffers[(int)OriginalType] == Handle)
            boundBuffers[(int)OriginalType] = 0;

        IsDisposed = true;
        Graphics.GL.DeleteBuffer(Handle);
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
        Graphics.GL.BindBuffer(Target, Handle);
        boundBuffers[(int)OriginalType] = Handle;
    }
}
