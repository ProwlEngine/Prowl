using System;
using System.Collections.Generic;

using Prowl.Vector;


namespace Prowl.Veldrid.Samples.CubeGrid;


public class CubeGrid
{
    public const int CubeCount = 10000;
    public const float SphereRadius = 10f;


    struct CubeInstance
    {
        public PropertySet Properties;
        public Float3 Position;
        public Float3 Axis;
        public float Speed;
        public float PhaseOffset;
    }


    static Mesh sharedMesh;
    static ShaderProgram sharedPass;
    static (Texture texture, Sampler sampler) cat;

    static Float4x4 view;
    static Float4x4 projection;


    static List<CubeInstance> cubes = [];


    public static void Create(GraphicsDevice device)
    {
        cat = ImageLoader.Load(device, "Cat_cat.png");
        sharedMesh = ModelLoader.CreateCube(device);
        sharedPass = ShaderLoader.CreateShader(device);

        Random rng = new(1337);

        for (int x = 0; x < CubeCount; x++)
        {
            PropertySet props = new();
            props.SetTexture("MainTexture", cat.texture, cat.sampler);
            props.SetFloat4("Color", new Float4(
                (float)rng.NextDouble(),
                (float)rng.NextDouble(),
                (float)rng.NextDouble(),
                1.0f));

            Float3 axis = Float3.Normalize(new Float3(
                (float)rng.NextDouble() * 2.0f - 1.0f,
                (float)rng.NextDouble() * 2.0f - 1.0f,
                (float)rng.NextDouble() * 2.0f - 1.0f));

            Float3 position = Float3.Normalize(new Float3(
                (float)rng.NextDouble() * 2.0f - 1.0f,
                (float)rng.NextDouble() * 2.0f - 1.0f,
                (float)rng.NextDouble() * 2.0f - 1.0f)) * SphereRadius;

            CubeInstance instance = new()
            {
                Properties = props,
                Position = position,
                Axis = axis,
                Speed = 0.5f + (float)rng.NextDouble() * 2.0f,
                PhaseOffset = (float)rng.NextDouble() * MathF.PI * 2.0f,
            };

            cubes.Add(instance);
        }

        float cameraDistance = SphereRadius * 1.45f;

        projection = Float4x4.CreatePerspectiveFov(1.0472f, 1, 1f, 100);

        Float3 cameraPosition = new(cameraDistance, cameraDistance, cameraDistance);
        view = Float4x4.CreateLookAt(cameraPosition, Float3.Zero, Float3.UnitY);
    }


    public static void Draw(float time, CommandBuffer buffer)
    {
        Float4x4 viewProj = projection * view;

        buffer.SetShader(sharedPass);
        buffer.SetVertexSource(sharedMesh);

        foreach (CubeInstance cube in cubes)
        {
            float angle = cube.PhaseOffset + time * cube.Speed;
            Quaternion spin = Quaternion.AngleAxis(angle, cube.Axis);

            Float4x4 model = Float4x4.CreateTRS(cube.Position, spin, Float3.One);
            Float4x4 mvp = viewProj * model;

            cube.Properties.SetMatrix("MatrixMVP", mvp);

            buffer.SetProperties(cube.Properties);
            buffer.DrawIndexed();
        }
    }


    public static void Dispose()
    {
        sharedPass.Dispose();
        sharedMesh.Dispose();
        cat.texture.Dispose();
        cat.sampler.Dispose();
    }
}

