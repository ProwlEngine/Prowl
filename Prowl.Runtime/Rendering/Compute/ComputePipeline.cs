// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Veldrid;

namespace Prowl.Runtime.Rendering;


public sealed partial class ComputePipeline : IDisposable, IBindableResourceProvider
{
    public readonly ComputeVariant variant;

    public readonly ShaderSetDescription shaderSet;
    public readonly ResourceLayout resourceLayout;

    private readonly Dictionary<string, uint> _bufferLookup;

    private readonly byte _bufferCount;

    public IReadOnlyList<Uniform> Uniforms => variant.Uniforms;

    private ComputePipelineDescription _description;

    private Pipeline _pipeline;

    public Pipeline GetPipeline() => _pipeline;

    public ComputePipeline(ComputeVariant variant)
    {
        this.variant = variant;

        ShaderDescription shaderDescription = variant.GetProgramForBackend();

        Veldrid.Shader shader = Graphics.Factory.CreateShader(shaderDescription);

        shaderSet = new ShaderSetDescription(null, [shader]);

        // Create resource layout and uniform lookups
        _bufferLookup = new();

        ResourceLayoutDescription layoutDescription = new ResourceLayoutDescription(
            new ResourceLayoutElementDescription[Uniforms.Count]);

        for (ushort uniformIndex = 0; uniformIndex < Uniforms.Count; uniformIndex++)
        {
            Uniform uniform = Uniforms[uniformIndex];

            layoutDescription.Elements[uniform.binding] =
                new ResourceLayoutElementDescription(uniform.name, uniform.kind, ShaderStages.Compute);

            if (uniform.kind != ResourceKind.UniformBuffer)
                continue;

            _bufferLookup[uniform.name] = Pack(uniformIndex, _bufferCount);
            _bufferCount++;
        }

        resourceLayout = Graphics.Factory.CreateResourceLayout(layoutDescription);

        _description = new()
        {
            ComputeShader = shader,
            ResourceLayouts = [resourceLayout],
            ThreadGroupSizeX = variant.ThreadGroupSizeX,
            ThreadGroupSizeY = variant.ThreadGroupSizeY,
            ThreadGroupSizeZ = variant.ThreadGroupSizeZ
        };

        _pipeline = Graphics.Factory.CreateComputePipeline(_description);
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

    public void Dispose()
    {
        for (int i = 0; i < shaderSet.Shaders.Length; i++)
            shaderSet.Shaders[i]?.Dispose();

        _pipeline?.Dispose();
        resourceLayout?.Dispose();
    }
}
