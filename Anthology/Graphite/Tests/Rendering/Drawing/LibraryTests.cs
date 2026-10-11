using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

file sealed class NoResources : IMaterialResources
{
    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName) => 0;

    public uint SamplerSlot(SamplerDescription? sampler) => 0;
}

file readonly struct NoView : IRenderView
{
    public Framebuffer? Target => null;
    public uint PixelWidth => 8;
    public uint PixelHeight => 8;
    public int ViewId => 0;
}

file sealed class LambdaPass(string name, Action<RenderContextBuilder> setup, Action<RenderContext, CommandBuffer> render) : IPass
{
    public string Name => name;

    public void Setup(RenderContextBuilder builder) => setup(builder);

    public void Render(RenderContext context, CommandBuffer cmd) => render(context, cmd);
}

[StructLayout(LayoutKind.Sequential)]
file struct Push
{
    public uint DrawRecordBase;
    public uint InstanceBase;
    public uint ViewBase;
    public uint Flags;
}

public sealed class LibraryTests : IDisposable
{
    private const string Source = """
        Shader "Test/Library"
        {
            Properties
            {
                _Color("Tint", Color) = (1, 1, 1, 1)
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec4 vColor;
                    layout(location = 1) out vec2 vUV;

                    void main()
                    {
                        gl_Position = Graphite_ObjectToClip(vertexPosition);
                        vColor = vertexColor;
                        vUV = vertexUV0;
                    }
                }
                Fragment
                {
                    #include "Graphite/Core.glsl"
                    uniform vec4 _Color;
                    layout(location = 0) in vec4 vColor;
                    layout(location = 1) in vec2 vUV;
                    layout(location = 0) out vec4 outColor;

                    void main()
                    {
                        outColor = _Color * vColor + Graphite_Object().custom0 + vec4(vUV, 0.0, 0.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        _device.WaitForIdle();
        for (int i = _owned.Count - 1; i >= 0; i--)
            _owned[i].Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    private static MeshData Quad(bool withColor)
    {
        Float3[] positions = [new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0)];
        List<VertexStream> streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }];
        if (withColor)
        {
            Float4[] colors = Enumerable.Repeat(new Float4(0.5f, 0.5f, 0.5f, 1), 4).ToArray();
            streams.Add(new VertexStream { Semantic = VertexSemantic.Color, Format = VertexElementFormat.Float4, Data = MemoryMarshal.AsBytes(colors.AsSpan()).ToArray() });
        }
        return new MeshData { VertexCount = 4, Streams = [.. streams], Indices = new uint[] { 0, 1, 2, 0, 2, 3 } };
    }

    [Fact]
    public void MaterialPasses_DrawTheBucketsThroughTheLibrary_FarFromTheOrigin_InOneMultiDrawCall()
    {
        InternalShaders.Register();
        Uploader uploader = Own(new Uploader(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl")));
        GeometryPool geometry = Own(new GeometryPool(_device, 1 << 16, 1 << 16));
        MaterialSystem materials = Own(new MaterialSystem(_device, new NoResources()));
        GlobalRegistry globals = Own(new GlobalRegistry(_device, new NoResources()));
        RenderWorld world = Own(new RenderWorld(_device));
        DrawPrograms programs = new();
        BucketSet buckets = new(world.Render, programs);

        GpuMesh white = geometry.Create(Quad(false));
        GpuMesh gray = geometry.Create(Quad(true));
        geometry.Apply(uploader, 0, long.MaxValue, new List<GpuMesh>());

        ShaderDefinition definition = ShaderParser.Parse(Source);
        Shader shader = new(definition, _compiler);
        Material red = materials.Create(shader);
        red.SetColor("_Color", new Float4(1, 0, 0, 1));
        Material green = materials.Create(shader);
        green.SetColor("_Color", new Float4(0, 1, 0, 1));

        Double3 camera = new(10_000_000, 500, -3_000_000);
        world.Add(new RenderableDesc { Mesh = white, Transform = Double4x4.CreateTranslation(camera + new Double3(-1, 0, 2)) }, [red]);
        world.Add(new RenderableDesc { Mesh = gray, Transform = Double4x4.CreateTranslation(camera + new Double3(1, 0, 2)), Custom0 = new Float4(0, 0, 0.25f, 0) }, [green]);

        List<Material> changed = new();
        materials.Extract(changed);
        world.Extract();
        buckets.Update(world.Render.DrawChanged, changed);

        Float4x4 projection = new(new Float4(0.5f, 0, 0, 0), new Float4(0, 0.5f, 0, 0), new Float4(0, 0, 0, 0), new Float4(0, 0, 0.5f, 1));
        DrawList list = new();
        CullFrustum frustum = new(projection, camera);
        buckets.Cull(frustum, ~0u);
        buckets.Build(1, frustum, list);
        Assert.Equal(2, list.CommandCount);
        DrawCall call = Assert.Single(list.Calls);

        ResourceLayoutDescription frameLayout = GraphiteBindings.FrameTable(16, 4);
        ResourceLayoutDescription viewLayout = GraphiteBindings.ViewTable(4);
        ShaderPass pass = definition.Passes![programs[call.Program].PassIndex];
        ShaderDescription description = MaterialRewrite.Rewrite(shader.Name, _compiler.Compile(pass, [], _device.BackendType), shader.Schema, _compiler.GetGlobals(pass), globals, frameLayout, viewLayout);
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        GraphicsProgram program = Own(_device.ResourceFactory.CreateGraphicsProgram(description));

        DeviceBuffer Upload<T>(ReadOnlySpan<T> data, BufferUsage usage) where T : unmanaged
        {
            DeviceBuffer buffer = Own(_device.ResourceFactory.CreateBuffer(new BufferDescription((uint)Math.Max(16, data.Length * Marshal.SizeOf<T>()), usage)));
            _device.UpdateBuffer(buffer, 0, data);
            return buffer;
        }

        DeviceBuffer instances = Upload<uint>(list.Instances.AsSpan(0, list.InstanceCount), BufferUsage.StructuredBufferReadOnly);
        DeviceBuffer records = Upload<DrawRecord>(list.Records.AsSpan(0, list.CommandCount), BufferUsage.StructuredBufferReadOnly);
        DeviceBuffer commands = Upload<IndirectDrawIndexedArguments>(list.Commands.AsSpan(0, list.CommandCount), BufferUsage.IndirectBuffer);
        ViewBlock viewBlock = ViewBlock.Create(Float4x4.Identity, projection, camera, 8, 8, 0.1f, 1000);
        DeviceBuffer views = Upload<ViewBlock>([viewBlock], BufferUsage.StructuredBufferReadOnly);
        DeviceBuffer globalsBuffer = Upload<uint>(new uint[4], BufferUsage.StructuredBufferReadOnly);

        GeometryPage page = geometry.Pages[call.Page];
        RenderWorld.RenderSide render = world.Render;
        GpuTable materialTable = red.Table!.Records;
        DeviceBuffer[] tables = [geometry.Chunks.Buffer, geometry.Meshes.Buffer, page.Vertices, page.Indices, render.Transforms.Buffer, render.PreviousTransforms.Buffer,
            render.Attributes.Buffer, render.Bounds.Buffer, render.MaterialSlots.Buffer, materialTable.Buffer];
        materials.Flush(uploader, 0);
        render.Flush(uploader, new List<DeviceBuffer>());
        geometry.Apply(uploader, 0, long.MaxValue, new List<GpuMesh>());

        DescriptorTable frame = Own(_device.ResourceFactory.CreateDescriptorTable(frameLayout));
        frame.SetBuffer("Graphite_Chunks", 0, geometry.Chunks.Buffer);
        frame.SetBuffer("Graphite_Transforms", 0, render.Transforms.Buffer);
        frame.SetBuffer("Graphite_PreviousTransforms", 0, render.PreviousTransforms.Buffer);
        frame.SetBuffer("Graphite_Objects", 0, render.Attributes.Buffer);
        frame.SetBuffer("Graphite_MaterialSlots", 0, render.MaterialSlots.Buffer);
        DescriptorTable view = Own(_device.ResourceFactory.CreateDescriptorTable(viewLayout));
        view.SetBuffer("Graphite_Globals", 0, globalsBuffer);
        view.SetBuffer("Graphite_Views", 0, views);

        PropertySet passProperties = new();
        passProperties.SetBuffer(GraphiteBindings.MaterialsName, materialTable.Buffer);
        passProperties.SetBuffer(GraphiteBindings.InstancesName, instances);
        passProperties.SetBuffer(GraphiteBindings.DrawRecordsName, records);
        passProperties.SetBuffer(GraphiteBindings.VerticesName, page.Vertices);

        Texture target = Own(_device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(8, 8, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled)));
        Framebuffer framebuffer = Own(_device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, target)));
        VertexSource vertexSource = new VertexSource(PrimitiveTopology.TriangleList).SetIndexBuffer(page.Indices, IndexFormat.UInt32, page.Indices.SizeInBytes / 4);

        using RenderPipeline pipeline = new([
            new LambdaPass("Upload", b =>
            {
                for (int i = 0; i < tables.Length; i++)
                    b.DeclareImportedBuffer(RenderResourceID.Intern("Table" + i), tables[i], BufferAccess.ShaderWrite);
            }, (context, cmd) => uploader.Record(cmd, context.Task)),
            new LambdaPass("Draw", b =>
            {
                for (int i = 0; i < tables.Length; i++)
                    b.DeclareImportedBuffer(RenderResourceID.Intern("Table" + i), tables[i], BufferAccess.ShaderRead);
            }, (context, cmd) =>
            {
                cmd.SetFramebuffer(framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
                cmd.SetFullViewport();
                cmd.SetShader(program);
                cmd.SetVertexSource(vertexSource);
                cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame);
                cmd.SetDescriptorTable(GraphiteBindings.ViewSet, view);
                cmd.SetProperties(passProperties);
                cmd.SetPushConstants(new Push { DrawRecordBase = (uint)call.FirstCommand });
                cmd.DrawIndexedIndirect(commands, (uint)(call.FirstCommand * Marshal.SizeOf<IndirectDrawIndexedArguments>()), (uint)call.CommandCount, (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>());
            })]);
        _device.DispatchGraph([new GraphDispatch(pipeline, [new NoView()])]);
        _device.WaitForIdle();

        DeviceBuffer readback = Own(_device.ResourceFactory.CreateBuffer(new BufferDescription(8 * 8 * 16, BufferUsage.Staging)));
        _device.Record(cmd => cmd.CopyTextureToBuffer(target, readback, 0, TextureRegion.Whole(target, 0, 0))).Wait();
        Float4[] pixels = _device.Map<Float4>(readback).ToArray();
        _device.Unmap(readback);

        Assert.Equal(new Float4(1, 0, 0, 1), pixels[4 * 8 + 2]);
        Assert.Equal(new Float4(0, 128 / 255f, 0.25f, 1), pixels[4 * 8 + 5]);
        Assert.Equal(new Float4(0, 0, 0, 1), pixels[4 * 8 + 0]);
        Assert.Equal(new Float4(0, 0, 0, 1), pixels[4 * 8 + 4]);
    }
}
