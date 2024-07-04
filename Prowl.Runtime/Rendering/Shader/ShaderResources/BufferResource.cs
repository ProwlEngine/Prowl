using System;
using System.Collections.Generic;
using Veldrid;

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

    public class BufferResource : ShaderResource
    {
        public string name { get; private set; }
        public (string, ResourceType)[] resources { get; private set; } = [];
        public ShaderStages stages { get; private set; }
        public uint sizeInBytes { get; private set; }

        private BufferResource() { }

        public BufferResource(string name, ShaderStages stages, params (string, ResourceType)[] resources)
        {
            this.name = name;
            this.resources = resources;
            this.stages = stages;

            sizeInBytes = 0;

            for (int i = 0; i < resources.Length; i++)
                sizeInBytes += ResourceSize(resources[i].Item2);
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

        public override SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty serializedBuffer = SerializedProperty.NewCompound();

            serializedBuffer.Add("Name", new(name));
            serializedBuffer.Add("Stages", new((byte)stages));

            SerializedProperty serializedResources = SerializedProperty.NewList();

            foreach (var resource in resources)
            {
                SerializedProperty serializedResource = SerializedProperty.NewCompound();

                serializedResource.Add("Name", new(resource.Item1));
                serializedResource.Add("Type", new((byte)resource.Item2));

                serializedResources.ListAdd(serializedResource);
            }

            serializedBuffer.Add("Resources", serializedResources);

            return serializedBuffer;
        }

        public override void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            this.name = value.Get("Name").StringValue;
            this.stages = (ShaderStages)value.Get("Stages").ByteValue;

            SerializedProperty resourceProp = value.Get("Resources");

            this.resources = new (string, ResourceType)[resourceProp.Count];

            sizeInBytes = 0;
            for (int i = 0; i < resources.Length; i++)
            {
                this.resources[i].Item1 = resourceProp[i].Get("Name").StringValue;
                this.resources[i].Item2 = (ResourceType)resourceProp[i].Get("Type").ByteValue;

                sizeInBytes += ResourceSize(resources[i].Item2);
            }
        }

    }
}