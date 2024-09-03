using System;
using System.Linq;

using Veldrid;

#pragma warning disable

namespace Prowl.Runtime
{
    public class BindableResourceSet : IDisposable
    {
        public GraphicsPipeline Pipeline { get; private set; }

        public ResourceSetDescription description;
        private ResourceSet resources;

        private DeviceBuffer[] uniformBuffers;
        private byte[][] intermediateBuffers;


        public BindableResourceSet(GraphicsPipeline pipeline, ResourceSetDescription description, DeviceBuffer[] buffers, byte[][] intermediate)
        {
            this.Pipeline = pipeline;
            this.description = description;
            this.uniformBuffers = buffers;
            this.intermediateBuffers = buffers.Select(x => new byte[x.SizeInBytes]).ToArray();
        }


        public void Bind(CommandList list, PropertyState state)
        {
            bool recreateResourceSet = false | (resources == null);

            for (int i = 0; i < Pipeline.Uniforms.Length; i++)
            {
                Uniform uniform = Pipeline.Uniforms[i];

                switch (uniform.kind)
                {
                    case ResourceKind.UniformBuffer:
                        UpdateBuffer(list, uniform.name, state);
                        break;

                    case ResourceKind.StructuredBufferReadOnly:
                        if (state._buffers.TryGetValue(uniform.name, out GraphicsBuffer buffer))
                            if (buffer.Buffer.Usage.HasFlag(BufferUsage.StructuredBufferReadOnly))
                                UpdateResource(buffer.Buffer, uniform.binding, ref recreateResourceSet);
                        break;

                    case ResourceKind.StructuredBufferReadWrite:
                        if (state._buffers.TryGetValue(uniform.name, out GraphicsBuffer rwbuffer))
                            if (rwbuffer.Buffer.Usage.HasFlag(BufferUsage.StructuredBufferReadWrite))
                                UpdateResource(rwbuffer.Buffer, uniform.binding, ref recreateResourceSet);
                        break;

                    case ResourceKind.TextureReadOnly:
                        if (state._textures.TryGetValue(uniform.name, out AssetRef<Texture> texture))
                            if (texture.Res.Usage.HasFlag(TextureUsage.Sampled))
                                UpdateResource(texture.Res.TextureView, uniform.binding, ref recreateResourceSet);
                        break;

                    case ResourceKind.TextureReadWrite:
                        if (state._textures.TryGetValue(uniform.name, out AssetRef<Texture> rwtexture))
                            if (rwtexture.Res.Usage.HasFlag(TextureUsage.Storage))
                                UpdateResource(rwtexture.Res.TextureView, uniform.binding, ref recreateResourceSet);
                        break;

                    case ResourceKind.Sampler:
                        if (state._textures.TryGetValue(SliceSampler(uniform.name), out AssetRef<Texture> stexture))
                            if (stexture.Res.Usage.HasFlag(TextureUsage.Sampled))
                                UpdateResource(stexture.Res.Sampler.InternalSampler, uniform.binding, ref recreateResourceSet);
                        break;
                }
            }

            if (recreateResourceSet)
            {
                resources?.Dispose();
                resources = Graphics.Factory.CreateResourceSet(description);
            }

            list.SetGraphicsResourceSet(0, resources);
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

            Uniform uniform = Pipeline.Uniforms[uniformIndex];
            DeviceBuffer buffer = uniformBuffers[bufferIndex];
            byte[] tempBuffer = intermediateBuffers[bufferIndex];

            for (int i = 0; i < uniform.members.Length; i++)
            {
                UniformMember member = uniform.members[i];

                if (state._values.TryGetValue(member.name, out byte[] value))
                {
                    Buffer.BlockCopy(value, 0, tempBuffer, (int)member.bufferOffsetInBytes, (int)member.size);
                }
            }

            list.UpdateBuffer(buffer, 0, tempBuffer);

            return true;
        }


        private static string SliceSampler(string name)
        {
            const string prefix = "sampler";

            if (name.StartsWith(prefix))
                return name.Substring(prefix.Length);

            return name;
        }


        public void Dispose()
        {
            resources.Dispose();

            for (int i = 0; i < uniformBuffers.Length; i++)
                uniformBuffers[i].Dispose();
        }
    }
}
