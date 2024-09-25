// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Veldrid;

#pragma warning disable

namespace Prowl.Runtime;

public class BindableResourceSet
{
    public ShaderPipeline Pipeline { get; private set; }

    public ResourceSetDescription description;
    private ResourceSet resources;

    private readonly DeviceBuffer[] uniformBuffers;
    private readonly byte[][] intermediateBuffers;


    public BindableResourceSet(ShaderPipeline pipeline, ResourceSetDescription description, DeviceBuffer[] buffers, byte[][] intermediate)
    {
        Pipeline = pipeline;
        this.description = description;
        uniformBuffers = buffers;
        intermediateBuffers = buffers.Select(x => new byte[x.SizeInBytes]).ToArray();
    }


    public void Bind(CommandList list, PropertyState state, List<IDisposable> resourcesToDispose)
    {
        bool recreateResourceSet = false | (resources == null);

        for (int i = 0; i < Pipeline.Uniforms.Length; i++)
        {
            ShaderUniform uniform = Pipeline.Uniforms[i];

            switch (uniform.kind)
            {
                case ResourceKind.UniformBuffer:
                    UpdateBuffer(list, uniform.name, state);
                    break;

                case ResourceKind.StructuredBufferReadOnly:
                    GraphicsBuffer buffer = state._buffers.GetValueOrDefault(uniform.name, null) ?? GraphicsBuffer.Empty;

                    if (!buffer.Buffer.Usage.HasFlag(BufferUsage.StructuredBufferReadOnly))
                        buffer = GraphicsBuffer.EmptyRW;

                    UpdateResource(buffer.Buffer, uniform.binding, ref recreateResourceSet);
                    break;

                case ResourceKind.StructuredBufferReadWrite:
                    GraphicsBuffer rwbuffer = state._buffers.GetValueOrDefault(uniform.name, null) ?? GraphicsBuffer.EmptyRW;

                    if (!rwbuffer.Buffer.Usage.HasFlag(BufferUsage.StructuredBufferReadWrite))
                        rwbuffer = GraphicsBuffer.EmptyRW;

                    UpdateResource(rwbuffer.Buffer, uniform.binding, ref recreateResourceSet);
                    break;

                case ResourceKind.TextureReadOnly:
                    Veldrid.Texture texture = GetTexture(uniform.name, state, TextureUsage.Sampled, Texture2D.EmptyWhite, out _);

                    UpdateResource(texture, uniform.binding, ref recreateResourceSet);

                    break;

                case ResourceKind.TextureReadWrite:
                    Veldrid.Texture rwtexture = GetTexture(uniform.name, state, TextureUsage.Storage, Texture2D.EmptyRW, out _);

                    UpdateResource(rwtexture, uniform.binding, ref recreateResourceSet);

                    break;

                case ResourceKind.Sampler:
                    GetTexture(SliceSampler((uniform.name)), state, TextureUsage.Sampled, Texture2D.EmptyWhite, out Sampler sampler);

                    UpdateResource(sampler, uniform.binding, ref recreateResourceSet);

                    break;
            }
        }

        if (recreateResourceSet)
        {
            if (resources != null)
                resourcesToDispose.Add(resources);

            resources = Graphics.Factory.CreateResourceSet(description);
        }

        list.SetGraphicsResourceSet(0, resources);
    }


    private Veldrid.Texture GetTexture(string name, PropertyState state, TextureUsage usage, Texture defaultTex, out Sampler sampler)
    {
        Veldrid.Texture texture;

        (Veldrid.Texture?, Veldrid.Sampler?) texturePair =
            state._textures.GetValueOrDefault(name, (null, null));

        texture = texturePair.Item1 ?? defaultTex.InternalTexture;
        sampler = texturePair.Item2 ?? defaultTex.Sampler.InternalSampler;

        if (texture.IsDisposed)
            return defaultTex.InternalTexture;

        if (!texture.Usage.HasFlag(usage))
            return defaultTex.InternalTexture;

        return texture;
    }


    private void UpdateResource(BindableResource newResource, uint binding, ref bool wasChanged)
    {
        if (description.BoundResources[binding].Resource != newResource.Resource)
        {
            wasChanged |= true;
            description.BoundResources[binding] = newResource;
        }
    }


    public bool UpdateBuffer(CommandList list, string ID, PropertyState state)
    {
        if (!Pipeline.GetBuffer(ID, out ushort uniformIndex, out ushort bufferIndex))
            return false;

        ShaderUniform uniform = Pipeline.Uniforms[uniformIndex];
        DeviceBuffer buffer = uniformBuffers[bufferIndex];
        byte[] tempBuffer = intermediateBuffers[bufferIndex];

        for (int i = 0; i < uniform.members.Length; i++)
        {
            ShaderUniformMember member = uniform.members[i];

            if (state._values.TryGetValue(member.name, out ValueProperty value))
            {
                if (value.type != member.type)
                    continue;

                if (member.arrayStride <= 0)
                {
                    Buffer.BlockCopy(value.data, 0, tempBuffer, (int)member.bufferOffsetInBytes, Math.Min((int)member.size, value.data.Length));
                    continue;
                }

                uint destStride = member.arrayStride;
                uint srcStride = Math.Min(destStride, (uint)value.width * value.height);
                uint destLength = member.size / member.arrayStride;

                for (int j = 0; j < Math.Min(destLength, value.arraySize); i++)
                {
                    Buffer.BlockCopy(value.data, (int)(j * srcStride), tempBuffer, (int)(member.bufferOffsetInBytes + (j * destStride)), (int)srcStride);
                }
            }
        }

        list.UpdateBuffer(buffer, 0, tempBuffer);

        return true;
    }


    private static string SliceSampler(string name)
    {
        const string prefix = "sampler";

        if (name.StartsWith(prefix, StringComparison.Ordinal))
            return name.Substring(prefix.Length);

        return name;
    }


    public void DisposeResources(List<IDisposable> resourcesToDispose)
    {
        resourcesToDispose.Add(resources);
        resourcesToDispose.AddRange(uniformBuffers);
    }


    ~BindableResourceSet()
    {
        resources?.Dispose();

        foreach (IDisposable disposable in uniformBuffers)
            disposable.Dispose();
    }
}
