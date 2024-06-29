using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public enum ResourceType
    {
        Float,
        Vector2,
        Vector3,
        Vector4,
        Matrix4x4
    }


    public interface ShaderResource
    {
        public void GetDescription(List<ResourceLayoutElementDescription> elements);

        public void BindResource(CommandList commandList, List<BindableResource> resources, RenderState state);

        public string GetResourceName();
    }


    public class TextureResource : ShaderResource
    {
        public readonly string textureName;
        public readonly bool readWrite;
        public readonly ShaderStages stages;

        public TextureResource(string textureName, bool readWrite, ShaderStages stages)
        {
            this.textureName = textureName;
            this.readWrite = readWrite;
            this.stages = stages;
        }

        public void GetDescription(List<ResourceLayoutElementDescription> elements)
        {
            ResourceKind kind = readWrite ? ResourceKind.TextureReadWrite : ResourceKind.TextureReadOnly;
            
            elements.Add(new ResourceLayoutElementDescription(textureName, kind, stages));
            elements.Add(new ResourceLayoutElementDescription(textureName, ResourceKind.Sampler, stages));
        }

        public void BindResource(CommandList commandList, List<BindableResource> resources, RenderState state)
        {
            Texture tex = state.propertyState.GetTexture(textureName);

            if (tex == null)
                tex = Texture2D.EmptyWhite;
            
            if (tex.IsDestroyed)
                tex = Texture2D.EmptyWhite;

            if (!tex.InternalTexture.Usage.HasFlag(TextureUsage.Sampled))
                tex = Texture2D.EmptyWhite;

            resources.Add(tex.TextureView);
            resources.Add(tex.Sampler.InternalSampler);
        }

        public string GetResourceName() => textureName;
    }


    public class BufferResource : ShaderResource
    {
        public readonly string name;
        public readonly (string, ResourceType)[] resources;
        public readonly ShaderStages stages;
        public readonly uint sizeInBytes;

        public BufferResource(string name, ShaderStages stages, params (string, ResourceType)[] resources)
        {
            this.name = name;
            this.resources = resources;
            this.stages = stages;

            sizeInBytes = 0;

            for (int i = 0; i < resources.Length; i++)
                sizeInBytes += ResourceSize(resources[i].Item2);
        }

        public string GetResourceName() => name;

        public void GetDescription(List<ResourceLayoutElementDescription> elements)
        {
            elements.Add(new ResourceLayoutElementDescription(name, ResourceKind.UniformBuffer, stages));
        }

        public void BindResource(CommandList commandList, List<BindableResource> resource, RenderState state)
        {
            DeviceBuffer GetUniformBuffer(uint size)
            {
                bool hasBuffer = state.uniformBuffers.TryGetValue(this, out DeviceBuffer buffer);

                if (!hasBuffer)
                {
                    buffer = Graphics.Factory.CreateBuffer(new BufferDescription(size, BufferUsage.UniformBuffer));
                    state.uniformBuffers[this] = buffer;
                }

                return buffer;
            }

            var buffer = GetUniformBuffer(sizeInBytes);

            UploadBuffer(commandList, state.propertyState, buffer);

            resource.Add(buffer);
        }

        private void UploadBuffer(CommandList commandList, PropertyState properties, DeviceBuffer uniformBuffer)
        {
            uint bufferOffset = 0;

            for (int i = 0; i < resources.Length; i++)
            {
                (string, ResourceType) resource = resources[i];

                switch (resource.Item2)
                {
                    case ResourceType.Float: 
                        float data = properties.GetFloat(resource.Item1);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, data); 
                        bufferOffset += sizeof(float);
                    break;

                    case ResourceType.Vector2: 
                        System.Numerics.Vector2 vec2 = properties.GetVector2(resource.Item1);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec2); 
                        bufferOffset += sizeof(float) * 2;
                    break;

                    case ResourceType.Vector3: 
                        System.Numerics.Vector3 vec3 = properties.GetVector3(resource.Item1);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec3); 
                        bufferOffset += sizeof(float) * 3;
                    break;

                    case ResourceType.Vector4: 
                        System.Numerics.Vector4 vec4 = properties.GetVector4(resource.Item1);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec4); 
                        bufferOffset += sizeof(float) * 4;
                    break;

                    case ResourceType.Matrix4x4: 
                        System.Numerics.Matrix4x4 mat4 = properties.GetMatrix(resource.Item1).ToFloat();
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, mat4); 
                        bufferOffset += sizeof(float) * 4 * 4;
                    break;
                }
            }
        }

        private static uint ResourceSize(ResourceType resource)
        {
            const uint floatSize = sizeof(float);

            return resource switch {
                ResourceType.Float => floatSize,
                ResourceType.Vector2 => floatSize * 2,
                ResourceType.Vector3 => floatSize * 3,
                ResourceType.Vector4 => floatSize * 4,
                ResourceType.Matrix4x4 => floatSize * 4 * 4,
                _ => 0,
            };
        }
    }
}