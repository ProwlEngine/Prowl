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

        Surface = Renderer.LoadShader(ShaderParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Shaders", "Demo.shader"))));
        Cube = Renderer.CreateMesh(Geometry.Cube());
        Sphere = Renderer.CreateMesh(Geometry.Sphere());
        Cylinder = Renderer.CreateMesh(Geometry.Cylinder());

        Taa = new TemporalAntiAliasing(Renderer);
        AutoExposure = new AutoExposure(Renderer);
        Bloom = new Bloom(Renderer, ShaderLoader.Compiler);
        Tonemap = new Tonemapper(Renderer) { Operator = TonemapOperator.Aces };
        Post.Add(Taa);
        Post.Add(AutoExposure);
        Bloom.AddTo(Post);
        Post.Add(Tonemap);
    }

    public GraphicsDevice Device { get; }
    public Renderer Renderer { get; }
    /// <summary>Graphite/PBR plus the world grid and the instance colour, for the objects that show them.</summary>
    public Shader Surface { get; }
    public GpuMesh Cube { get; }
    public GpuMesh Sphere { get; }
    public GpuMesh Cylinder { get; }
    public ExtensionSet<RenderFeature> Post { get; } = new();
    public TemporalAntiAliasing Taa { get; }
    public AutoExposure AutoExposure { get; }
    public Bloom Bloom { get; }
    public Tonemapper Tonemap { get; }

    /// <summary>A Graphite/PBR material, on <see cref="Surface"/> when it shows a grid or the instance colour.</summary>
    public Material CreatePbr(Float3 color, float metallic = 0f, float roughness = 0.6f, Float3 emission = default, float grid = 0f, bool instanceColor = false)
    {
        bool custom = grid > 0f || instanceColor;
        Material material = Renderer.CreateMaterial(custom ? Surface : Renderer.Standard.Lit);
        material.SetColor("base_color", new Float4(color.X, color.Y, color.Z, 1));
        material.SetColor("emission_color", new Float4(emission.X, emission.Y, emission.Z, 1));
        material.SetFloat("emission_luminance", 1f);
        material.SetFloat("base_metalness", metallic);
        material.SetFloat("specular_roughness", roughness);
        if (grid > 0f)
            material.SetFloat("_Grid", grid);
        if (instanceColor)
            material.SetFloat("_InstanceColor", 1f);
        return material;
    }

    public void Dispose()
    {
        Cube.Dispose();
        Sphere.Dispose();
        Cylinder.Dispose();
        Renderer.Dispose();
        Bloom.Dispose();
    }
}
