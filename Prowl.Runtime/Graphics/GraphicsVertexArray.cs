// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

using static Prowl.Runtime.VertexFormat;

namespace Prowl.Runtime;

public unsafe class GraphicsVertexArray : IDisposable
{
    public uint Handle { get; private set; }

    public GraphicsVertexArray(
        VertexFormat format,
        GraphicsBuffer vertices,
        GraphicsBuffer? indices,
        VertexFormat? instanceFormat = null,
        GraphicsBuffer? instanceBuffer = null)
    {
        Handle = Graphics.GL.GenVertexArray();

        if (Handle == 0)
        {
            throw new System.Exception("Failed to create VAO - glGenVertexArray returned 0");
        }

        Graphics.GL.BindVertexArray(Handle);

        // Bind vertex buffer and set up per-vertex attributes
        Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, vertices.Handle);
        BindFormat(format);

        // Bind instance buffer and set up per-instance attributes (if provided)
        if (instanceFormat != null && instanceBuffer != null)
        {
            Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, instanceBuffer.Handle);
            BindFormat(instanceFormat);
        }

        // Bind index buffer if present
        if (indices != null)
            Graphics.GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, indices.Handle);

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

                // Set divisor for instancing (0 = per-vertex, 1+ = per-instance)
                if (element.Divisor > 0)
                {
                    Graphics.GL.VertexAttribDivisor(index, (uint)element.Divisor);
                }
            }
        }
    }

    public bool IsDisposed { get; protected set; }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        Graphics.GL.DeleteVertexArray(Handle);
        IsDisposed = true;
    }

    public override string ToString()
    {
        return Handle.ToString();
    }
}
