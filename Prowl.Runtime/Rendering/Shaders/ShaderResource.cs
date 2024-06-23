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
        public void BindResource(CommandList commandList, Material material, List<BindableResource> bindableResources);

        public void GetDescription(List<ResourceLayoutElementDescription> elements);

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

        public void BindResource(CommandList commandList, Material material, List<BindableResource> bindableResources)
        {
            Texture tex = material.PropertyBlock.GetTexture(textureName) ?? Texture2D.EmptyWhite;

            bindableResources.Add(tex.InternalTexture);
            bindableResources.Add(tex.Sampler.InternalSampler);
        }

        public void GetDescription(List<ResourceLayoutElementDescription> elements)
        {
            ResourceKind kind = readWrite ? ResourceKind.TextureReadWrite : ResourceKind.TextureReadOnly;
            
            elements.Add(new ResourceLayoutElementDescription(textureName, kind, stages));
            elements.Add(new ResourceLayoutElementDescription(textureName, ResourceKind.Sampler, stages));
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

        public void BindResource(CommandList commandList, Material material, List<BindableResource> bindableResources)
        {
            DeviceBuffer uniformBuffer = material.GetUniformBuffer(this, sizeInBytes);

            UploadBuffer(commandList, material, uniformBuffer);

            bindableResources.Add(uniformBuffer);
        }

        public string GetResourceName() => name;

        public void GetDescription(List<ResourceLayoutElementDescription> elements)
        {
            elements.Add(new ResourceLayoutElementDescription(name, ResourceKind.UniformBuffer, stages));
        }


        private void UploadBuffer(CommandList commandList, Material material, DeviceBuffer uniformBuffer)
        {
            uint bufferOffset = 0;

            for (int i = 0; i < resources.Length; i++)
            {
                (string, ResourceType) resource = resources[i];

                switch (resource.Item2)
                {
                    case ResourceType.Float: 
                        float data = material.PropertyBlock.GetFloat(resource.Item1);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, data); 
                        bufferOffset += sizeof(float);
                    break;

                    case ResourceType.Vector2: 
                        System.Numerics.Vector2 vec2 = material.PropertyBlock.GetVector2(resource.Item1);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec2); 
                        bufferOffset += sizeof(float) * 2;
                    break;

                    case ResourceType.Vector3: 
                        System.Numerics.Vector3 vec3 = material.PropertyBlock.GetVector3(resource.Item1);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec3); 
                        bufferOffset += sizeof(float) * 3;
                    break;

                    case ResourceType.Vector4: 
                        System.Numerics.Vector4 vec4 = material.PropertyBlock.GetVector4(resource.Item1);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec4); 
                        bufferOffset += sizeof(float) * 4;
                    break;

                    case ResourceType.Matrix4x4: 
                        System.Numerics.Matrix4x4 mat4 = material.PropertyBlock.GetMatrix(resource.Item1).ToFloat();
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