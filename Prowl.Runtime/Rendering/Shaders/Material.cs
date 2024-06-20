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
        private DeviceBuffer uniformBuffer;
        private ResourceSet[] resources;


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
                for (int i = 0; i < resources.Length; i++)
                    resources[i].Dispose();
            }

            Pass pass = Shader.Res.GetPass(activePass);
            
            Pass.Variant variant = pass.GetVariant(Keywords);

            int bufferSize = 0;

            for (int set = 0; set < variant.resourceSets.Count; set++)
            {
                ShaderResource[] resourceSet = variant.resourceSets[set];

                for (int res = 0; res < resourceSet.Length; res++)
                    bufferSize += Math.Max(0, resourceSet[res].size);
            }

            if (uniformBuffer == null || uniformBuffer.SizeInBytes != bufferSize)
                uniformBuffer = Graphics.ResourceFactory.CreateBuffer(new BufferDescription((uint)bufferSize, BufferUsage.UniformBuffer));

            uint bufferOffset = 0;

            resources = new ResourceSet[variant.resourceSets.Count];

            for (int set = 0; set < variant.resourceSets.Count; set++)
            {
                ShaderResource[] resourceSet = variant.resourceSets[set];

                ResourceSetDescription description = new ResourceSetDescription();
                description.Layout = boundPipeline.description.ResourceLayouts[set];
                description.BoundResources = new BindableResource[resourceSet.Length];

                for (int res = 0; res < resourceSet.Length; res++)
                {
                    ShaderResource resource = resourceSet[res];

                    if (resource.type == ResourceType.Texture || resource.type == ResourceType.Sampler)
                    {
                        AssetRef<Texture>? texRef = PropertyBlock.GetTexture(resource.name);
                        Texture tex = texRef.GetValueOrDefault(Texture2D.EmptyWhite).Res ?? Texture2D.EmptyWhite;
                        
                        if (resource.type == ResourceType.Texture)
                            description.BoundResources[res] = tex.TextureView;
                        else
                            description.BoundResources[res] = tex.Sampler.InternalSampler;
                        
                        continue;
                    }

                    UpdateBuffer(commandList, resource, bufferOffset);

                    description.BoundResources[res] = new DeviceBufferRange(uniformBuffer, bufferOffset, (uint)resource.size);
                    bufferOffset += (uint)resource.size;
                }

                resources[set] = Graphics.ResourceFactory.CreateResourceSet(description);

                commandList.SetGraphicsResourceSet((uint)set, resources[set]);
            }
        }


        private void UpdateBuffer(CommandList commandList, ShaderResource resource, uint offset)
        {
            switch (resource.type)
            {
                case ResourceType.Float:    
                    commandList.UpdateBuffer(uniformBuffer, offset, PropertyBlock.GetFloat(resource.name));      
                break;

                case ResourceType.Vector2:  
                    commandList.UpdateBuffer(uniformBuffer, offset, (System.Numerics.Vector2)PropertyBlock.GetVector2(resource.name));    
                break;

                case ResourceType.Vector3:  
                    commandList.UpdateBuffer(uniformBuffer, offset, (System.Numerics.Vector3)PropertyBlock.GetVector3(resource.name));    
                break;

                case ResourceType.Vector4:  
                    commandList.UpdateBuffer(uniformBuffer, offset, (System.Numerics.Vector4)PropertyBlock.GetVector4(resource.name));    
                break;

                case ResourceType.Matrix4x4:
                    commandList.UpdateBuffer(uniformBuffer, offset, PropertyBlock.GetMatrix(resource.name).ToFloat());
                break;
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
        public void SetTexture(string name, AssetRef<Texture> value) => PropertyBlock.SetTexture(name, value);
        public void SetMatrices(string name, System.Numerics.Matrix4x4[] value) { }

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
