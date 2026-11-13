using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Graphite.Rendering.Tools;
using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Samples.PBRRenderer;

/// <summary>
/// Generated broadleaf and conifer trees, bushes and rocks scattered over the terrain scene: forests in patches below the
/// tree line with bushes around their edges, boulders on open ground and steep slopes. Each tree has two coarser
/// levels and an impostor, each rock two coarser levels.
/// </summary>
internal sealed class TerrainForest : IDisposable
{
    private readonly List<GpuMesh> _meshes = new();
    private readonly List<Texture> _textures = new();
    private readonly List<Material> _materials = new();
    private readonly List<ImpostorAsset> _impostors = new();
    private readonly TerrainScatter _scatter;

    public TerrainForest(Renderer renderer, RenderWorld world, TerrainData data)
    {
        Texture barkColor = Upload(renderer, PropTextures.Bark(512, new Float3(0.32f, 0.25f, 0.19f)));
        Texture barkNormal = Upload(renderer, PropTextures.BarkNormal(512));
        Texture birchColor = Upload(renderer, PropTextures.Bark(512, new Float3(0.42f, 0.4f, 0.36f), seed: 9));
        Texture rockColor = Upload(renderer, PropTextures.Rock(512, new Float3(0.42f, 0.4f, 0.37f)));
        Texture rockNormal = Upload(renderer, PropTextures.RockNormal(512));
        Texture leaves = Upload(renderer, PropTextures.Leaves(512));
        Texture needles = Upload(renderer, PropTextures.Needles(512));

        ShaderLibrary.Register(typeof(Renderer).Assembly);
        Shader leafShader = renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Foliage/Leaves.shader")!));

        Material bark = Lit(renderer, barkColor, barkNormal, 0.85f);
        Material birch = Lit(renderer, birchColor, barkNormal, 0.8f);
        Material rock = Lit(renderer, rockColor, rockNormal, 0.75f);
        Material oak = Leaf(renderer, leafShader, leaves, new Float3(0.2f, 0.36f, 0.08f));
        Material beech = Leaf(renderer, leafShader, leaves, new Float3(0.3f, 0.4f, 0.1f));
        Material pine = Leaf(renderer, leafShader, needles, new Float3(0.1f, 0.2f, 0.08f));
        Material shrub = Leaf(renderer, leafShader, leaves, new Float3(0.18f, 0.3f, 0.09f));

        List<TerrainScatterRule> rules = new();
        List<TerrainPropKind?> kinds = new();
        void Add(TerrainScatterRule rule, TerrainPropKind kind)
        {
            rules.Add(rule);
            kinds.Add(kind);
        }

        for (int i = 0; i < 3; i++)
        {
            TreeDesc broad = new() { Seed = 100 + i, Shape = TreeShape.Broadleaf, Height = 11f + i * 2f, TrunkRadius = 0.24f + i * 0.04f, Branches = 7 + i };
            Add(new TerrainScatterRule { Id = 1 + i, Priority = 0, Density = 0.35f, Footprint = 5f, MaxSteepness = 0.55f, MaxHeight = 400, Layer = TerrainTextures.Grass, MinLayerWeight = 0.3f, NoiseSize = 260f, NoiseThreshold = 0.5f, NoiseSeed = 1, MinScale = 0.8f, MaxScale = 1.2f, Sink = 0.2f },
                Tree(renderer, broad, i == 2 ? birch : bark, i == 1 ? beech : oak));
        }
        for (int i = 0; i < 3; i++)
        {
            TreeDesc conifer = new() { Seed = 200 + i, Shape = TreeShape.Conifer, Height = 15f + i * 3f, TrunkRadius = 0.25f + i * 0.03f, Levels = 2, Branches = 9, LeafSize = 1.3f, LeafDensity = 2.4f };
            Add(new TerrainScatterRule { Id = 10 + i, Priority = 0, Density = 0.4f, Footprint = 4f, MaxSteepness = 0.7f, MinHeight = 180, MaxHeight = 425, NoiseSize = 260f, NoiseThreshold = 0.45f, NoiseSeed = 2, MinScale = 0.75f, MaxScale = 1.25f, Sink = 0.2f },
                Tree(renderer, conifer, bark, pine));
        }
        for (int i = 0; i < 2; i++)
        {
            TreeDesc bush = new() { Seed = 300 + i, Shape = TreeShape.Bush, Height = 1.6f + i * 0.5f, TrunkRadius = 0.04f, Branches = 8, Levels = 2, BranchAngle = 40f, LeafSize = 0.55f, LeafDensity = 5f, Gnarl = 0.5f };
            Add(new TerrainScatterRule { Id = 20 + i, Priority = 2, Density = 1.2f, Footprint = 1.8f, MaxSteepness = 0.7f, MaxHeight = 420, Layer = TerrainTextures.Grass, MinLayerWeight = 0.3f, NoiseSize = 260f, NoiseThreshold = 0.36f, NoiseSeed = 1, MinScale = 0.7f, MaxScale = 1.3f },
                Tree(renderer, bush, bark, shrub, impostor: false));
        }
        for (int i = 0; i < 4; i++)
        {
            RockDesc boulder = new() { Seed = 400 + i, Size = i < 2 ? 1.2f : 2.6f, Stretch = new Float3(1.2f + i * 0.1f, 0.6f + (i % 2) * 0.25f, 1f), Cuts = 6 + i };
            Add(new TerrainScatterRule { Id = 30 + i, Priority = 1, Density = i < 2 ? 0.25f : 0.08f, Footprint = i < 2 ? 1.5f : 3f, NoiseSize = 90f, NoiseThreshold = 0.55f, NoiseSeed = 3, MinScale = 0.6f, MaxScale = 1.5f, Align = 0.6f, Sink = 0.15f },
                Rock(renderer, boulder, rock));
        }

        _scatter = new TerrainScatter(data, rules, seed: 11);
        Props = new TerrainProps(world, _scatter, kinds) { Range = 700 };
    }

