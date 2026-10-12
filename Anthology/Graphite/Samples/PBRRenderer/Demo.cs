using System;
using System.IO;

using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal sealed class Demo : IDisposable
{
    public Demo(GraphicsDevice device)
    {
        Device = device;
        Renderer = new Renderer(device, ShaderLoader.Compiler);
        Renderer.Error += error => Console.WriteLine($"[{error.Severity}] {error.Code}: {error.Message}");

        Lit = Renderer.LoadShader(ShaderParser.Parse(File.ReadAllText("Shaders/Lit.shader")));
        Cube = Renderer.CreateMesh(Geometry.Cube());
        Sphere = Renderer.CreateMesh(Geometry.Sphere());
        Cylinder = Renderer.CreateMesh(Geometry.Cylinder());

        PixelFormat format = device.MainSwapchain.Framebuffer.OutputDescription.ColorFormats[0];
        bool srgb = format.ToString().EndsWith("SRgb", StringComparison.Ordinal);

        Bloom = new Bloom(Renderer, ShaderLoader.Compiler);
        Tonemap = new Tonemap(Renderer, !srgb);
        Bloom.AddTo(Post);
        Post.Add(Tonemap);
    }

    public GraphicsDevice Device { get; }
    public Renderer Renderer { get; }
    public Shader Lit { get; }
    public GpuMesh Cube { get; }
    public GpuMesh Sphere { get; }
    public GpuMesh Cylinder { get; }
    public FeatureSet Post { get; } = new();
    public Bloom Bloom { get; }
    public Tonemap Tonemap { get; }

    public Material CreateLit(Float3 color, float metallic = 0f, float roughness = 0.6f, Float3 emission = default, float grid = 0f)
    {
        Material material = Renderer.CreateMaterial(Lit);
        material.SetColor("_BaseColor", new Float4(color.X, color.Y, color.Z, 1));
        material.SetColor("_Emission", new Float4(emission.X, emission.Y, emission.Z, 1));
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Roughness", roughness);
        material.SetFloat("_Grid", grid);
        return material;
    }

    public void Dispose()
    {
        Cube.Dispose();
        Sphere.Dispose();
        Cylinder.Dispose();
        Renderer.Dispose();
        Bloom.Dispose();
        Tonemap.Dispose();
    }
}
