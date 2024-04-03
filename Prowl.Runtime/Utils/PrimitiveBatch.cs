using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    public class PrimitiveBatch
    {
        private struct Vertex
        {
            public System.Numerics.Vector3 Position;
            public System.Numerics.Vector4 Color;
        }

        private uint vao;
        private uint vbo;
        private List<Vertex> vertices = new List<Vertex>(50);
        private Mesh mesh;

        private PrimitiveType primitiveType;

        public bool IsUploaded { get; private set; }

        public PrimitiveBatch(PrimitiveType primitiveType)
        {
            this.primitiveType = primitiveType;

            vao = Graphics.GL.GenVertexArray();
            vbo = Graphics.GL.GenBuffer();

            Graphics.GL.BindVertexArray(vao);
            Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            Graphics.CheckGL();

            new VertexFormat([
                new VertexFormat.Element((uint)0, VertexFormat.VertexType.Float, 3),
                new VertexFormat.Element((uint)1, VertexFormat.VertexType.Float, 4)
            ]).Bind();

            Graphics.CheckGL();
            IsUploaded = false;
        }

        public void Reset()
        {
            vertices.Clear();
            IsUploaded = false;
        }

        public void Line(Vector3 a, Vector3 b, Color colorA, Color colorB)
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
            Graphics.CheckGL();

            IsUploaded = true;
        }

        public void Draw()
        {
            if (vertices.Count == 0 || vao <= 0) return;

            Graphics.GL.BindVertexArray(vao);
            Graphics.GL.DrawArrays(primitiveType, 0, (uint)vertices.Count);
            Graphics.CheckGL();
        }
    }

}
