using System;
using System.Collections.Generic;
using Veldrid;
using System.Linq;

namespace Prowl.Runtime
{
    public enum ResourceType : byte
    {
        Float,
        Vector2,
        Vector3,
        Vector4,
        Matrix4x4
    }

    public struct BufferProperty
    {
        [SerializeField, HideInInspector]
        public string Name;

        [SerializeField, HideInInspector]
        public ResourceType Type;
    }

    public class BufferResource : ShaderResource, ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector]
        private string name;
        public string Name => name;

        [SerializeField, HideInInspector]
        private BufferProperty[] resources = [];
        public IEnumerable<BufferProperty> Resources => resources;

        [SerializeField, HideInInspector]
        private ShaderStages stages;
        public ShaderStages Stages => stages;

        private uint sizeInBytes;
        public uint SizeInBytes => sizeInBytes;

        private BufferResource() { }

        public BufferResource(string name, ShaderStages stages, params BufferProperty[] resources)
        {
            this.name = name;
            this.resources = resources;
            this.stages = stages;
            ComputeSize();
        }

        public BufferResource(string name, ShaderStages stages, params (string, ResourceType)[] resources)
        {
            this.name = name;
            this.resources = resources.Select(x => new BufferProperty() { Name = x.Item1, Type = x.Item2 }).ToArray();
            this.stages = stages;
            ComputeSize();
        }

        private void ComputeSize()
        {
            sizeInBytes = 0;

            for (int i = 0; i < resources.Length; i++)
                sizeInBytes += ResourceSize(resources[i].Type);
        }

        public override string GetResourceName() => name;

        public override void GetDescription(List<ResourceLayoutElementDescription> elements)
        {
            elements.Add(new ResourceLayoutElementDescription(name, ResourceKind.UniformBuffer, stages));
        }

        public override void BindResource(CommandList commandList, List<BindableResource> resource, RenderState state)
        {
            DeviceBuffer GetUniformBuffer(uint size)
            {
                if (!state.uniformBuffers.TryGetValue(this, out DeviceBuffer buffer))
                {
                    buffer = Graphics.Factory.CreateBuffer(new BufferDescription(size, BufferUsage.UniformBuffer | BufferUsage.DynamicWrite));
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
                BufferProperty property = resources[i];

                switch (property.Type)
                {
                    case ResourceType.Float: 
                        float data = properties.GetFloat(property.Name);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, data); 
                        bufferOffset += sizeof(float);
                    break;

                    case ResourceType.Vector2: 
                        System.Numerics.Vector2 vec2 = properties.GetVector2(property.Name);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec2); 
                        bufferOffset += sizeof(float) * 2;
                    break;

                    case ResourceType.Vector3: 
                        System.Numerics.Vector3 vec3 = properties.GetVector3(property.Name);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec3); 
                        bufferOffset += sizeof(float) * 3;
                    break;

                    case ResourceType.Vector4: 
                        System.Numerics.Vector4 vec4 = properties.GetVector4(property.Name);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec4); 
                        bufferOffset += sizeof(float) * 4;
                    break;

                    case ResourceType.Matrix4x4: 
                        System.Numerics.Matrix4x4 mat4 = properties.GetMatrix(property.Name).ToFloat();
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

        public override int GetHashCode()
        {
            return HashCode.Combine(name, stages, sizeInBytes);
        }

        public override bool Equals(object? obj)
        {
            if (obj is not BufferResource other)
                return false;
            return Equals(other);
        }

        public bool Equals(BufferResource other)
        {
            return 
                name.Equals(other.name) &&
                stages.Equals(other.stages) &&
                sizeInBytes.Equals(other.sizeInBytes) &&
                resources.SequenceEqual(other.resources);
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            ComputeSize();
        }
    }
}