using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime
{
    public sealed class Material : EngineObject
    {
        public KeywordState Keywords;
        public AssetRef<Shader> Shader;
        public MaterialPropertyBlock PropertyBlock;


        private int activePass = -1;
        private ResourceCache.PipelineInfo boundPipeline;

        private ResourceSet[] resources;
        private Dictionary<ShaderResource, DeviceBuffer> uniformBuffers = new();


        internal DeviceBuffer GetUniformBuffer(ShaderResource resource, uint size)
        {
            bool hasBuffer = uniformBuffers.TryGetValue(resource, out DeviceBuffer buffer);

            if (!hasBuffer || buffer.SizeInBytes != size)
            {
                buffer = Graphics.Factory.CreateBuffer(new BufferDescription(size, BufferUsage.UniformBuffer));
                uniformBuffers[resource] = buffer;
            }

            return buffer;
        }

        public Material(AssetRef<Shader> shader, MaterialPropertyBlock? properties = null, KeywordState? keywords = null)
        {
            if (shader.Res == null) 
                throw new ArgumentNullException(nameof(shader));
            
            Shader = shader;
            PropertyBlock = properties ?? new();
            Keywords = keywords ?? KeywordState.Empty;
        }

        public void SetPass(CommandList commandList, int passIndex = 0, PolygonFillMode fill = PolygonFillMode.Solid, PrimitiveTopology topology = PrimitiveTopology.TriangleList, bool scissorTest = false)
        {
            activePass = passIndex;
            Pass pass = Shader.Res.GetPass(passIndex);

            boundPipeline = ResourceCache.GetPipelineForPass(pass, fillMode: fill, topology: topology, scissor: scissorTest);

            commandList.SetPipeline(boundPipeline.pipeline);
        }

        public Pass GetPass() => Shader.Res.GetPass(activePass);

        // Suboptimal solution that recreates every resource set to ensure that textures are properly sent to shader.
        // When shaders are being compiled by us, we can optimize this so textures go into their own resource set and then we only have to update the uniform buffer.
        public void Upload(CommandList commandList)
        {
            if (activePass < 0)
                throw new Exception("Invalid pass state. Please make sure to call SetPass() onn this material before calling Upload()");

            if (resources != null)
            {
                foreach (var resource in resources)
                    resource.Dispose();
            }

            Pass pass = Shader.Res.GetPass(activePass);
            
            Pass.Variant variant = pass.GetVariant(Keywords);

            List<BindableResource> bindableResources = new();

            resources = new ResourceSet[variant.resourceSets.Count];

            for (int set = 0; set < variant.resourceSets.Count; set++)
            {
                ShaderResource[] resourceSet = variant.resourceSets[set];

                bindableResources.Clear();

                for (int res = 0; res < resourceSet.Length; res++)
                    resourceSet[res].BindResource(commandList, this, bindableResources);

                ResourceSetDescription description = new ResourceSetDescription
                {
                    Layout = boundPipeline.description.ResourceLayouts[set],
                    BoundResources = bindableResources.ToArray()
                };

                resources[set] = Graphics.Factory.CreateResourceSet(description);

                commandList.SetGraphicsResourceSet((uint)set, resources[set]);
            }
        }


        public void SetKeyword(string keyword, bool state) => Keywords.SetKeyword(keyword, state);
        public void EnableKeyword(string keyword) => Keywords.EnableKeyword(keyword);
        public void DisableKeyword(string keyword) => Keywords.DisableKeyword(keyword);

        public void SetColor(string name, Color value) => PropertyBlock.SetColor(name, value);
        public void SetVector(string name, Vector2 value) => PropertyBlock.SetVector(name, value);
        public void SetVector(string name, Vector3 value) => PropertyBlock.SetVector(name, value);
        public void SetVector(string name, Vector4 value) => PropertyBlock.SetVector(name, value);
        public void SetFloat(string name, float value) => PropertyBlock.SetFloat(name, value);
        public void SetInt(string name, int value) => PropertyBlock.SetInt(name, value);
        public void SetMatrix(string name, Matrix4x4 value) => PropertyBlock.SetMatrix(name, value);
        public void SetTexture(string name, Texture value) => PropertyBlock.SetTexture(name, value);

        public void SetMatrices(string name, System.Numerics.Matrix4x4[] value) { }

        public override void OnDispose()
        {
            if (resources != null)
            {
                foreach (var resource in resources)
                    resource.Dispose();

                foreach (var buffer in uniformBuffers.Values)
                    buffer.Dispose();
            }
        }

        //public CompoundTag Serialize(string tagName, TagSerializer.SerializationContext ctx)
        //{
        //    CompoundTag compoundTag = new CompoundTag(tagName);
        //    compoundTag.Add(TagSerializer.Serialize(Shader, "Shader", ctx));
        //    compoundTag.Add(TagSerializer.Serialize(PropertyBlock, "PropertyBlock", ctx));
        //    return compoundTag;
        //}
        //
        //public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
        //{
        //    Shader = TagSerializer.Deserialize<AssetRef<Shader>>(value["Shader"], ctx);
        //    PropertyBlock = TagSerializer.Deserialize<MaterialPropertyBlock>(value["PropertyBlock"], ctx);
        //}
    }
}
