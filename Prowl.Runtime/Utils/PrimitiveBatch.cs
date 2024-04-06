﻿using Prowl.Runtime.Rendering;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Prowl.Runtime
{
    public class PrimitiveBatch
    {
        private struct Vertex
        {
            public float x, y, z;
            public float r, g, b, a;
        }

        private uint vao;
        private GraphicsBuffer vbo;
        private List<Vertex> vertices = new List<Vertex>(50);
        private Mesh mesh;

        private PrimitiveType primitiveType;

        public bool IsUploaded { get; private set; }

        public PrimitiveBatch(PrimitiveType primitiveType)
        {
            this.primitiveType = primitiveType;

            vao = Graphics.Device.GenVertexArray();
            Graphics.Device.BindVertexArray(vao);
            vbo = Graphics.Device.CreateBuffer(BufferType.VertexBuffer, new byte[0], true);

            new VertexFormat([
                new VertexFormat.Element((uint)0, VertexFormat.VertexType.Float, 3),
                new VertexFormat.Element((uint)1, VertexFormat.VertexType.Float, 4)
            ]).Bind();

            IsUploaded = false;
        }

        public void Reset()
        {
            vertices.Clear();
            IsUploaded = false;
        }

        public void Line(Vector3 a, Vector3 b, Color colorA, Color colorB)
        {
            System.Numerics.Vector3 af = a;
            System.Numerics.Vector3 bf = b;
            vertices.Add(new Vertex { x = af.X, y = af.Y, z = af.Z, r = colorA.r, g = colorA.g, b = colorA.b, a = colorA.a });
            vertices.Add(new Vertex { x = bf.X, y = bf.Y, z = bf.Z, r = colorB.r, g = colorB.g, b = colorB.b, a = colorB.a });
        }

        // Implement Quad and QuadWire similarly...

        public void Upload()
        {
            if (vertices.Count == 0) return;

            Graphics.Device.SetBuffer(vbo, vertices.ToArray(), true);

            IsUploaded = true;
        }

        public void Draw()
        {
            if (vertices.Count == 0 || vao <= 0) return;

            Graphics.Device.BindVertexArray(vao);
            Graphics.Device.DrawArrays(primitiveType, 0, (uint)vertices.Count);
        }
    }

}
