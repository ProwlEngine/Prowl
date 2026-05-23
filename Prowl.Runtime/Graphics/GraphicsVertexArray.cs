// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

using static Prowl.Runtime.VertexFormat;

namespace Prowl.Runtime;

public unsafe class GraphicsVertexArray : IDisposable
{
    public uint Handle { get; internal set; }
    public bool IsDisposed { get; protected set; }

    // Construction params kept for CreateGLObject, which the executor runs later
    // on the render thread.
    private readonly VertexFormat _format;
    private readonly GraphicsBuffer _vertices;
    private readonly GraphicsBuffer? _indices;
    private readonly VertexFormat? _instanceFormat;
    private readonly GraphicsBuffer? _instanceBuffer;

    public GraphicsVertexArray(
        VertexFormat format,
        GraphicsBuffer vertices,
        GraphicsBuffer? indices,
        VertexFormat? instanceFormat = null,
        GraphicsBuffer? instanceBuffer = null)
    {
        _format = format;
        _vertices = vertices;
        _indices = indices;
        _instanceFormat = instanceFormat;
        _instanceBuffer = instanceBuffer;
        Handle = 0;

        using var cmd = Graphics.GetCommandBuffer("GraphicsVertexArray.Create");
        cmd.EncodeCreateVertexArray(this);
        Graphics.Submit(cmd);
    }

    /// <summary>Invoked by the CreateVertexArrayOp executor handler on the render
    /// thread. Buffer handles are valid by submit-order guarantee.</summary>
    internal void CreateGLObject()
    {
        Handle = Graphics.GL.GenVertexArray();
        if (Handle == 0)
            throw new Exception("Failed to create VAO - glGenVertexArray returned 0");

        Graphics.GL.BindVertexArray(Handle);

        Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices.Handle);
        BindFormat(_format);

        if (_instanceFormat != null && _instanceBuffer != null)
        {
            Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer.Handle);
            BindFormat(_instanceFormat);
        }

        // Element binding lands on THIS VAO (which is currently bound).
        if (_indices != null)
            Graphics.GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indices.Handle);

        Graphics.GL.BindVertexArray(0);
    }

    void BindFormat(VertexFormat format)
    {
        for (int i = 0; i < format.Elements.Length; i++)
        {
            Element element = format.Elements[i];
            uint index = element.Semantic;
            Graphics.GL.EnableVertexAttribArray(index);
            int offset = element.Offset;
            unsafe
            {
                if (element.Type == VertexType.Float || element.Normalized)
                    Graphics.GL.VertexAttribPointer(index, element.Count, (GLEnum)element.Type, element.Normalized, (uint)format.Size, (void*)offset);
                else
                    Graphics.GL.VertexAttribIPointer(index, element.Count, (GLEnum)element.Type, (uint)format.Size, (void*)offset);

                if (element.Divisor > 0)
                    Graphics.GL.VertexAttribDivisor(index, (uint)element.Divisor);
            }
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;

        using var cmd = Graphics.GetCommandBuffer("GraphicsVertexArray.Dispose");
        cmd.EncodeDisposeVertexArray(this);
        Graphics.Submit(cmd);
    }

    public override string ToString()
    {
        return Handle.ToString();
    }
}
