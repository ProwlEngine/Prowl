using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Vector;

using Silk.NET.Input;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal abstract class DemoScene : IDisposable
{
    private readonly List<Material> _materials = new();

    protected DemoScene(Demo demo)
    {
        Demo = demo;
        World = demo.Renderer.CreateWorld();
        World.Name = Name;
        View = demo.Renderer.CreateView(World, "Main camera");
        View.ClearColor = new Float4(0.01f, 0.012f, 0.02f, 1);
        View.Features = demo.Post;
    }

    protected Demo Demo { get; }

    public RenderWorld World { get; }

    /// <summary>The camera into the world, keeping its history while other scenes show.</summary>
    public RenderView View { get; }

    public abstract string Name { get; }

    public abstract Double3 StartPosition { get; }

    public abstract float StartYaw { get; }

    public abstract float StartPitch { get; }

    /// <summary>The exposure the camera starts from before auto exposure has measured the scene.</summary>
    public virtual float StartEV100 => 14f;

    public virtual string Status => "";

    public virtual string Controls => "";

    public abstract void Update(float time, float deltaTime);

    public virtual void OnKey(Key key)
    {
    }

    protected Material Pbr(Float3 color, float metallic = 0f, float roughness = 0.6f, Float3 emission = default, float grid = 0f)
    {
        Material material = Demo.CreatePbr(color, metallic, roughness, emission, grid);
        material.Name = $"PBR {_materials.Count}";
        _materials.Add(material);
        return material;
    }

    protected Material Track(Material material)
    {
        _materials.Add(material);
        return material;
    }

    protected RenderableHandle Add(GpuMesh mesh, Material material, Double3 position, Double3 scale, Quaternion? rotation = null, RenderableFlags flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows, string? name = null)
    {
        RenderableDesc desc = new()
        {
            Mesh = mesh,
            Name = name ?? $"{MeshName(mesh)} {World.Count}",
            Transform = Double4x4.CreateTRS(position, rotation ?? Quaternion.Identity, scale),
            Flags = flags,
        };
        return World.Add(desc, [material]);
    }

    private string MeshName(GpuMesh mesh)
        => mesh == Demo.Cube ? "Cube" : mesh == Demo.Sphere ? "Sphere" : mesh == Demo.Cylinder ? "Cylinder" : "Mesh";

    public virtual void Dispose()
    {
        View.Dispose();
        World.Dispose();
        foreach (Material material in _materials)
            material.Dispose();
    }

    protected static Float3 Hsv(float hue, float saturation, float value)
    {
        hue = hue - MathF.Floor(hue);
        float r = Math.Clamp(MathF.Abs(hue * 6 - 3) - 1, 0, 1);
        float g = Math.Clamp(2 - MathF.Abs(hue * 6 - 2), 0, 1);
        float b = Math.Clamp(2 - MathF.Abs(hue * 6 - 4), 0, 1);
        return new Float3(
            value * (1 + saturation * (r - 1)),
            value * (1 + saturation * (g - 1)),
            value * (1 + saturation * (b - 1)));
    }
}
