using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    public class PrimitiveBatch
    {
        private struct Vertex
        {
            public Vector3 Position;
            public Vector4 Color;
        }

        private uint vao;
        private uint vbo;
        private List<Vertex> vertices = new List<Vertex>(50);

        private PrimitiveType primitiveType;

        public PrimitiveBatch(PrimitiveType primitiveType)
        {
            this.primitiveType = primitiveType;

            vao = Graphics.GL.GenVertexArray();
            vbo = Graphics.GL.GenBuffer();

            Graphics.GL.BindVertexArray(vao);
            Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

            // Define the layout of the vertex data
            unsafe {
                Graphics.GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex), (void*)0); // position
                Graphics.GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex), (void*)sizeof(Vector3)); // color
            }
            Graphics.GL.EnableVertexAttribArray(0);
            Graphics.GL.EnableVertexAttribArray(1);
        }

        public void Reset()
        {
            vertices.Clear();
        }

        public void Line(Vector3 a, Vector3 b, Vector4 colorA, Vector4 colorB)
        {
            vertices.Add(new Vertex { Position = a, Color = colorA });
            vertices.Add(new Vertex { Position = b, Color = colorB });
        }

        // Implement Quad and QuadWire similarly...

        public void Upload()
        {
            if (vertices.Count == 0) return;

            Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            Graphics.GL.BufferData(BufferTargetARB.ArrayBuffer, new ReadOnlySpan<Vertex>(vertices.ToArray()), BufferUsageARB.StaticDraw);

            Graphics.GL.BindVertexArray(vao);
        }

        public void Draw()
        {
            if (vertices.Count == 0 || vao <= 0) return;

            Graphics.GL.DrawArrays(primitiveType, 0, (uint)vertices.Count);
        }
    }

}
