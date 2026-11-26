using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

using static Prowl.Graphite.Rendering.Tests.RendererHarness;

namespace Prowl.Graphite.Rendering.Tests;

// Host features in a view's slots: which run, in what order, and what they draw onto
[Collection("GPU Tests")]
public sealed class FeatureTests : IDisposable
{
    // Colours with the object's Custom0, shifting each instance of a shared draw a quarter of the view along X
    private const string ObjectColorSource = """
        Shader "Test/ObjectColor"
        {
            Properties
            {
                _Shift("Shift", Float) = 0
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    uniform float _Shift;
                    void main()
                    {
                        vec3 p = vertexPosition + vec3(float(Graphite_InstanceIndex()) * _Shift, 0.0, 0.0);
                        gl_Position = Graphite_ObjectToClip(p);
                    }
                }
                Fragment
                {
                    #include "Graphite/Core.glsl"
                    #include "Graphite/Passes.glsl"
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        outColor = vec4(Graphite_Object().custom0.rgb, 1.0);
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private readonly RendererHarness _harness = new();
    private readonly GraphicsDevice _device;
    private readonly RendererOptions _options;
    private readonly Renderer _renderer;
    private readonly RenderWorld _world;
    private readonly Shader _unlit;
    private readonly GpuMesh _quad;
    private readonly List<RendererError> _errors;
    private readonly List<IDisposable> _owned;

    public FeatureTests()
    {
        (_device, _options, _renderer, _world, _errors, _owned) = (_harness.Device, _harness.Options, _harness.Renderer, _harness.World, _harness.Errors, _harness.Owned);
        _unlit = _renderer.LoadShader(ShaderParser.Parse(UnlitSource(exposed: false)));
        _quad = _harness.CreateQuad();
    }

    public void Dispose() => _harness.Dispose();

    private RenderableHandle AddQuad(Float3 color, uint layer = ~0u, double scale = 1)
    {
        Material material = _renderer.CreateMaterial(_unlit);
        material.SetColor("_Color", new Float4(color.X, color.Y, color.Z, 1));
        return _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(scale), LayerMask = layer }, [material]);
    }

    private static bool Centre(int i) => i % Size is >= 4 and < 12 && i / Size is >= 4 and < 12;

    private RenderView View(int id = 1) => _harness.View(id);

    private Float4[] Frame(params RenderView[] views) => _harness.Frame(views);

    private FullscreenProgram Program(string fragment)
    {
        FullscreenProgram program = _renderer.CreateFullscreenProgram(fragment);
        _owned.Add(program);
        return program;
    }

    // A fullscreen program writing the next colour, with the order and slot given
    private sealed class Effect(FullscreenProgram program, RenderSlot slot, int order = 0, PropertySet? properties = null) : RenderFeature(slot, order: order)
    {
        public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteColor();
        public override void Render(FeatureContext context) => context.Fullscreen(program, properties);
    }

    // Shows the scene's motion in red and green, read after the opaque pass
    private sealed class ShowMotion(FullscreenProgram program, Sampler sampler) : RenderFeature(RenderSlot.AfterOpaques, sceneReads: SceneTargets.Motion)
    {
        private readonly PropertySet _properties = new();

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.ReadScene();
            builder.WriteColor();
        }

        public override void Render(FeatureContext context)
        {
            _properties.SetTexture("_Motion", context.Motion!, sampler);
            context.Fullscreen(program, _properties);
        }
    }

    private void ShowingMotion()
    {
        Sampler point = _device.ResourceFactory.CreateSampler(SamplerDescription.Point);
        _owned.Add(point);
        _world.Features.Add(new ShowMotion(Program("uniform sampler2D _Motion; void main() { outColor = vec4(texture(_Motion, vUV).rg, 0.0, 1.0); }"), point));
    }

    private RenderableHandle AddMoving(Double3 at, bool motionVectors)
    {
        Material material = _renderer.CreateMaterial(_unlit);
        RenderableFlags flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows | (motionVectors ? RenderableFlags.MotionVectors : 0);
        return _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(at), Flags = flags }, [material]);
    }

    // Copies one scene target right after the prepass, for ShowPrepassCopy to show at the end of the view
    private sealed class PrepassCopy(FullscreenProgram program, Sampler sampler, SceneTargets target) : RenderFeature(RenderSlot.AfterPrepass, sceneReads: target)
    {
        private sealed class State { public TextureHandle Copy; }

        private readonly PropertySet _properties = new();

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.ReadScene();
            State state = builder.GetViewState<State>();
            state.Copy = builder.Temporary("Copy", view.Texture(PixelFormat.R32_G32_B32_A32_Float));
            builder.Publish("PrepassCopy", state.Copy);
        }

        public override void Render(FeatureContext context)
        {
            _properties.SetTexture("_Target", target == SceneTargets.Motion ? context.Motion! : context.Normals!, sampler);
            context.Fullscreen(program, context.Get(context.GetViewState<State>().Copy), _properties);
        }
    }

    private sealed class ShowPrepassCopy(FullscreenProgram program, Sampler sampler) : RenderFeature(RenderSlot.HdrPost)
    {
        private sealed class State { public TextureHandle Copy; }

        private readonly PropertySet _properties = new();

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.GetViewState<State>().Copy = builder.Read("PrepassCopy");
            builder.WriteColor();
        }

        public override void Render(FeatureContext context)
        {
            _properties.SetTexture("_Copy", context.Get(context.GetViewState<State>().Copy).ColorTextures[0], sampler);
            context.Fullscreen(program, _properties);
        }
    }

    // The test quad wound to face the camera for the standard shaders, which cull back faces, its normal toward it
    private GpuMesh FacingQuad()
    {
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        Float3[] normals = [new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1)];
        GpuMesh mesh = _renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.AsSpan()).ToArray() },
            ],
            Indices = new uint[] { 0, 2, 1, 0, 3, 2 },
        });
        _owned.Add(mesh);
        return mesh;
    }

    private Material Unlit(Float3 color)
    {
        Material material = _renderer.CreateMaterial(_unlit);
        material.SetColor("_Color", new Float4(color.X, color.Y, color.Z, 1));
        return material;
    }

    // A mesh with the bone streams given and a triangle list over its vertices in order
    private GpuMesh SkinnedMesh(Float3[] positions, ushort[] boneIndices, ushort[] boneWeights, Float3[]? normals = null, Float4[]? tangents = null, BlendShapeData[]? shapes = null)
    {
        uint count = (uint)positions.Length;
        List<VertexStream> streams =
        [
            new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.BoneIndices, Format = VertexElementFormat.UShort4, Data = MemoryMarshal.AsBytes(boneIndices.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.BoneWeights, Format = VertexElementFormat.UShort4_Norm, Data = MemoryMarshal.AsBytes(boneWeights.AsSpan()).ToArray() },
        ];
        if (normals != null)
            streams.Add(new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.AsSpan()).ToArray() });
        if (tangents != null)
            streams.Add(new VertexStream { Semantic = VertexSemantic.Tangent, Format = VertexElementFormat.Float4, Data = MemoryMarshal.AsBytes(tangents.AsSpan()).ToArray() });
        uint[] indices = [.. Enumerable.Range(0, (int)(count / 3 * 3)).Select(i => (uint)i)];
        GpuMesh mesh = _renderer.CreateMesh(new MeshData { VertexCount = count, Streams = [.. streams], Indices = indices, BlendShapes = shapes ?? [] });
        _owned.Add(mesh);
        return mesh;
    }

    // The quad as two triangles, every corner on bone 1
    private GpuMesh SkinnedQuad()
    {
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        ushort[] indices = new ushort[positions.Length * 4];
        ushort[] weights = new ushort[positions.Length * 4];
        for (int v = 0; v < positions.Length; v++)
            (indices[v * 4], weights[v * 4]) = (1, 65535);
        return SkinnedMesh(positions, indices, weights);
    }

    private static Float4x4 Translation(float x, float y, float z) => new(new Float4(1, 0, 0, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 1, 0), new Float4(x, y, z, 1));

    private static Float3 Transform(in Float4x4 m, Float3 p, float w)
        => new(m.c0.X * p.X + m.c1.X * p.Y + m.c2.X * p.Z + m.c3.X * w,
               m.c0.Y * p.X + m.c1.Y * p.Y + m.c2.Y * p.Z + m.c3.Y * w,
               m.c0.Z * p.X + m.c1.Z * p.Y + m.c2.Z * p.Z + m.c3.Z * w);

    // Prowl's skinning: bone index 0 is none, as are indices past the bones; under 0.01 total weight keeps the rest pose
    private static Float3 CpuSkin(Float3 rest, float w, ReadOnlySpan<ushort> indices, ReadOnlySpan<ushort> weights, Float4x4[] bones)
    {
        Float3 sum = default;
        float total = 0;
        for (int i = 0; i < 4; i++)
        {
            float weight = weights[i] / 65535f;
            total += weight;
            if (indices[i] > 0 && weight > 0 && indices[i] <= bones.Length)
                sum += Transform(bones[indices[i] - 1], rest, w) * weight;
        }
        return total < 0.01f ? rest : sum;
    }

    // A stream of the renderable's deformed copy, read back from its page
    private float[] DeformedStream(RenderableHandle handle, VertexSemantic semantic)
    {
        MeshPlacement placement = _world.Render.Deformed[handle.Index]!;
        uint words = MeshLayout.WordsPerVertex(semantic) * placement.VertexCount;
        byte[]? bytes = null;
        _renderer.Readback(_renderer.Frame.Geometry.Pages[placement.Page].Vertices, data => bytes = data.Data, offset: placement.StreamOffsets[(int)semantic] * 4, size: words * 4);
        _renderer.SubmitFrame(new FrameDesc());
        _device.WaitForIdle();
        _renderer.SubmitFrame(new FrameDesc());
        Assert.NotNull(bytes);
        return MemoryMarshal.Cast<byte, float>(bytes).ToArray();
    }

    [Fact]
    public void Skinning_MatchesACpuSkinOfTheSameMesh()
    {
        Random random = new(7);
        const int count = 96;
        Float3[] positions = new Float3[count], normals = new Float3[count];
        Float4[] tangents = new Float4[count];
        ushort[] indices = new ushort[count * 4], weights = new ushort[count * 4];
        for (int v = 0; v < count; v++)
        {
            positions[v] = new Float3(random.NextSingle() * 4 - 2, random.NextSingle() * 4 - 2, random.NextSingle());
            normals[v] = new Float3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, -1);
            tangents[v] = new Float4(1, random.NextSingle() - 0.5f, 0, random.Next(2) * 2 - 1);
            for (int i = 0; i < 4; i++)
            {
                // Bones 1 to 5 of 5, with 0 and 6 meaning none, and every tenth vertex unweighted
                indices[v * 4 + i] = (ushort)random.Next(0, 7);
                weights[v * 4 + i] = v % 10 == 0 ? (ushort)0 : (ushort)random.Next(0, 65536 / 4);
            }
        }
        Float4x4[] bones = new Float4x4[5];
        for (int b = 0; b < bones.Length; b++)
        {
            float angle = random.NextSingle() * 3, c = MathF.Cos(angle), s = MathF.Sin(angle);
            bones[b] = new Float4x4(new Float4(c, s, 0, 0), new Float4(-s, c, 0, 0), new Float4(0, 0, 1.5f, 0), new Float4(random.NextSingle(), random.NextSingle(), random.NextSingle(), 1));
        }

        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = SkinnedMesh(positions, indices, weights, normals, tangents), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-8), new Float3(8)));
        _world.SetBones(handle, bones);
        Frame(View());

        float[] skinnedPositions = DeformedStream(handle, VertexSemantic.Position);
        float[] skinnedNormals = DeformedStream(handle, VertexSemantic.Normal);
        float[] skinnedTangents = DeformedStream(handle, VertexSemantic.Tangent);
        for (int v = 0; v < count; v++)
        {
            ReadOnlySpan<ushort> vi = indices.AsSpan(v * 4, 4), vw = weights.AsSpan(v * 4, 4);
            Float3 position = CpuSkin(positions[v], 1, vi, vw, bones);
            Float3 normal = CpuSkin(normals[v], 0, vi, vw, bones);
            Float3 tangent = CpuSkin(new Float3(tangents[v].X, tangents[v].Y, tangents[v].Z), 0, vi, vw, bones);
            normal /= MathF.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
            tangent /= MathF.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y + tangent.Z * tangent.Z);
            for (int c = 0; c < 3; c++)
            {
                Assert.True(MathF.Abs(skinnedPositions[v * 3 + c] - position[c]) < 1e-4f, $"vertex {v} position {c}: {skinnedPositions[v * 3 + c]} against {position[c]}");
                Assert.True(MathF.Abs(skinnedNormals[v * 3 + c] - normal[c]) < 1e-4f, $"vertex {v} normal {c}: {skinnedNormals[v * 3 + c]} against {normal[c]}");
                Assert.True(MathF.Abs(skinnedTangents[v * 4 + c] - tangent[c]) < 1e-4f, $"vertex {v} tangent {c}: {skinnedTangents[v * 4 + c]} against {tangent[c]}");
            }
            Assert.Equal(tangents[v].W, skinnedTangents[v * 4 + 3]);
        }
    }

    // Prowl's frame pick: one frame scales by weight over its weight, several interpolate between the two around the
    // weight, clamped to the last, with near zero weights and pairs left out
    private static List<(BlendShapeFrame Frame, float Weight)> CpuActiveFrames(BlendShapeData[] shapes, float[] weights)
    {
        List<(BlendShapeFrame, float)> active = new();
        void Add(BlendShapeFrame frame, float weight)
        {
            if (MathF.Abs(weight) >= 1e-5f)
                active.Add((frame, weight));
        }
        for (int s = 0; s < shapes.Length; s++)
        {
            float w = weights[s];
            BlendShapeFrame[] frames = shapes[s].Frames;
            if (MathF.Abs(w) < 1e-4f || frames.Length == 0)
                continue;
            float first = frames[0].Weight, last = frames[^1].Weight;
            if (frames.Length == 1 || w <= first)
            {
                Add(frames[0], first != 0 ? w / first : 0);
                continue;
            }
            if (w >= last)
            {
                Add(frames[^1], 1);
                continue;
            }
            int i = 0;
            while (i < frames.Length - 1 && frames[i + 1].Weight <= w)
                i++;
            float a = frames[i].Weight, b = frames[i + 1].Weight;
            float t = b > a ? (w - a) / (b - a) : 0;
            Add(frames[i], 1 - t);
            Add(frames[i + 1], t);
        }
        return active;
    }

    private static Float3 CpuMorph(Float3 rest, int vertex, List<(BlendShapeFrame Frame, float Weight)> active, Func<BlendShapeFrame, Float3[]?> deltas)
    {
        foreach ((BlendShapeFrame frame, float weight) in active)
        {
            int at = Array.IndexOf(frame.Vertices, (uint)vertex);
            if (at >= 0 && deltas(frame) is { } d)
                rest += d[at] * weight;
        }
        return rest;
    }

    private static BlendShapeFrame RandomFrame(Random random, float weight, int vertexCount, int moved)
    {
        uint[] vertices = [.. Enumerable.Range(0, vertexCount).OrderBy(_ => random.Next()).Take(moved).Select(v => (uint)v)];
        Float3 Next() => new(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f);
        return new BlendShapeFrame
        {
            Weight = weight,
            Vertices = vertices,
            Positions = [.. vertices.Select(_ => Next())],
            Normals = [.. vertices.Select(_ => Next())],
            Tangents = [.. vertices.Select(_ => Next())],
        };
    }

    [Fact]
    public void BlendShapes_MorphBeforeSkinning_AsProwlDoes()
    {
        Random random = new(11);
        const int count = 60;
        Float3[] positions = new Float3[count], normals = new Float3[count];
        Float4[] tangents = new Float4[count];
        ushort[] indices = new ushort[count * 4], weights = new ushort[count * 4];
        for (int v = 0; v < count; v++)
        {
            positions[v] = new Float3(random.NextSingle() * 4 - 2, random.NextSingle() * 4 - 2, random.NextSingle());
            normals[v] = new Float3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, -1);
            tangents[v] = new Float4(1, random.NextSingle() - 0.5f, 0, 1);
            (indices[v * 4], weights[v * 4]) = ((ushort)(v % 2 + 1), 65535);
        }

        // Frames that overlap on vertices, so the frames must add up one after another
        BlendShapeData[] shapes =
        [
            new BlendShapeData { Name = "Single", Frames = [RandomFrame(random, 100, count, 40)] },
            new BlendShapeData { Name = "Steps", Frames = [RandomFrame(random, 25, count, 40), RandomFrame(random, 50, count, 40), RandomFrame(random, 100, count, 40)] },
            new BlendShapeData { Name = "Idle", Frames = [RandomFrame(random, 100, count, 40)] },
        ];
        float[] shapeWeights = [60, 37.5f, 0];
        Float4x4[] bones = [Translation(0.5f, 0, 0), new Float4x4(new Float4(0, 1, 0, 0), new Float4(-1, 0, 0, 0), new Float4(0, 0, 1, 0), new Float4(0, 0, 0.25f, 1))];

        GpuMesh mesh = SkinnedMesh(positions, indices, weights, normals, tangents, shapes);
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-8), new Float3(8)));
        _world.SetBlendShapeWeights(handle, shapeWeights);
        _world.SetBones(handle, bones);
        Frame(View());

        List<(BlendShapeFrame Frame, float Weight)> active = CpuActiveFrames(shapes, shapeWeights);
        Assert.Equal(3, active.Count);
        float[] gpuPositions = DeformedStream(handle, VertexSemantic.Position);
        float[] gpuNormals = DeformedStream(handle, VertexSemantic.Normal);
        float[] gpuTangents = DeformedStream(handle, VertexSemantic.Tangent);
        for (int v = 0; v < count; v++)
        {
            ReadOnlySpan<ushort> vi = indices.AsSpan(v * 4, 4), vw = weights.AsSpan(v * 4, 4);
            Float3 position = CpuSkin(CpuMorph(positions[v], v, active, f => f.Positions), 1, vi, vw, bones);
            Float3 normal = CpuSkin(CpuMorph(normals[v], v, active, f => f.Normals), 0, vi, vw, bones);
            Float3 tangent = CpuSkin(CpuMorph(new Float3(tangents[v].X, tangents[v].Y, tangents[v].Z), v, active, f => f.Tangents), 0, vi, vw, bones);
            normal /= MathF.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
            tangent /= MathF.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y + tangent.Z * tangent.Z);
            for (int c = 0; c < 3; c++)
            {
                Assert.True(MathF.Abs(gpuPositions[v * 3 + c] - position[c]) < 1e-4f, $"vertex {v} position {c}: {gpuPositions[v * 3 + c]} against {position[c]}");
                Assert.True(MathF.Abs(gpuNormals[v * 3 + c] - normal[c]) < 1e-4f, $"vertex {v} normal {c}: {gpuNormals[v * 3 + c]} against {normal[c]}");
                Assert.True(MathF.Abs(gpuTangents[v * 4 + c] - tangent[c]) < 1e-4f, $"vertex {v} tangent {c}: {gpuTangents[v * 4 + c]} against {tangent[c]}");
            }
        }
    }

    private RenderableHandle AddSkinned(bool motionVectors, uint layer = ~0u)
    {
        RenderableFlags flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows | (motionVectors ? RenderableFlags.MotionVectors : 0);
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = SkinnedQuad(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)), Flags = flags, LayerMask = layer }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-16), new Float3(16)));
        _world.SetBones(handle, [Float4x4.Identity]);
        return handle;
    }

    [Fact]
    public void SkinnedMotion_StartsAtNothingWhenNoViewSawTheLastPose()
    {
        ShowingMotion();
        RenderableHandle handle = AddSkinned(motionVectors: true);
        RenderView view = View();
        view.CullingMask = 1;
        Frame(view);
        _world.SetLayerMask(handle, 2);
        _world.SetBones(handle, [Translation(0, 1, 0)]);
        Frame(view);

        _world.SetLayerMask(handle, ~0u);
        _world.SetBones(handle, [Translation(0, 2, 0)]);
        AssertAll(new Float3(0, 0, 0), Frame(view));

        ShowingMotion();
        RenderableHandle moved = AddMoving(new Double3(0, 0, 10), motionVectors: false);
        Frame(View());
        _world.SetTransform(moved, Double4x4.CreateTranslation(new Double3(0, 1, 10)));
        AssertAll(new Float3(0, 0, 0), Frame(View()));

        ShowingMotion();
        RenderableHandle handleUnflagged = AddSkinned(motionVectors: false);
        Frame(View());
        _world.SetBones(handleUnflagged, [Translation(0, 1, 0)]);
        AssertAll(new Float3(0, 0, 0), Frame(View()));

        ShowingMotion();
        RenderableHandle first = AddSkinned(motionVectors: true);
        Frame(View());
        _world.SetBones(first, [Translation(0, 1, 0)]);
        Frame(View());

        // The same mesh, so the slot keeps its copy, posed back down
        GpuMesh mesh = _world.Render.Mesh[first.Index]!;
        _world.Remove(first);
        RenderableHandle second = _world.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)), Flags = RenderableFlags.MotionVectors }, [Unlit(new Float3(1, 1, 1))]);
        Assert.Equal(first.Index, second.Index);
        _world.SetBounds(second, new AABB(new Float3(-16), new Float3(16)));
        _world.SetBones(second, [Float4x4.Identity]);
        AssertAll(new Float3(0, 0, 0), Frame(View()));
    }

    private sealed class Failing(string message) : RenderFeature(RenderSlot.Overlay)
    {
        public override void Setup(FeatureBuilder builder, in FeatureView view) => throw new InvalidOperationException(message);

        public override void Render(FeatureContext context) { }
    }

    // A frame that fails, a feature of its view throwing as it is built, after its resources were applied. Each message is reported once.
    private void FailedFrame(string message = "failing on purpose")
    {
        RenderView view = View();
        view.Features = new ExtensionSet<RenderFeature> { new Failing(message) };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        _device.WaitForIdle();
        Assert.Contains(_errors, e => e.Code == "frame.failed");
        _errors.Clear();
    }

    [Fact]
    public void AFailedFrame_KeepsTheUploadsItApplied()
    {
        Frame(View());
        RenderableHandle quad = AddQuad(new Float3(0.25f, 0.5f, 1));
        FailedFrame();
        AssertAll(new Float3(0.25f, 0.5f, 1), Frame(View()));
        _world.Remove(quad);

        GpuMesh mesh = SkinnedQuad();
        Frame(View());
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5) }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-16), new Float3(16)));
        _world.SetBones(handle, [Translation(4, 0, 0)]);
        FailedFrame("failing again");
        Float4[] pixels = Frame(View());
        AssertAll(new Float3(1, 1, 1), pixels, i => i % Size is >= 8 and < 16 && i / Size is >= 4 and < 12);
        AssertAll(new Float3(0, 0, 0), pixels, i => i % Size < 8);
    }

    [Fact]
    public void TheScene_HasOnlyTheTargetsItsFeaturesRead()
    {
        _harness.ViewOf(1).Path = RenderPath.Deferred;
        AddQuad(new Float3(1, 1, 1));
        int Targets() => ViewGraph.SceneTargetCount(_renderer.Frame.Camera(_harness.ViewOf(1))!.ShapeKey.Targets);
        Frame(View());
        Assert.Equal(1, Targets());

        TemporalAntiAliasing taa = new(_renderer);
        _world.Features.Add(taa);
        Frame(View());
        Assert.Equal(2, Targets());

        ScreenSpaceReflections ssr = new(_renderer);
        _world.Features.Add(ssr);
        Frame(View());
        Assert.Equal(5, Targets());

        _world.Features.Remove(ssr);
        _world.Features.Remove(taa);
        AssertAll(new Float3(1, 1, 1), Frame(View()));
        Assert.Equal(1, Targets());
    }

    [Fact]
    public void Jitter_CyclesThroughHaltonPhases_OnlyWhileAFeatureAsks()
    {
        AddQuad(new Float3(1, 1, 1));
        Frame(View());
        Assert.Equal(default, _renderer.LastFrameStats.View(_harness.ViewOf(1))!.Jitter);

        TemporalAntiAliasing taa = new(_renderer) { JitterSpread = 4 };
        _world.Features.Add(taa);
        List<Float2> jitters = new();
        for (int frame = 0; frame < 5; frame++)
        {
            Frame(View());
            jitters.Add(_renderer.LastFrameStats.View(_harness.ViewOf(1))!.Jitter);
        }

        // Within half a pixel, four different offsets, then the first again
        Assert.All(jitters, j => Assert.True(MathF.Abs(j.X) <= 1f / Size && MathF.Abs(j.Y) <= 1f / Size, $"jitter {j}"));
        Assert.Equal(4, jitters.Take(4).Distinct().Count());
        Assert.Equal(jitters[0], jitters[4]);
    }

    [Fact]
    public void Taa_ConvergesAStillEdgeToItsCoverage()
    {
        // A white plane from a quarter into the middle column onwards: pixel 8 spans 0 to 0.5, three quarters covered
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(4.125, 0, 10)) }, [Unlit(new Float3(1, 1, 1))]);
        TemporalAntiAliasing taa = new(_renderer) { Sharpness = 0, JitterSpread = 8 };
        _world.Features.Add(taa);
        Float4[] pixels = [];
        for (int frame = 0; frame < 120; frame++)
            pixels = Frame(View());
        for (int y = 0; y < Size; y++)
        {
            Assert.InRange(pixels[y * Size + 8].X, 0.6f, 0.9f);
            Assert.True(pixels[y * Size + 7].X < 0.01f && pixels[y * Size + 9].X > 0.99f, $"row {y}: {pixels[y * Size + 7].X}, {pixels[y * Size + 9].X}");
        }
    }

    [Fact]
    public void Views_DrawByOrder_AfterTheViewsTheyDependOn()
    {
        AddQuad(new Float3(1, 0, 0), layer: 1);
        AddQuad(new Float3(0, 1, 0), layer: 2);
        RenderView red = View(1);
        red.CullingMask = 1;
        RenderView green = View(2);
        green.CullingMask = 2;

        // The last view drawn covers the target
        AssertAll(new Float3(0, 1, 0), Frame(red, green));
        red.Order = 1;
        AssertAll(new Float3(1, 0, 0), Frame(red, green));
        green.DependsOn.Add(_harness.ViewOf(1));
        AssertAll(new Float3(0, 1, 0), Frame(red, green));
    }

    private sealed class Named(string name, RenderSlot slot, int order = 0, ViewKinds runsOn = ViewKinds.Cameras, FeatureGroup? group = null) : RenderFeature(slot, order: order, group: group, runsOn: runsOn)
    {
        public override void Setup(FeatureBuilder builder, in FeatureView view) { }
        public override void Render(FeatureContext context) { }
        public override string ToString() => name;
    }

    // Names the built in draw features by their group, as PrepassFeature, OpaqueFeature, SkyFeature and TransparentFeature
    private List<string> Select(RenderFeature[] world, RenderFeature[] view, ViewKind kind = ViewKind.Game)
    {
        foreach (RenderFeature feature in world.Concat(view))
            feature.Snapshot();
        ViewDesc desc = new() { Kind = kind };
        List<RenderFeature> result = new();
        FeatureSelection.Select(result, _renderer.Frame.BuiltIns, world, view, new FeatureView(desc, 1, 1));
        return result.Select(f => f is Named ? f.ToString()! : f.Name).ToList();
    }

    private static FeatureGroup Group(string name) => (FeatureGroup)typeof(FeatureGroup).GetField(name)!.GetValue(null)!;

    private sealed class Throws(bool inSetup) : RenderFeature(RenderSlot.HdrPost)
    {
        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            if (inSetup)
                throw new InvalidOperationException("setup");
            builder.WriteColor();
        }

        public override void Render(FeatureContext context) => throw new InvalidOperationException("render");
    }

    // Inverts the colour once told to, which changes what it declares
    private sealed class Toggle(FullscreenProgram program) : RenderFeature(RenderSlot.HdrPost)
    {
        public bool On;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            if (On)
                builder.WriteColor();
        }

        public override void Render(FeatureContext context)
        {
            if (On)
                context.Fullscreen(program);
        }
    }

    // Adds a quarter to what it held last frame, and publishes it
    private sealed class Accumulate(FullscreenProgram program) : RenderFeature(RenderSlot.HdrPost)
    {
        private readonly PropertySet _properties = new();
        private TextureHandle _total;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.ReadColor();
            _total = builder.History("Total", view.Texture(PixelFormat.R16_G16_B16_A16_Float));
            builder.Publish("Total", _total);
        }

        public override void Render(FeatureContext context)
        {
            RenderTexture? previous = context.History(_total);
            context.Command.SetFramebuffer(context.Get(_total).Framebuffer, new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded));
            context.Command.SetFullViewport();
            context.Command.SetShader(program.For(1));
            context.Command.SetVertexSource(VertexSource.None);
            _properties.SetTexture("Graphite_Source", context.Color!);
            _properties.SetTexture("_Previous", previous?.ColorTextures[0] ?? context.Color!);
            _properties.SetFloat("_HasPrevious", previous != null ? 1 : 0);
            context.Command.SetProperties(_properties);
            context.Command.Draw(3);
        }
    }

    // Shows what an earlier feature published
    private sealed class Show(FullscreenProgram program) : RenderFeature(RenderSlot.Tonemap)
    {
        private readonly PropertySet _properties = new();
        private TextureHandle _shown;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            _shown = builder.Read("Total");
            builder.WriteColor();
        }

        public override void Render(FeatureContext context)
        {
            _properties.SetTexture("_Shown", context.Get(_shown).ColorTextures[0]);
            context.Fullscreen(program, _properties);
        }
    }

    [Fact]
    public void HistoryTextures_HoldWhatTheFeatureWroteLastFrame()
    {
        _world.Features.Add(new Accumulate(Program("uniform sampler2D _Previous; uniform float _HasPrevious; void main() { outColor = vec4(texture(_Previous, vUV).rgb * _HasPrevious + 0.25, 1.0); }")));
        _world.Features.Add(new Show(Program("uniform sampler2D _Shown; void main() { outColor = texture(_Shown, vUV) + 0.0 * Graphite_SampleSource(vUV); }")));
        AssertAll(new Float3(0.25f, 0.25f, 0.25f), Frame(View()));
        AssertAll(new Float3(0.5f, 0.5f, 0.5f), Frame(View()));
        AssertAll(new Float3(0.75f, 0.75f, 0.75f), Frame(View()));
    }

    // A fullscreen effect whose pass measures its GPU time
    private sealed class Timed(FullscreenProgram program, GpuTimer timer) : RenderFeature(RenderSlot.HdrPost)
    {
        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.WriteColor();
            builder.MeasureGpuTime(timer);
        }

        public override void Render(FeatureContext context) => context.Fullscreen(program);
    }

    private sealed class Counter
    {
        public int Frames;
    }

    private sealed class Counts() : RenderFeature(RenderSlot.Overlay)
    {
        public readonly Dictionary<RenderView, int> Seen = new();

        public override void Setup(FeatureBuilder builder, in FeatureView view) { }
        public override void Render(FeatureContext context) => Seen[context.View.RenderView!] = ++context.GetViewState<Counter>().Frames;
    }

    [Fact]
    public void Readbacks_OfTexturesAndBuffers_CompleteOnALaterSubmit()
    {
        AddQuad(new Float3(0.25f, 0.5f, 1));
        using DeviceBuffer buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));
        _device.UpdateBuffer<uint>(buffer, 0, [1, 2, 3, 4]);

        TextureReadback? texture = null;
        byte[]? bytes = null;
        _renderer.Readback(_harness.Target, data => texture = data);
        _renderer.Readback(buffer, data => bytes = data.Data, offset: 4, size: 8);
        Frame(View());
        Assert.Null(texture);

        _renderer.SubmitFrame(new FrameDesc());
        Assert.NotNull(texture);
        Assert.Equal((16u, 16u, PixelFormat.R32_G32_B32_A32_Float), (texture!.Value.Width, texture.Value.Height, texture.Value.Format));
        Float4 pixel = MemoryMarshal.Cast<byte, Float4>(texture.Value.Data)[5 * Size + 7];
        Assert.Equal(new Float4(0.25f, 0.5f, 1, 1), pixel);
        Assert.Equal([2u, 3u], MemoryMarshal.Cast<byte, uint>(bytes).ToArray());
    }

    private Material ObjectColor(float shift = 0)
    {
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(ObjectColorSource)));
        material.SetFloat("_Shift", shift);
        return material;
    }

    // Pixels whose centre lies inside a two unit square centred at x, y
    private static Func<int, bool> Inside(float x, float y) => i =>
    {
        float px = 4 * ((i % Size + 0.5f) / Size * 2 - 1), py = 4 * ((i / Size + 0.5f) / Size * 2 - 1);
        return MathF.Abs(px - x) < 1 && MathF.Abs(py - y) < 1;
    };

    // The eight unit quad shrunk to two units, centred at x, y
    private static Float4x4 At(float x, float y) => Float4x4.CreateTranslation(new Float3(x, y, 0)) * Float4x4.CreateScale(0.25f);

    private sealed class ComputedItems(GpuMesh mesh, Material material) : RenderProvider
    {
        public override void Cull(in ProviderView view, DrawItemSink sink)
            => sink.AddIndirect(mesh, 0, material, new DrawItem { Transform = Double4x4.CreateTranslation(new Double3(-2, -2, 10)) * Double4x4.CreateScale(0.25), Custom0 = new Float4(1, 0, 0, 1) }, "Test.ComputedCount", 4);
    }

    // Writes its count on the GPU each frame through a compute dispatch of its own
    private sealed class ComputedCount(ComputeProgram program) : FrameFeature(FrameSlot.BeforeViews)
    {
        private readonly PropertySet _properties = new();
        private BufferHandle _count;
        public uint Instances;

        public override void Setup(FrameBuilder builder)
        {
            _count = builder.Buffer("Test.ComputedCount", GraphBufferDesc.Structured(4, 4));
            builder.Export(_count);
        }

        public override void Render(FrameContext context)
        {
            _properties.SetBuffer("Counts", context.Get(_count));
            context.Command.SetComputeShader(program);
            context.Command.SetProperties(_properties);
            context.Command.SetPushConstants(Instances);
            context.Command.Dispatch(1, 1, 1);
        }
    }

    [Fact]
    public void GpuCounts_AProviderComputes_DrawInTheSameFrame()
    {
        const string source = """
            Compute
            {
                layout(local_size_x = 1) in;
                layout(set = 0, binding = 0, std430) buffer Counts { uint words[]; };
                layout(push_constant) uniform Push { uint count; } push;
                void main() { words[1] = push.count; }
            }
            """;
        ComputeProgram program = _device.ResourceFactory.CreateComputeProgram(TestCompiler.Shared.CompileCompute(new ShaderPass { State = new PassState(), Source = source }, [], _device.BackendType));
        _owned.Add(program);

        // A world object draws first, so the provider's commands are not the view's first
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(2, 2, 10)) * Double4x4.CreateScale(0.25), Custom0 = new Float4(0, 0, 1, 1) }, [ObjectColor()]);
        ComputedCount count = new(program) { Instances = 1 };
        _world.FrameFeatures.Add(count);
        _world.Providers.Add(new ComputedItems(_quad, ObjectColor(shift: 16)));

        Float4[] pixels = Frame(View());
        AssertAll(new Float3(1, 0, 0), pixels, Inside(-2, -2));
        AssertAll(new Float3(0, 0, 0), pixels, Inside(2, -2));
        AssertAll(new Float3(0, 0, 1), pixels, Inside(2, 2));

        count.Instances = 2;
        pixels = Frame(View());
        AssertAll(new Float3(1, 0, 0), pixels, Inside(2, -2));
        count.Instances = 0;
        pixels = Frame(View());
        AssertAll(new Float3(0, 0, 0), pixels, Inside(-2, -2));
        AssertAll(new Float3(0, 0, 1), pixels, Inside(2, 2));
    }

    [Fact]
    public void Selection_ViewBeatsWorldBeatsBuiltIn_ForGroupsAndReplacements()
    {
        Named worldSky = new("world sky", RenderSlot.Sky, group: FeatureGroup.Sky);
        Named viewSky = new("view sky", RenderSlot.Sky, group: FeatureGroup.Sky);
        Assert.DoesNotContain("SkyFeature", Select([worldSky], []));
        Assert.Contains("world sky", Select([worldSky], []));
        List<string> both = Select([worldSky], [viewSky]);
        Assert.Contains("view sky", both);
        Assert.DoesNotContain("world sky", both);

        Named opaque = new("opaque", RenderSlot.Opaque, group: FeatureGroup.Opaque);
        Assert.DoesNotContain("OpaqueFeature", Select([opaque], []));
        Assert.Contains("opaque", Select([opaque], []));

        AddQuad(new Float3(1, 0, 0));
        (RenderSlot Slot, string Group)[] viewGroups = [(RenderSlot.Present, "Present"), (RenderSlot.Clear, "SceneClear"), (RenderSlot.Shadows, "Cascades")];
        foreach ((RenderSlot slot, string group) in viewGroups)
        {
            ViewStand stand = new(slot, group, scene: slot == RenderSlot.Clear);
            _world.Features.Add(stand);
            Float4[] pixels = Frame(View());

            Assert.True(stand.Setups > 0);
            List<RenderFeature> selected = _renderer.Frame.Camera(_harness.ViewOf(1))!.Selected;
            Assert.Contains(stand, selected);
            Assert.DoesNotContain(selected, f => f.Group == Group(group) && f != stand);
            if (slot == RenderSlot.Present)
                AssertAll(new Float3(0, 0, 0), pixels);
            else if (slot != RenderSlot.Clear)
                AssertAll(new Float3(1, 0, 0), pixels, Centre);
            _world.Features.Remove(stand);
        }

        (string Group, FrameSlot Slot, bool InWorld)[] frameGroups =
        [
            ("Deform", FrameSlot.Deform, false),
            ("LightProbes", FrameSlot.LightProbes, true),
            ("LocalShadows", FrameSlot.Shadows, true),
            ("CaptureFill", FrameSlot.Captures, true),
            ("Readback", FrameSlot.Readback, false),
        ];
        foreach ((string group, FrameSlot slot, bool inWorld) in frameGroups)
        {
            FrameStand stand = new(slot, group, ProvidedBy(group));
            ExtensionSet<FrameFeature> features = inWorld ? _world.FrameFeatures : _renderer.FrameFeatures;
            features.Add(stand);
            AssertAll(new Float3(1, 0, 0), Frame(View()), Centre);

            FrameFeature[] active = [.. _renderer.Frame.ActiveFrameFeatures];
            Assert.Contains(stand, active);
            Assert.DoesNotContain(active, f => f.IsBuiltIn && f.Group == Group(group));
            Assert.Equal(1, stand.Runs);
            features.Remove(stand);
        }
    }

    // Takes a built in frame feature's group and provides what its consumers read
    private static RenderResourceID? ProvidedBy(string group) => group switch
    {
        "Deform" => RendererOutputs.DeformedVertices,
        "LightProbes" => RendererOutputs.ObjectProbes,
        "LocalShadows" => RendererOutputs.ShadowPages,
        "CaptureFill" => RendererOutputs.CaptureCubes,
        _ => RendererOutputs.ReadbackStaging,
    };

    private sealed class FrameStand(FrameSlot slot, string group, RenderResourceID? provides) : FrameFeature(slot, group: Group(group))
    {
        public int Runs;
        public override void Setup(FrameBuilder builder)
        {
            if (provides is { } id)
                builder.Export(builder.Buffer(id, GraphBufferDesc.Structured(4, 4)));
        }
        public override void Render(FrameContext context) => Runs++;
    }

    private sealed class ViewStand(RenderSlot slot, string group, bool scene = false) : RenderFeature(slot, group: Group(group), runsOn: ViewKinds.All)
    {
        public int Setups;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            Setups++;
            if (scene)
            {
                GraphTextureDesc desc = new() { Width = (int)view.Width, Height = (int)view.Height, ColorFormats = [PixelFormat.R16_G16_B16_A16_Float], EnableDepth = true, DepthFormat = PixelFormat.D32_Float, ArrayLayers = view.LayerCount > 1 ? (uint)view.LayerCount : 0 };
                builder.Publish(RendererOutputs.Scene, builder.Temporary("Scene", desc, TextureState.Attachment));
            }
            else if (group == "Present")
            {
                builder.ReadColor();
            }
        }

        public override void Render(FeatureContext context) { }
    }
}
