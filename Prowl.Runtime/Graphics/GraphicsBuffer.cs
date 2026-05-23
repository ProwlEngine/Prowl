// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

namespace Prowl.Runtime;

public class GraphicsBuffer : IDisposable
{
    public bool IsDisposed { get; protected set; }

    // Handle / SizeInBytes are mutable because resource lifecycle now flows through
    // CommandBuffer opcodes the constructor allocates a 0-handle CPU stub and the
    // executor fills these in when the CreateBuffer opcode runs. Same story for
    // Set: it reallocates GL storage and the new size lives here.
    public uint Handle { get; internal set; }
    public readonly BufferType OriginalType;
    public readonly BufferTargetARB Target;
    public uint SizeInBytes { get; internal set; }

    /// <summary>Constructs the CPU wrapper and queues the GL buffer create + initial
    /// upload through a CommandBuffer. Under Step 1 sync submit, the GL work runs
    /// inline before this constructor returns. Under Step 2 (render thread), the
    /// work is queued in order so any subsequent encoded use of this buffer is
    /// guaranteed to execute against a valid GL handle.</summary>
    public GraphicsBuffer(BufferType type, ReadOnlySpan<byte> initialData, bool dynamic)
    {
        if (type == BufferType.Count)
            throw new ArgumentOutOfRangeException(nameof(type), type, null);

        OriginalType = type;
        Target = type switch
        {
            BufferType.VertexBuffer => BufferTargetARB.ArrayBuffer,
            BufferType.ElementsBuffer => BufferTargetARB.ElementArrayBuffer,
            BufferType.UniformBuffer => BufferTargetARB.UniformBuffer,
            BufferType.StructuredBuffer => BufferTargetARB.ShaderStorageBuffer,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
        // SizeInBytes is the CPU mirror of the GL storage size. The executor's
        // Set call inside CreateBuffer overwrites it with the actual uploaded size.
        SizeInBytes = (uint)initialData.Length;
        Handle = 0;

        using var cmd = Graphics.GetCommandBuffer("GraphicsBuffer.Create");
        cmd.EncodeCreateBuffer(this, dynamic, initialData);
        Graphics.Submit(cmd);
    }

    public unsafe void Set(uint sizeInBytes, void* data, bool dynamic)
    {
        Bind();
        BufferUsageARB usage = dynamic ? BufferUsageARB.DynamicDraw : BufferUsageARB.StaticDraw;
        Graphics.GL.BufferData(Target, sizeInBytes, data, usage);
        SizeInBytes = sizeInBytes;
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
        IsDisposed = true;
        // Queue the GL delete so it runs after any encoded use of this buffer that
        // hasn't executed yet. Sync submit makes it immediate; render-thread mode
        // makes it deferred but still ordered.
        using var cmd = Graphics.GetCommandBuffer("GraphicsBuffer.Dispose");
        cmd.EncodeDisposeBuffer(this);
        Graphics.Submit(cmd);
    }

    public override string ToString()
    {
        return Handle.ToString();
    }

    // No "currently bound buffer" cache. The previous version kept one indexed by
    // BufferType, but ELEMENT_ARRAY_BUFFER's binding is part of VAO state (per-VAO),
    // not global the cache would correctly skip a redundant ARRAY_BUFFER bind but
    // silently lie for ELEMENT_ARRAY_BUFFER across VAO switches. glBindBuffer is
    // cheap; always-bind is correct and simple.
    private void Bind()
    {
        if (OriginalType == BufferType.ElementsBuffer)
        {
            // ELEMENT_ARRAY_BUFFER binding lives on the currently-bound VAO. If we
            // call glBindBuffer(ELEMENT_ARRAY_BUFFER, x) while a user VAO is bound,
            // x silently becomes that VAO's new index buffer, corrupting it for
            // every subsequent draw. Switch to VAO 0 first so any binding change
            // here lands in the default VAO. The owning user VAO retains its
            // original element binding (set at VAO-creation time) which points to
            // the same buffer we're about to upload to anyway.
            Graphics.GL.BindVertexArray(0);
            Graphics.Executor.InvalidateBoundVAO();
        }
        Graphics.GL.BindBuffer(Target, Handle);
    }
}
