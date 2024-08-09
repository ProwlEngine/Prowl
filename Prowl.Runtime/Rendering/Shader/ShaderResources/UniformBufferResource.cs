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

    public struct BufferProperty(string name, ResourceType type, uint offset)
    {
        [SerializeField, HideInInspector]
        public string Name = name;

        [SerializeField, HideInInspector]
        public ResourceType Type = type;

        [SerializeField, HideInInspector]
        public uint Offset = offset;
    }

    public class UniformBufferResource : ShaderResource
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

        private UniformBufferResource() { }

        public UniformBufferResource(string name, ShaderStages stages, params BufferProperty[] resources)
        {
            this.name = name;
            this.resources = resources;
            this.stages = stages;
        }

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

                switch (property.Type)
                {
                    case ResourceType.Float: 
                        float data = properties.GetFloat(property.Name);
                        commandList.UpdateBuffer(uniformBuffer, property.Offset, data); 
                    break;

                    case ResourceType.Vector2: 
                        System.Numerics.Vector2 vec2 = properties.GetVector2(property.Name);
                        commandList.UpdateBuffer(uniformBuffer, property.Offset, vec2); 
                    break;

                    case ResourceType.Vector3: 
                        System.Numerics.Vector3 vec3 = properties.GetVector3(property.Name); 
                        commandList.UpdateBuffer(uniformBuffer, property.Offset, vec3); 
                    break;

                    case ResourceType.Vector4: 
                        System.Numerics.Vector4 vec4 = properties.GetVector4(property.Name); 
                        commandList.UpdateBuffer(uniformBuffer, property.Offset, vec4); 
                    break;

                    case ResourceType.Matrix4x4: 
                        System.Numerics.Matrix4x4 mat4 = properties.GetMatrix(property.Name).ToFloat();
                        commandList.UpdateBuffer(uniformBuffer, property.Offset, mat4); 
                    break;
                }
            }
        }
    }
}