// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Veldrid;


namespace Prowl.Runtime.Rendering;

public struct ShaderPipelineDescription : IEquatable<ShaderPipelineDescription>
{
    public ShaderPass pass;
    public ShaderVariant variant;

    public OutputDescription? output;

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(pass, variant, output);
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is not ShaderPipelineDescription other)
            return false;

        return Equals(other);
    }

    public bool Equals(ShaderPipelineDescription other)
    {
        return
            pass == other.pass &&
            variant == other.variant &&
            output.Equals(other.output);
    }
}

public sealed partial class ShaderPipeline : IDisposable, IBindableResourceProvider
{
    public static readonly FrontFace FrontFace = Graphics.GetFrontFace();

    public readonly ShaderVariant shader;

    public readonly ShaderSetDescription shaderSet;
    public readonly ResourceLayout resourceLayout;

    private readonly Dictionary<string, uint> _semanticLookup;
    private readonly Dictionary<string, uint> _bufferLookup;

    private readonly byte _bufferCount;

    public IReadOnlyList<Uniform> Uniforms => shader.Uniforms;

    private GraphicsPipelineDescription _description;

    private static readonly int s_pipelineCount = 20; // 20 possible combinations (5 topologies, 2 fill modes, 2 scissor modes)
    private readonly Pipeline[] _pipelines;


    public Pipeline GetPipeline(PolygonFillMode fill, PrimitiveTopology topology, bool scissor)
    {
        int index = (int)topology * 4 + (int)fill * 2 + (scissor ? 0 : 1);

        if (_pipelines[index] == null)
        {
            _description.RasterizerState.ScissorTestEnabled = scissor;
            _description.RasterizerState.FillMode = fill;
            _description.PrimitiveTopology = topology;

            _pipelines[index] = Graphics.Factory.CreateGraphicsPipeline(_description);
        }

        return _pipelines[index];
    }


    public ShaderPipeline(ShaderPipelineDescription description)
    {
        shader = description.variant;

        ShaderDescription[] shaderDescriptions = shader.GetProgramsForBackend();

        // Create shader set description
        Veldrid.Shader[] shaders = new Veldrid.Shader[shaderDescriptions.Length];

        _semanticLookup = new();

        for (int shaderIndex = 0; shaderIndex < shaders.Length; shaderIndex++)
            shaders[shaderIndex] = Graphics.Factory.CreateShader(shaderDescriptions[shaderIndex]);

        VertexLayoutDescription[] vertexLayouts = new VertexLayoutDescription[shader.VertexInputs.Length];

        for (int inputIndex = 0; inputIndex < vertexLayouts.Length; inputIndex++)
        {
            VertexInput input = shader.VertexInputs[inputIndex];

            // Add in_var_ to match reflected name in SPIRV-Cross generated GLSL.
            vertexLayouts[inputIndex] = new VertexLayoutDescription(
                new VertexElementDescription("in_var_" + input.semantic, input.format, VertexElementSemantic.TextureCoordinate));

            _semanticLookup[input.semantic] = (uint)inputIndex;
        }

        shaderSet = new ShaderSetDescription(vertexLayouts, shaders);

        // Create resource layout and uniform lookups
        _bufferLookup = new();

        ResourceLayoutDescription layoutDescription = new ResourceLayoutDescription(
            new ResourceLayoutElementDescription[Uniforms.Count]);

        for (ushort uniformIndex = 0; uniformIndex < Uniforms.Count; uniformIndex++)
        {
            Uniform uniform = Uniforms[uniformIndex];
            ShaderStages stages = shader.UniformStages[uniformIndex];

            layoutDescription.Elements[uniform.binding] =
                new ResourceLayoutElementDescription(uniform.name, uniform.kind, stages);

            if (uniform.kind != ResourceKind.UniformBuffer)
                continue;

            _bufferLookup[uniform.name] = Pack(uniformIndex, _bufferCount);
            _bufferCount++;
        }

        resourceLayout = Graphics.Factory.CreateResourceLayout(layoutDescription);

        _pipelines = new Pipeline[s_pipelineCount];

        RasterizerStateDescription rasterizerState = new(
            description.pass.CullMode,
            PolygonFillMode.Solid,
            FrontFace,
            description.pass.DepthClipEnabled,
            false
        );

        _description = new(
            description.pass.Blend,
            description.pass.DepthStencilState,
            rasterizerState,
            PrimitiveTopology.LineList,
            shaderSet,
            [resourceLayout],
            description.output ?? Graphics.ScreenTarget.OutputDescription);
    }


    private static BindableResource GetBindableResource(Uniform uniform, out DeviceBuffer? buffer)
    {
        buffer = null;

        if (uniform.kind == ResourceKind.TextureReadOnly)
            return Texture2D.White.Res.InternalTexture;

        if (uniform.kind == ResourceKind.TextureReadWrite)
            return Texture2D.EmptyRW.Res.InternalTexture;

        if (uniform.kind == ResourceKind.Sampler)
            return Graphics.Device.PointSampler;

        if (uniform.kind == ResourceKind.StructuredBufferReadOnly)
            return GraphicsBuffer.Empty.Buffer;

        if (uniform.kind == ResourceKind.StructuredBufferReadWrite)
            return GraphicsBuffer.EmptyRW.Buffer;

        uint bufferSize = (uint)Math.Ceiling(uniform.size / (double)16) * 16;
        buffer = Graphics.Factory.CreateBuffer(new BufferDescription(bufferSize, BufferUsage.UniformBuffer | BufferUsage.DynamicWrite));

        return buffer;
    }


    public BindableResourceSet CreateResources()
    {
        BindableResource[] boundResources = new BindableResource[Uniforms.Count];

        DeviceBuffer[] boundBuffers = new DeviceBuffer[_bufferCount];
        byte[][] intermediateBuffers = new byte[_bufferCount][];

        for (int i = 0, b = 0; i < Uniforms.Count; i++)
        {
            boundResources[Uniforms[i].binding] = GetBindableResource(Uniforms[i], out DeviceBuffer? buffer);

            if (buffer != null)
            {
                boundBuffers[b] = buffer;
                intermediateBuffers[b] = new byte[buffer.SizeInBytes];

                b++;
            }
        }

        ResourceSetDescription setDescription = new ResourceSetDescription(resourceLayout, boundResources);
        BindableResourceSet resources = new BindableResourceSet(this, setDescription, boundBuffers, intermediateBuffers);

        return resources;
    }


    public bool GetBufferIndex(string name, out ushort uniform, out ushort buffer)
    {
        uniform = 0;
        buffer = 0;

        if (_bufferLookup.TryGetValue(name, out uint packed))
        {
            Unpack(packed, out uniform, out buffer);
            return true;
        }

        return false;
    }


    private static uint Pack(ushort a, ushort b)
        => ((uint)a << 16) | b;

    private static void Unpack(uint packed, out ushort a, out ushort b)
        => (a, b) = ((ushort)(packed >> 16), (ushort)(packed & ushort.MaxValue));


    public void BindVertexBuffer(CommandList list, string semantic, DeviceBuffer buffer, uint offset = 0)
    {
        if (_semanticLookup.TryGetValue(semantic, out uint location))
            list.SetVertexBuffer(location, buffer, offset);
    }


    public void Dispose()
    {
        for (int i = 0; i < shaderSet.Shaders.Length; i++)
            shaderSet.Shaders[i]?.Dispose();

        for (int i = 0; i < _pipelines.Length; i++)
            _pipelines[i]?.Dispose();

        resourceLayout?.Dispose();
    }
}
