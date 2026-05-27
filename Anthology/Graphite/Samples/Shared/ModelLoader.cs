using System;
using System.IO;


using Prowl.Vector;


namespace Prowl.Veldrid.Samples;


public static class ModelLoader
{
    public static Mesh CreateCube(GraphicsDevice device)
    {
        MeshCreateInfo createInfo = new()
        {
            VertexLayout = [
                new VertexElementDescription("POSITION", VertexElementFormat.Float3),
                new VertexElementDescription("UV", VertexElementFormat.Float2)
            ],
            Topology = PrimitiveTopology.TriangleList
        };

        Mesh mesh = new Mesh(device, createInfo);

        mesh.SetVertexInput([
            // Back face (-Z)
            new Float3(-0.5f, -0.5f, -0.5f), // 0
            new Float3( 0.5f, -0.5f, -0.5f), // 1
            new Float3( 0.5f,  0.5f, -0.5f), // 2
            new Float3(-0.5f,  0.5f, -0.5f), // 3

            // Front face (+Z)
            new Float3(-0.5f, -0.5f,  0.5f), // 4
            new Float3( 0.5f, -0.5f,  0.5f), // 5
            new Float3( 0.5f,  0.5f,  0.5f), // 6
            new Float3(-0.5f,  0.5f,  0.5f), // 7

            // Left face (-X)
            new Float3(-0.5f, -0.5f, -0.5f), // 8
            new Float3(-0.5f,  0.5f, -0.5f), // 9
            new Float3(-0.5f, -0.5f,  0.5f), // 10
            new Float3(-0.5f,  0.5f,  0.5f), // 11

            // Right face (+X)
            new Float3( 0.5f, -0.5f, -0.5f), // 12
            new Float3( 0.5f,  0.5f, -0.5f), // 13
            new Float3( 0.5f, -0.5f,  0.5f), // 14
            new Float3( 0.5f,  0.5f,  0.5f), // 15

            // Bottom face (-Y)
            new Float3(-0.5f, -0.5f, -0.5f), // 16
            new Float3( 0.5f, -0.5f, -0.5f), // 17
            new Float3(-0.5f, -0.5f,  0.5f), // 18
            new Float3( 0.5f, -0.5f,  0.5f), // 19

            // Top face (+Y)
            new Float3( 0.5f,  0.5f, -0.5f), // 20
            new Float3(-0.5f,  0.5f, -0.5f), // 21
            new Float3( 0.5f,  0.5f,  0.5f), // 22
            new Float3(-0.5f,  0.5f,  0.5f)  // 23
        ], 0);

        mesh.SetVertexInput([
            // Back face (-Z)
            new Float2(0.0f, 0.0f),
            new Float2(1.0f, 0.0f),
            new Float2(1.0f, 1.0f),
            new Float2(0.0f, 1.0f),

            // Front face (+Z)
            new Float2(0.0f, 0.0f),
            new Float2(1.0f, 0.0f),
            new Float2(1.0f, 1.0f),
            new Float2(0.0f, 1.0f),

            // Left face (-X)
            new Float2(0.0f, 0.0f),
            new Float2(0.0f, 1.0f),
            new Float2(1.0f, 0.0f),
            new Float2(1.0f, 1.0f),

            // Right face (+X)
            new Float2(0.0f, 0.0f),
            new Float2(0.0f, 1.0f),
            new Float2(1.0f, 0.0f),
            new Float2(1.0f, 1.0f),

            // Bottom face (-Y)
            new Float2(0.0f, 0.0f),
            new Float2(1.0f, 0.0f),
            new Float2(0.0f, 1.0f),
            new Float2(1.0f, 1.0f),

            // Top face (+Y)
            new Float2(1.0f, 0.0f),
            new Float2(0.0f, 0.0f),
            new Float2(1.0f, 1.0f),
            new Float2(0.0f, 1.0f)
        ], 1);

        mesh.SetIndexInput16([
            // Back face (-Z)
            0, 2, 1,
            0, 3, 2,

            // Front face (+Z)
            4, 5, 6,
            4, 6, 7,

            // Left face (-X)
            8, 11, 9,
            8, 10, 11,

            // Right face (+X)
            12, 13, 15,
            12, 15, 14,

            // Bottom face (-Y)
            16, 17, 19,
            16, 19, 18,

            // Top face (+Y)
            21, 23, 22,
            21, 22, 20
    ]);

        return mesh;
    }


    public static Mesh CreateTriangle(GraphicsDevice device)
    {
        MeshCreateInfo createInfo = new()
        {
            VertexLayout = [
                new VertexElementDescription("POSITION", VertexElementFormat.Float3),
                new VertexElementDescription("UV", VertexElementFormat.Float2)
            ],
            Topology = PrimitiveTopology.TriangleStrip
        };

        Mesh mesh = new(device, createInfo);

        mesh.SetVertexInput([
            new Float3( 0.0f,  0.5f, 0.5f),   // top
            new Float3(-0.5f, -0.5f, 0.5f),   // bottom left
            new Float3( 0.5f, -0.5f, 0.5f)
        ], 0);

        mesh.SetVertexInput([
            new Float2(1.0f, 1.0f),   // top
            new Float2(0.0f, 1.0f),   // bottom left
            new Float2(1.0f, 0.0f)
        ], 1);

        mesh.SetIndexInput16([
            2, 1, 0
        ]);

        return mesh;
    }


    public static Mesh CreateQuad(GraphicsDevice device)
    {
        MeshCreateInfo createInfo = new()
        {
            VertexLayout = [
                new VertexElementDescription("POSITION", VertexElementFormat.Float3),
                new VertexElementDescription("UV", VertexElementFormat.Float2)
            ],
            Topology = PrimitiveTopology.TriangleStrip
        };

        Mesh mesh = new(device, createInfo);

        mesh.SetVertexInput([
            new Float3(0.5f,  0.5f, 0.5f),   // top right
            new Float3(-0.5f,  0.5f, 0.5f),   // top left
            new Float3(-0.5f, -0.5f, 0.5f),   // bottom left
            new Float3( 0.5f, -0.5f, 0.5f),   // bottom right
        ], 0);

        mesh.SetVertexInput([
            new Float2(1.0f, 1.0f), // top right
            new Float2(0.0f, 1.0f), // top left
            new Float2(0.0f, 0.0f), // bottom left
            new Float2(1.0f, 0.0f), // bottom right
        ], 1);

        mesh.SetIndexInput16([
            0, 1, 2, 0, 3, 2,
        ]);

        return mesh;
    }
}
