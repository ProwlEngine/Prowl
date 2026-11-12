using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

[Collection("GPU Tests")]
public sealed class LodTests : IDisposable
{
    private const string Lit = """
        Shader "Test/Lit"
        {
            Pass { Tags { "LightMode" = "Prepass" } GLSLPROGRAM ENDGLSL }
            Pass { Tags { "LightMode" = "Forward" } GLSLPROGRAM ENDGLSL }
            Pass { Tags { "LightMode" = "ShadowCaster" } GLSLPROGRAM ENDGLSL }
        }
        """;

    private const int Forward = 1, Shadows = 2;

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly RenderStore _store = new();
    private readonly GlslShaderCompiler _compiler = TestCompiler.Shared;
    private readonly Uploader _uploader;
    private readonly GeometryPool _geometry;
    private readonly MaterialSystem _materials;
    private readonly RenderWorld _world;
    private readonly DrawPrograms _programs = new();
    private readonly BucketSet _buckets;
    private readonly Shader _lit;
    private readonly GpuMesh _fine, _middle, _coarse;
    private readonly DrawList _list = new();

    // Sees x and y from -10 to 10 and z from 0 to 10, objects sit at z 5.
    private static readonly CullFrustum s_frustum = new(Float4x4.CreateScale(0.1f), default);

