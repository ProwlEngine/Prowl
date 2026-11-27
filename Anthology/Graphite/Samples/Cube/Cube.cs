using System;
using System.Collections.Generic;

using Prowl.Vector;


namespace Prowl.Graphite.Samples.Cube;


public class Cube
{
    static Mesh sharedMesh;
    static GraphicsProgram sharedPass;
    static (Texture texture, Sampler sampler) cat;

    static Float4x4 view;
    static Float4x4 projection;
    static PropertySet properties;


    public static void Create(GraphicsDevice device)
    {
        cat = ImageLoader.Load(device, "Cat_cat.png");
        sharedMesh = ModelLoader.CreateCube(device);
        sharedPass = ShaderLoader.CreateShader(device);

        Random rng = new(1337);

        properties = new();
        properties.SetTexture("MainTexture", cat.texture, cat.sampler);
        properties.SetFloat4("Color", new Float4(
            (float)rng.NextDouble(),
            (float)rng.NextDouble(),
            (float)rng.NextDouble(),
            1.0f));

        float cameraDistance = 2f;

        projection = Float4x4.CreatePerspectiveFov(1.0472f, 1, 1f, 100);

        Float3 cameraPosition = new(cameraDistance, cameraDistance, cameraDistance);
        view = Float4x4.CreateLookAt(cameraPosition, Float3.Zero, Float3.UnitY);
    }


    public static void Draw(CommandBuffer buffer)
    {
        Float4x4 viewProj = projection * view;

        buffer.SetShader(sharedPass);
        buffer.SetVertexSource(sharedMesh);

        Float4x4 model = Float4x4.CreateTRS(Float3.Zero, Quaternion.Identity, Float3.One);
        Float4x4 mvp = viewProj * model;

        properties.SetMatrix("MatrixMVP", mvp);

        buffer.SetProperties(properties);
        buffer.DrawIndexed();
    }


    public static void Dispose()
    {
        sharedPass.Dispose();
        sharedMesh.Dispose();
        cat.texture.Dispose();
        cat.sampler.Dispose();
    }
}

