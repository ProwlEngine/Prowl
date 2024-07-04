using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public class TextureResource : ShaderResource
    {
        public string textureName { get; private set; }
        public bool readWrite { get; private set; }
        public ShaderStages stages { get; private set; }

        private TextureResource() { }

        public TextureResource(string textureName, bool readWrite, ShaderStages stages)
        {
            this.textureName = textureName;
            this.readWrite = readWrite;
            this.stages = stages;
        }

        public override void GetDescription(List<ResourceLayoutElementDescription> elements)
        {
            ResourceKind kind = readWrite ? ResourceKind.TextureReadWrite : ResourceKind.TextureReadOnly;
            
            elements.Add(new ResourceLayoutElementDescription(textureName, kind, stages));
        }

        public override void BindResource(CommandList commandList, List<BindableResource> resources, RenderState state)
        {
            Texture tex = state.propertyState.GetTexture(textureName);

            if (tex == null)
                tex = Texture2D.EmptyWhite;
            
            if (tex.IsDestroyed)
                tex = Texture2D.EmptyWhite;

            if (!tex.InternalTexture.Usage.HasFlag(TextureUsage.Sampled))
                tex = Texture2D.EmptyWhite;

            resources.Add(tex.TextureView);
        }

        public override string GetResourceName() => textureName;

        public override SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty serializedTexture = SerializedProperty.NewCompound();

            serializedTexture.Add("TextureName", new(textureName));
            serializedTexture.Add("ReadWrite", new(readWrite));
            serializedTexture.Add("Stages", new((byte)stages));

            return serializedTexture;
        }

        public override void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            textureName = value.Get("TextureName").StringValue;
            readWrite = value.Get("ReadWrite").BoolValue;
            stages = (ShaderStages)value.Get("Stages").ByteValue;
        }
    }

    public class SamplerResource : ShaderResource
    {
        public string textureName { get; private set; }
        public ShaderStages stages { get; private set; }

        private SamplerResource() { }

        public SamplerResource(string textureName, ShaderStages stages)
        {
            this.textureName = textureName;
            this.stages = stages;
        }

        public override void GetDescription(List<ResourceLayoutElementDescription> elements)
        {
            elements.Add(new ResourceLayoutElementDescription(textureName, ResourceKind.Sampler, stages));
        }

        public override void BindResource(CommandList commandList, List<BindableResource> resources, RenderState state)
        {
            Texture tex = state.propertyState.GetTexture(textureName);

            if (tex == null)
                tex = Texture2D.EmptyWhite;
            
            if (tex.IsDestroyed)
                tex = Texture2D.EmptyWhite;

            if (!tex.InternalTexture.Usage.HasFlag(TextureUsage.Sampled))
                tex = Texture2D.EmptyWhite;

            resources.Add(tex.Sampler.InternalSampler);
        }

        public override string GetResourceName() => textureName;

        public override SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty serializedTexture = SerializedProperty.NewCompound();

            serializedTexture.Add("TextureName", new(textureName));
            serializedTexture.Add("Stages", new((byte)stages));

            return serializedTexture;
        }

        public override void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            textureName = value.Get("TextureName").StringValue;
            stages = (ShaderStages)value.Get("Stages").ByteValue;
        }
    }
}