    public LodTests()
    {
        InternalShaders.Register();
        _uploader = new Uploader(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl"));
        _geometry = new GeometryPool(_device, _store, 1 << 18, 1 << 18);
        _materials = new MaterialSystem(_device, _store, new NoResources());
        _world = new RenderWorld(_device, _store);
        _buckets = new BucketSet(_world.Render, _programs);
        _lit = new Shader(ShaderParser.Parse(Lit), _compiler);
        _fine = Mesh(9);
        _middle = Mesh(6);
        _coarse = Mesh(3);
    }

    public void Dispose()
    {
        _world.Dispose();
        _uploader.Dispose();
        _store.Dispose();
        _device.Dispose();
    }

    private GpuMesh Mesh(int vertices, float size = 1f)
    {
        Float3[] positions = Enumerable.Range(0, vertices).Select(i => new Float3(i % 3 == 1 ? size : 0, i % 3 == 2 ? size : 0, 0)).ToArray();
        GpuMesh mesh = _geometry.Create(new MeshData
        {
            VertexCount = (uint)vertices,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = Enumerable.Range(0, vertices).Select(i => (uint)i).ToArray(),
        });
        _geometry.Apply(_uploader, 0, long.MaxValue, new List<GpuMesh>());
        return mesh;
    }

    private LodSet Set(float middle = 0.5f, float coarse = 0.25f, float cull = 0f)
        => new([new LodLevel(_middle, middle), new LodLevel(_coarse, coarse)], cull);

    private RenderableHandle Add(LodSet? lods, float x = 0, float bias = 1, RenderableFlags flags = RenderableFlags.CastShadows)
        => _world.Add(new RenderableDesc { Mesh = _fine, Lods = lods, LodBias = bias, Transform = Double4x4.CreateTranslation(new Double3(x, 0, 5)), Flags = flags }, [_materials.Create(_lit)]);

    // A perspective view at the origin under which the renderable's projected diameter over the view height is size
    private LodView ViewAt(RenderableHandle sized, float size, LodState? state = null, int offset = 0, int forced = -1, float hysteresis = 0, float fade = 0, double now = 0)
    {
        Double3 center = _world.Render.WorldCenter[sized.Index];
        float distance = (float)Math.Sqrt(center.X * center.X + center.Y * center.Y + center.Z * center.Z);
        float scale = size * distance / _world.Render.WorldRadius[sized.Index];
        return new LodView(Double3.Zero, scale, Orthographic: false) { Offset = offset, Forced = forced, Hysteresis = hysteresis, State = state, FadeSeconds = fade, Now = now };
    }

    private void Extract()
    {
        List<Material> changed = new();
        _materials.Extract(changed);
        _world.Extract();
        _buckets.Update(_world.Render.DrawChanged, _world.Render.BoundsChanged, changed);
    }

    private DrawList Frame(int pass, Func<LodView> lod)
    {
        Extract();
        _buckets.Cull(s_frustum, ~0u, lod());
        _buckets.Build(pass, s_frustum, _list);
        return _list;
    }

    // The meshes the list draws, by their first chunk
    private GpuMesh[] Drawn(DrawList list)
    {
        GpuMesh[] meshes = [_fine, _middle, _coarse];
        return list.Records[..list.CommandCount].Select(r => meshes.First(m => m.Placement!.FirstChunk == r.Chunk)).ToArray();
    }

    [Fact]
    public void ALodSet_DrawsTheLevelItsScreenSizeFallsIn()
    {
        RenderableHandle handle = Add(Set());
        Extract();
        Assert.Equal([_fine], Drawn(Frame(Forward, () => ViewAt(handle, 0.8f))));
        Assert.Equal([_middle], Drawn(Frame(Forward, () => ViewAt(handle, 0.4f))));
        Assert.Equal([_coarse], Drawn(Frame(Forward, () => ViewAt(handle, 0.1f))));
    }

    [Fact]
    public void SwitchingLevel_RebuildsNoBuckets()
    {
        RenderableHandle handle = Add(Set());
        Frame(Forward, () => ViewAt(handle, 0.8f));
        int buckets = _buckets.BucketCount;
        Frame(Forward, () => ViewAt(handle, 0.1f));
        Assert.Empty(_world.Render.DrawChanged);
        Assert.Equal(buckets, _buckets.BucketCount);
    }

    [Fact]
    public void BelowTheCullSize_NothingDraws()
    {
        RenderableHandle handle = Add(Set(cull: 0.05f));
        Extract();
        Assert.Equal([_coarse], Drawn(Frame(Forward, () => ViewAt(handle, 0.06f))));
        Assert.Equal(0, Frame(Forward, () => ViewAt(handle, 0.04f)).CommandCount);
    }

    [Fact]
    public void WithoutASet_TheMeshDrawsAtAnySize()
    {
        RenderableHandle handle = Add(null);
        Extract();
        Assert.Equal([_fine], Drawn(Frame(Forward, () => ViewAt(handle, 0.01f))));
    }

    [Fact]
    public void Hysteresis_HoldsACoarserSwitch_UntilTheSizeFallsItsShareUnder()
    {
        RenderableHandle handle = Add(Set());
        LodState state = new();
        Extract();
        Assert.Equal([_fine], Drawn(Frame(Forward, () => ViewAt(handle, 0.6f, state, hysteresis: 0.1f))));
        Assert.Equal([_fine], Drawn(Frame(Forward, () => ViewAt(handle, 0.47f, state, hysteresis: 0.1f))));
        Assert.Equal([_middle], Drawn(Frame(Forward, () => ViewAt(handle, 0.44f, state, hysteresis: 0.1f))));
        Assert.Equal([_fine], Drawn(Frame(Forward, () => ViewAt(handle, 0.5f, state, hysteresis: 0.1f))));
    }

    [Fact]
    public void Hysteresis_NeedsTheViewToHaveSeenTheObject()
    {
        RenderableHandle handle = Add(Set());
        Extract();
        Assert.Equal([_middle], Drawn(Frame(Forward, () => ViewAt(handle, 0.47f, new LodState(), hysteresis: 0.1f))));
    }

    [Fact]
    public void TwoViews_KeepTheirOwnLevels()
    {
        RenderableHandle handle = Add(Set());
        LodState near = new(), far = new();
        Extract();
        Assert.Equal([_fine], Drawn(Frame(Forward, () => ViewAt(handle, 0.6f, near, hysteresis: 0.1f))));
        Assert.Equal([_coarse], Drawn(Frame(Forward, () => ViewAt(handle, 0.1f, far, hysteresis: 0.1f))));
        Assert.Equal([_fine], Drawn(Frame(Forward, () => ViewAt(handle, 0.47f, near, hysteresis: 0.1f))));
    }

    [Fact]
    public void Offsets_DrawCoarserLevels_NoFurtherThanTheLast()
    {
        RenderableHandle handle = Add(Set());
        Extract();
        Assert.Equal([_middle], Drawn(Frame(Forward, () => ViewAt(handle, 0.8f, offset: 1))));
        Assert.Equal([_coarse], Drawn(Frame(Forward, () => ViewAt(handle, 0.4f, offset: 5))));
    }

    [Fact]
    public void ForcedLevels_BeatTheSize_TheViewsBeforeTheRenderables()
    {
        RenderableHandle handle = Add(Set(cull: 0.05f));
        Extract();
        _world.ForceLod(handle, 2);
        Assert.Equal([_coarse], Drawn(Frame(Forward, () => ViewAt(handle, 0.8f))));
        Assert.Equal([_middle], Drawn(Frame(Forward, () => ViewAt(handle, 0.01f, forced: 1))));
        _world.ForceLod(handle, -1);
        Assert.Equal([_fine], Drawn(Frame(Forward, () => ViewAt(handle, 0.8f))));
    }

    [Fact]
    public void TheRenderablesBias_ScalesItsSize()
    {
        RenderableHandle handle = Add(Set(), bias: 2);
        Extract();
        Assert.Equal([_fine], Drawn(Frame(Forward, () => ViewAt(handle, 0.3f))));
        _world.SetLodBias(handle, 1);
        Assert.Equal([_middle], Drawn(Frame(Forward, () => ViewAt(handle, 0.3f))));
    }

    [Fact]
    public void SettingTheLods_ChangesWhatDraws()
    {
        RenderableHandle handle = Add(null);
        Extract();
        Assert.Equal([_fine], Drawn(Frame(Forward, () => ViewAt(handle, 0.1f))));
        _world.SetLods(handle, Set());
        Assert.Equal([_coarse], Drawn(Frame(Forward, () => ViewAt(handle, 0.1f))));
    }

    [Fact]
    public void OneMesh_AtDifferentLevels_DrawsOnceForEach()
    {
        RenderableHandle near = Add(Set());
        RenderableHandle far = Add(new LodSet([new LodLevel(_coarse, 0.9f)]), 1);
        Extract();
        DrawList list = Frame(Forward, () => ViewAt(near, 0.2f));
        Assert.Equal([_coarse], Drawn(list).Distinct());
        Assert.Equal(2, list.InstanceCount);
    }

    [Fact]
    public void Bounds_CoverEveryLevel()
    {
        GpuMesh large = Mesh(3, 4f);
        RenderableHandle handle = Add(new LodSet([new LodLevel(large, 0.5f)]));
        Extract();
        Assert.True(_world.Render.WorldRadius[handle.Index] > 2f);
    }

    [Fact]
    public void ALevelCastsNoShadowsWhenItSaysSo()
    {
        RenderableHandle handle = Add(new LodSet([new LodLevel(_middle, 0.5f, castShadows: false)]));
        Extract();
        Assert.Equal([_fine], Drawn(Frame(Shadows, () => ViewAt(handle, 0.8f))));
        Assert.Equal(0, Frame(Shadows, () => ViewAt(handle, 0.3f)).CommandCount);
        Assert.Equal([_middle], Drawn(Frame(Forward, () => ViewAt(handle, 0.3f))));
    }

    [Fact]
    public void ALodSet_RejectsLevelsWhoseSizesDoNotFall()
    {
        Assert.Throws<ArgumentException>(() => new LodSet([new LodLevel(_middle, 0.3f), new LodLevel(_coarse, 0.5f)]));
        Assert.Throws<ArgumentException>(() => new LodSet([]));
        Assert.Throws<ArgumentException>(() => new LodSet([new LodLevel(_middle, 0.3f)], cullScreenSize: 0.3f));
    }

    private const int Prepass = 0;

    // The instance words of each command, by the mesh it draws
    private (GpuMesh Mesh, ushort Program, uint[] Words)[] Commands(DrawList list)
    {
        GpuMesh[] meshes = [_fine, _middle, _coarse];
        List<(GpuMesh, ushort, uint[])> commands = new();
        foreach (DrawCall call in list.Calls)
        {
            for (int c = call.FirstCommand; c < call.FirstCommand + call.CommandCount; c++)
            {
                IndirectDrawIndexedArguments command = list.Commands[c];
                GpuMesh mesh = meshes.First(m => m.Placement!.FirstChunk == list.Records[c].Chunk);
                commands.Add((mesh, call.Program, list.Instances.AsSpan((int)list.Records[c].FirstInstance, (int)command.InstanceCount).ToArray()));
            }
        }
        return [.. commands];
    }

    [Fact]
    public void ACrossFade_DrawsBothLevels_TheOutgoingOneMarked()
    {
        RenderableHandle handle = Add(Set());
        LodState state = new();
        uint index = (uint)handle.Index;
        Extract();
        Frame(Prepass, () => ViewAt(handle, 0.8f, state, fade: 1, now: 0));
        Frame(Prepass, () => ViewAt(handle, 0.1f, state, fade: 1, now: 0));
        var commands = Commands(Frame(Prepass, () => ViewAt(handle, 0.1f, state, fade: 1, now: 0.5)));

        Assert.Equal(2, commands.Length);
        uint incoming = commands.Single(c => c.Mesh == _coarse).Words.Single();
        uint outgoing = commands.Single(c => c.Mesh == _fine).Words.Single();
        Assert.Equal(index, incoming & DrawList.ObjectMask);
        Assert.Equal(index, outgoing & DrawList.ObjectMask);
        Assert.InRange((int)((incoming >> 24) & 0x7F), 60, 67);
        Assert.Equal(0u, incoming >> 31);
        Assert.Equal(1u, outgoing >> 31);
        Assert.Equal(incoming & 0x7F000000u, outgoing & 0x7F000000u);
    }

    [Fact]
    public void ACrossFade_Ends_AfterItsSeconds()
    {
        RenderableHandle handle = Add(Set());
        LodState state = new();
        Extract();
        Frame(Prepass, () => ViewAt(handle, 0.8f, state, fade: 1, now: 0));
        Frame(Prepass, () => ViewAt(handle, 0.1f, state, fade: 1, now: 0.5));
        var commands = Commands(Frame(Prepass, () => ViewAt(handle, 0.1f, state, fade: 1, now: 1.6)));
        Assert.Equal((uint)handle.Index, Assert.Single(commands).Words.Single());
        Assert.Equal(_coarse, commands[0].Mesh);
    }

    [Fact]
    public void FadingDraws_UseTheirOwnPrograms_ForwardTestingDepthEqual()
    {
        RenderableHandle steady = Add(Set(), x: 1, bias: 0.1f);
        RenderableHandle fading = Add(Set());
        LodState state = new();
        Extract();
        Frame(Forward, () => ViewAt(fading, 0.1f, state, fade: 1, now: 0));
        Frame(Forward, () => ViewAt(fading, 0.8f, state, fade: 1, now: 0));
        DrawList prepass = Frame(Prepass, () => ViewAt(fading, 0.1f, state, fade: 1, now: 0.5));
        var prepassCommands = Commands(prepass);
        ushort steadyPrepass = prepassCommands.Single(c => c.Words.Contains((uint)steady.Index)).Program;
        ushort fadingPrepass = prepassCommands.Single(c => c.Mesh == _coarse && !c.Words.Contains((uint)steady.Index)).Program;
        Assert.NotEqual(steadyPrepass, fadingPrepass);
        Assert.True(_programs[fadingPrepass].LodFade);
        Assert.False(_programs[steadyPrepass].LodFade);

        state = new LodState();
        Frame(Forward, () => ViewAt(fading, 0.8f, state, fade: 1, now: 0));
        var forward = Commands(Frame(Forward, () => ViewAt(fading, 0.1f, state, fade: 1, now: 0.5)));
        ushort fadingForward = forward.Single(c => c.Mesh == _fine).Program;
        PassState? equal = _programs[fadingForward].StateOverride;
        Assert.NotNull(equal);
        Assert.Equal(ComparisonKind.Equal, equal!.DepthStencil.DepthComparison);
        Assert.False(equal.DepthStencil.DepthWriteEnabled);
    }

    [Fact]
    public void WithoutFadeSeconds_TheLevelPops()
    {
        RenderableHandle handle = Add(Set());
        LodState state = new();
        Extract();
        Frame(Prepass, () => ViewAt(handle, 0.8f, state, fade: 0, now: 0));
        Assert.Equal([_coarse], Drawn(Frame(Prepass, () => ViewAt(handle, 0.1f, state, fade: 0, now: 0.5))));
    }

    [Fact]
    public void OtherPasses_DrawOnlyTheIncomingLevel()
    {
        RenderableHandle handle = Add(Set());
        LodState state = new();
        Extract();
        Frame(Shadows, () => ViewAt(handle, 0.8f, state, fade: 1, now: 0));
        var commands = Commands(Frame(Shadows, () => ViewAt(handle, 0.1f, state, fade: 1, now: 0.5)));
        Assert.Equal((uint)handle.Index, Assert.Single(commands).Words.Single());
        Assert.Equal(_coarse, commands[0].Mesh);
    }

    [Fact]
    public void ARenderableTheViewDidNotSee_DoesNotFadeIn()
    {
        RenderableHandle handle = Add(Set());
        Extract();
        Assert.Equal([_coarse], Drawn(Frame(Prepass, () => ViewAt(handle, 0.1f, new LodState(), fade: 1, now: 0.5))));
    }

    [Fact]
    public void ALevelWithItsOwnMaterial_DrawsEverySubmeshWithIt()
    {
        Material own = _materials.Create(_lit);
        RenderableHandle handle = _world.Add(new RenderableDesc
        {
            Mesh = _fine,
            Lods = new LodSet([new LodLevel(_coarse, 0.5f, material: own)]),
            Transform = Double4x4.CreateTranslation(new Double3(0, 0, 5)),
        }, [_materials.Create(_lit)]);
        Extract();

        DrawList near = Frame(Forward, () => ViewAt(handle, 0.8f));
        Assert.Equal([_fine], Drawn(near));
        Assert.Equal(0u, near.Records[0].Flags & DrawRecord.DirectSlot);

        DrawList far = Frame(Forward, () => ViewAt(handle, 0.1f));
        Assert.Equal([_coarse], Drawn(far));
        Assert.Equal(DrawRecord.DirectSlot, far.Records[0].Flags & DrawRecord.DirectSlot);
        Assert.Equal(own.Slot, far.Records[0].SubMesh);
    }
}
