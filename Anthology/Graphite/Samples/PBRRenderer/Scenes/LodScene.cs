using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.Rendering;
using Prowl.Graphite.Rendering.Tools;
using Prowl.Vector;

using Silk.NET.Input;


namespace Prowl.Graphite.Samples.PBRRenderer;


/// <summary>A forest of high poly trees, each with simplified levels from LodBuilder and a baked impostor last.</summary>
internal sealed class LodScene : DemoScene
{
    private const int Side = 316;
    private const float Spacing = 8f;

    private readonly List<GpuMesh> _meshes = new();
    private readonly ImpostorAsset _impostor;
    private readonly int _levels;

    public LodScene(Demo demo) : base(demo)
    {
        Material bark = Pbr(new Float3(0.35f, 0.22f, 0.12f), 0f, 0.85f);
        Material leaves = Pbr(new Float3(0.18f, 0.45f, 0.12f), 0f, 0.7f);
        Material[] materials = [bark, leaves];

        MeshData tree = Tree();
        GpuMesh full = Mesh(tree);
        LodChain chain = LodBuilder.Build(tree, new LodBuildOptions { Ratios = [0.5f, 0.25f, 0.1f] });
        ImpostorData baked = ImpostorBuilder.Bake(demo.Renderer, full, materials, new ImpostorBakeDesc { Frames = 12, CellSize = 128 });
        _impostor = baked.CreateAsset(demo.Renderer);

        float[] sizes = [0.3f, 0.15f, 0.07f];
        List<LodLevel> levels = new();
        for (int i = 0; i < chain.Levels.Count; i++)
            levels.Add(new LodLevel(Mesh(chain.Levels[i]), sizes[i]));
        levels.Add(_impostor.Level(0.035f));
        LodSet lods = new(levels.ToArray());
        _levels = levels.Count + 1;

        Add(Demo.Cube, Pbr(new Float3(0.3f, 0.32f, 0.25f), 0f, 0.9f), new Double3(0, -0.25, 0), new Double3(5200, 0.5, 5200));
        Random random = new(7);
        float half = Side * Spacing * 0.5f;
        for (int z = 0; z < Side; z++)
        {
            for (int x = 0; x < Side; x++)
            {
                Double3 position = new(x * Spacing - half + random.NextSingle() * 4 - 2, 0, -z * Spacing + random.NextSingle() * 4 - 2);
                Quaternion turn = Quaternion.AxisAngle(new Float3(0, 1, 0), random.NextSingle() * MathF.PI * 2);
                World.Add(new RenderableDesc
                {
                    Mesh = full,
                    Lods = lods,
                    Name = $"Tree {x} {z}",
                    Transform = Double4x4.CreateTRS(position, turn, new Double3(1, 1, 1) * (0.8 + random.NextDouble() * 0.4)),
                    Flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows,
                }, materials);
            }
        }

        LightHandle sun = World.AddLight(new LightDesc
        {
            Name = "Sun",
            Type = LightType.Directional,
            Direction = Float3.Normalize(new Float3(0.45f, -0.6f, 0.5f)),
            Color = new Float3(1f, 0.92f, 0.8f),
            Intensity = 100000f,
            AngularRadius = 0.5f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 4096, Cascades = 4, Distance = 200 },
        });
        World.Environment = new EnvironmentDesc
        {
            Ambient = AmbientMode.Sky,
            AmbientIntensity = 0.5f,
            Sun = sun,
            Sky = Track(demo.Renderer.CreateMaterial(demo.Renderer.Skies.Procedural)),
            Fog = new FogDesc { Mode = FogMode.ExponentialSquared, Density = 0.0006f, UseSky = true },
        };
    }

    public override string Name => "LOD";

    public override Double3 StartPosition => new(0, 6, 20);

    public override float StartYaw => MathF.PI;

    public override float StartPitch => -0.05f;

    public override string Status => $"{Side * Side:N0} trees, {World.Lod.Transition}, bias {World.Lod.Bias:0.00}, {(View.ForcedLod < 0 ? "levels by size" : $"forced level {View.ForcedLod}")}";

    public override string Controls => "L cross fade or pop, K force level, [ ] bias";

    public override void Update(float time, float deltaTime)
    {
    }

    public override void OnKey(Key key)
    {
        switch (key)
        {
            case Key.L: World.Lod.Transition = World.Lod.Transition == LodTransition.CrossFade ? LodTransition.Pop : LodTransition.CrossFade; break;
            case Key.K: View.ForcedLod = View.ForcedLod + 1 < _levels ? View.ForcedLod + 1 : -1; break;
            case Key.LeftBracket: World.Lod.Bias /= 1.25f; break;
            case Key.RightBracket: World.Lod.Bias *= 1.25f; break;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _impostor.Dispose();
        foreach (GpuMesh mesh in _meshes)
            mesh.Dispose();
    }

    private GpuMesh Mesh(MeshData data)
    {
        GpuMesh mesh = Demo.Renderer.CreateMesh(data);
        _meshes.Add(mesh);
        return mesh;
    }

    // A trunk and three lumpy canopy spheres, about twenty thousand triangles, wound counter clockwise from outside, the trunk submesh 0 and the canopy 1
    private static MeshData Tree()
    {
        List<Float3> positions = new(), normals = new();
        List<Float2> uvs = new();
        List<uint> trunk = new(), canopy = new();

        const int segments = 16;
        for (int x = 0; x <= segments; x++)
        {
            float theta = x / (float)segments * MathF.PI * 2;
            Float3 n = new(MathF.Cos(theta), 0, MathF.Sin(theta));
            foreach (float y in new[] { 0f, 3.2f })
            {
                float radius = y == 0 ? 0.35f : 0.2f;
                positions.Add(n * radius + new Float3(0, y, 0));
                normals.Add(n);
                uvs.Add(new Float2(x / (float)segments, y / 3.2f));
            }
        }
        for (uint x = 0; x < segments; x++)
            trunk.AddRange([x * 2, x * 2 + 1, x * 2 + 3, x * 2, x * 2 + 3, x * 2 + 2]);

        (Float3 Center, float Radius)[] blobs = [(new(0, 3.6f, 0), 1.8f), (new(0.6f, 4.8f, 0.3f), 1.4f), (new(-0.4f, 5.7f, -0.2f), 1.0f)];
        const int rings = 40, around = 80;
        foreach ((Float3 center, float radius) in blobs)
        {
            uint first = (uint)positions.Count;
            for (int y = 0; y <= rings; y++)
            {
                float phi = y / (float)rings * MathF.PI;
                for (int x = 0; x <= around; x++)
                {
                    float theta = x / (float)around * MathF.PI * 2;
                    Float3 n = new(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                    float lump = 1 + 0.12f * MathF.Sin(theta * 7 + phi * 3) * MathF.Sin(phi * 5) + 0.05f * MathF.Sin(theta * 23) * MathF.Sin(phi * 17);
                    positions.Add(center + n * radius * lump);
                    normals.Add(n);
                    uvs.Add(new Float2(x / (float)around, y / (float)rings));
                }
            }
            uint stride = around + 1;
            for (uint y = 0; y < rings; y++)
            {
                for (uint x = 0; x < around; x++)
                {
                    uint a = first + y * stride + x;
                    canopy.AddRange([a, a + 1, a + stride + 1, a, a + stride + 1, a + stride]);
                }
            }
        }

        uint[] indices = [.. trunk, .. canopy];
        return new MeshData
        {
            VertexCount = (uint)positions.Count,
            Streams =
            [
                Stream(VertexSemantic.Position, VertexElementFormat.Float3, positions),
                Stream(VertexSemantic.Normal, VertexElementFormat.Float3, normals),
                Stream(VertexSemantic.UV0, VertexElementFormat.Float2, uvs),
            ],
            Indices = indices,
            SubMeshes = [new SubMesh { IndexStart = 0, IndexCount = (uint)trunk.Count }, new SubMesh { IndexStart = (uint)trunk.Count, IndexCount = (uint)canopy.Count }],
        };
    }

    private static VertexStream Stream<T>(VertexSemantic semantic, VertexElementFormat format, List<T> data) where T : struct
        => new() { Semantic = semantic, Format = format, Data = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(data)).ToArray() };
}
