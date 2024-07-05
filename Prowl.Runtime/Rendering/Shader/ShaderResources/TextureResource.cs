using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public class TextureResource : ShaderResource
    {
        [SerializeField, HideInInspector]
        private string textureName;
        public string TextureName => textureName;

        [SerializeField, HideInInspector]
        private bool readWrite;
        public bool ReadWrite => readWrite;

        [SerializeField, HideInInspector]
        private ShaderStages stages;
        public ShaderStages Stages => stages;


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
    }

    public class SamplerResource : ShaderResource
    {
        [SerializeField, HideInInspector]
        private string textureName;
        public string TextureName => textureName;

        [SerializeField, HideInInspector]
        private ShaderStages stages;
        public ShaderStages Stages => stages;

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
    }
}