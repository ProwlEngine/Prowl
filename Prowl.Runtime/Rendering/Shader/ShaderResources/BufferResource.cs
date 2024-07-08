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

        private uint[] resourceOffsets;

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
            const uint vec2Size = sizeof(float) * 2;
            const uint vec3Size = sizeof(float) * 3;
            const uint vec4Size = sizeof(float) * 4;
            const uint mat4Size = sizeof(float) * 4 * 4;

            sizeInBytes = 0;
            resourceOffsets = new uint[resources.Length];

            for (int i = 0; i < resources.Length; i++)
            {
                ResourceType type = resources[i].Type;

                if (type == ResourceType.Float)
                {
                    resourceOffsets[i] = sizeInBytes;
                    sizeInBytes = resourceOffsets[i] + sizeof(float);
                }
                
                if (type == ResourceType.Vector2)
                {
                    resourceOffsets[i] = AlignUp(sizeInBytes, vec2Size); // Ensure vec2 is aligned to an 8-byte offset
                    sizeInBytes = resourceOffsets[i] + vec2Size;
                }

                if (type == ResourceType.Vector3)
                {
                    resourceOffsets[i] = AlignUp(sizeInBytes, vec4Size); // Vec3 must be on a 16-byte alignment like vec4
                    sizeInBytes = resourceOffsets[i] + vec3Size; // However, a float can be packed behind it, so leave some space for one
                }

                if (type == ResourceType.Vector4)
                {
                    resourceOffsets[i] = AlignUp(sizeInBytes, vec4Size); // Ensure vec4 is aligned to a 16-byte offset
                    sizeInBytes = resourceOffsets[i] + vec4Size;
                }

                if (type == ResourceType.Matrix4x4)
                {
                    resourceOffsets[i] = AlignUp(sizeInBytes, vec4Size);
                    sizeInBytes = resourceOffsets[i] + mat4Size;
                }
            }

            sizeInBytes = AlignUp(sizeInBytes, 16);
        }

        private static uint AlignUp(uint value, uint alignment)
        {    
            return (value + alignment - 1) & ~(alignment - 1);
        }

        public override string GetResourceName() => name;

        public override void GetDescription(List<ResourceLayoutElementDescription> elements)
        {
            elements.Add(new ResourceLayoutElementDescription(name, ResourceKind.UniformBuffer, stages));
        }

        public override void BindResource(CommandList commandList, List<BindableResource> resource, RenderState state)
        {
            var buffer = state.GetBufferForResource(this, sizeInBytes);

            UploadBuffer(commandList, state.propertyState, buffer);

            resource.Add(buffer);
        }

        private void UploadBuffer(CommandList commandList, PropertyState properties, DeviceBuffer uniformBuffer)
        { 
            for (int i = 0; i < resources.Length; i++)
            {
                BufferProperty property = resources[i];
                uint bufferOffset = resourceOffsets[i];

                switch (property.Type)
                {
                    case ResourceType.Float: 
                        float data = properties.GetFloat(property.Name);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, data); 
                    break;

                    case ResourceType.Vector2: 
                        System.Numerics.Vector2 vec2 = properties.GetVector2(property.Name);
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec2); 
                    break;

                    case ResourceType.Vector3: 
                        System.Numerics.Vector3 vec3 = properties.GetVector3(property.Name); 
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec3); 
                    break;

                    case ResourceType.Vector4: 
                        System.Numerics.Vector4 vec4 = properties.GetVector4(property.Name); 
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, vec4); 
                    break;

                    case ResourceType.Matrix4x4: 
                        System.Numerics.Matrix4x4 mat4 = properties.GetMatrix(property.Name).ToFloat();
                        commandList.UpdateBuffer(uniformBuffer, bufferOffset, mat4); 
                    break;
                }
            }
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