    public TerrainProps Props { get; }

    public void Update(Double3 viewer) => Props.Update(viewer);

    public void Dispose()
    {
        Props.Dispose();
        foreach (ImpostorAsset impostor in _impostors)
            impostor.Dispose();
        foreach (Material material in _materials)
            material.Dispose();
        foreach (GpuMesh mesh in _meshes)
            mesh.Dispose();
        foreach (Texture texture in _textures)
            texture.Dispose();
    }

    private TerrainPropKind Tree(Renderer renderer, TreeDesc desc, Material bark, Material leaves, bool impostor = true)
    {
        Material[] materials = [bark, leaves];
        GpuMesh full = Mesh(renderer, TreeGenerator.Build(desc, 0));
        List<LodLevel> levels = [new LodLevel(Mesh(renderer, TreeGenerator.Build(desc, 1)), 0.3f), new LodLevel(Mesh(renderer, TreeGenerator.Build(desc, 2)), 0.12f)];
        if (impostor)
        {
            ImpostorAsset asset = ImpostorBuilder.Bake(renderer, full, materials, new ImpostorBakeDesc { Frames = 12, CellSize = 128 }).CreateAsset(renderer);
            _impostors.Add(asset);
            levels.Add(asset.Level(0.05f));
        }
        return new TerrainPropKind { Mesh = full, Lods = new LodSet(levels.ToArray(), impostor ? 0f : 0.01f), Materials = materials };
    }

    private TerrainPropKind Rock(Renderer renderer, RockDesc desc, Material material)
    {
        GpuMesh full = Mesh(renderer, RockGenerator.Build(desc, 0));
        LodSet lods = new([new LodLevel(Mesh(renderer, RockGenerator.Build(desc, 1)), 0.15f), new LodLevel(Mesh(renderer, RockGenerator.Build(desc, 2)), 0.05f)], 0.006f);
        return new TerrainPropKind { Mesh = full, Lods = lods, Materials = [material] };
    }

    private Material Lit(Renderer renderer, Texture color, Texture normal, float roughness)
    {
        Material material = renderer.CreateMaterial(renderer.Standard.Lit);
        material.SetTexture("base_color_map", color);
        material.SetTexture("geometry_normal_map", normal);
        material.SetFloat("specular_roughness", roughness);
        _materials.Add(material);
        return material;
    }

    private Material Leaf(Renderer renderer, Shader shader, Texture card, Float3 color)
    {
        Material material = renderer.CreateMaterial(shader);
        material.Surface = new SurfaceSettings(SurfaceType.Cutout, Cull: SurfaceCull.Off);
        material.SetTexture("base_color_map", card);
        material.SetTexture("geometry_opacity_map", card);
        material.SetTextureChannel("geometry_opacity_map", 3);
        material.SetColor("base_color", new Float4(color.X, color.Y, color.Z, 1f));
        material.SetColor("translucency_color", new Float4(0.9f, 1f, 0.5f, 1f));
        _materials.Add(material);
        return material;
    }

    private GpuMesh Mesh(Renderer renderer, MeshData data)
    {
        GpuMesh mesh = renderer.CreateMesh(MeshOptimizer.Optimize(data));
        _meshes.Add(mesh);
        return mesh;
    }

    private Texture Upload(Renderer renderer, PropImage image)
    {
        Texture texture = image.Create(renderer);
        _textures.Add(texture);
        return texture;
    }
}
