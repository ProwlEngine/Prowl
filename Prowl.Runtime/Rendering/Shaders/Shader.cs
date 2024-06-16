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
        Matrix4x4,
        Texture,
        RWTexture,
        Sampler,
        StructuredBuffer,
        RWStructuredBuffer,
    }


    public readonly struct ShaderResource(string name, ResourceType type, ShaderStages stages)
    {
        public readonly string name = name;
        public readonly ResourceType type = type;
        public readonly ShaderStages stages = stages;
        public readonly int size = ResourceSize(type);


        public readonly ResourceLayoutElementDescription ToDescription()
        {
            return new ResourceLayoutElementDescription(name, TypeToKind(type), stages);
        }

        public static ResourceKind TypeToKind(ResourceType type)
        {
            return type switch 
            {
                ResourceType.Texture            =>  ResourceKind.TextureReadOnly,
                ResourceType.RWTexture          =>  ResourceKind.TextureReadWrite,
                ResourceType.Sampler            =>  ResourceKind.Sampler,
                ResourceType.StructuredBuffer   =>  ResourceKind.StructuredBufferReadOnly,
                ResourceType.RWStructuredBuffer =>  ResourceKind.StructuredBufferReadWrite,
                _                               =>  ResourceKind.UniformBuffer
            };
        }

        public static int ResourceSize(ResourceType type)
        {
            return type switch
            {
                ResourceType.Float     => sizeof(float),
                ResourceType.Vector2   => sizeof(float) * 2,
                ResourceType.Vector3   => sizeof(float) * 3,
                ResourceType.Vector4   => sizeof(float) * 4,
                ResourceType.Matrix4x4 => sizeof(float) * 4 * 4,
                _ => -1
            };
        }
    }


    public sealed class Shader : EngineObject, ISerializable
    {
        private List<Pass> passes = new();
        private Dictionary<string, int> nameIndexLookup = new();
        private Dictionary<string, List<int>> tagIndexLookup = new(); 


        public Shader(params Pass[] passes) : base("New Shader")
        {
            foreach (Pass pass in passes)
                AddPass(pass);

            ResourceCache.RegisterShader(this);
        }

        public void AddPass(Pass pass)
        {
            int passIndex = passes.Count;
            passes.Add(pass);

            if (!string.IsNullOrWhiteSpace(pass.name))
            {
                if (!nameIndexLookup.TryAdd(pass.name, passIndex))
                    throw new InvalidOperationException($"Pass with name {pass.name} conflicts with existing pass at index {nameIndexLookup[pass.name]}. Ensure no two passes have equal names.");
            }

            if (pass.tags.Count != 0)
            {
                foreach (string key in pass.tags.Keys)
                {
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    if (!tagIndexLookup.TryGetValue(key, out _))
                        tagIndexLookup.Add(key, []);

                    tagIndexLookup[key].Add(passIndex);
                }
            }
        }

        public Pass GetPass(int passIndex)
        {
            return passes[passIndex];
        }

        public Pass GetPass(string passName)
        {   
            return passes[nameIndexLookup.GetValueOrDefault(passName, -1)];
        }

        public Pass GetPassWithTag(string tag, string? tagValue)
        {   
            List<Pass> passes = GetPassesWithTag(tag, tagValue);
            return passes.Count > 0 ? passes[0] : null;
        }

        public List<Pass> GetPassesWithTag(string tag, string? tagValue)
        {   
            List<Pass> passes = [];

            if (tagIndexLookup.TryGetValue(tag, out List<int> passesWithTag))
            {
                foreach (int index in passesWithTag)
                {
                    Pass pass = passes[index];

                    if (tagValue != null)
                    {
                        if (pass.tags[tag] == tagValue)
                            passes.Add(pass);
                    }
                    else
                    {
                        passes.Add(pass);
                    }
                }
            }

            return passes;
        }

        public override void OnDispose()
        {
            foreach (Pass pass in passes)
                pass.Dispose();
        }

        public static AssetRef<Shader> Find(string path)
        {
            return Application.AssetProvider.LoadAsset<Shader>(path);
        }

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            /*SerializedProperty compoundTag = SerializedProperty.NewCompound();
            if (AssetID != Guid.Empty)
            {
                compoundTag.Add("AssetID", new SerializedProperty(AssetID.ToString()));
                if (FileID != 0)
                    compoundTag.Add("FileID", new SerializedProperty(FileID));
            }

            SerializedProperty propertiesTag = SerializedProperty.NewList();
            foreach (var property in Properties)
            {
                SerializedProperty propertyTag = SerializedProperty.NewCompound();
                propertyTag.Add("Name", new(property.Name));
                propertyTag.Add("DisplayName", new(property.DisplayName));
                propertyTag.Add("Type", new((byte)property.Type));
                propertiesTag.ListAdd(propertyTag);
            }
            compoundTag.Add("Properties", propertiesTag);*/
            return null;
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            /*Name = value.Get("Name")?.StringValue;

            if (value.TryGet("AssetID", out var assetIDTag))
            {
                AssetID = Guid.Parse(assetIDTag.StringValue);
                FileID = value.Get("FileID").UShortValue;
            }

            Properties.Clear();
            var propertiesTag = value.Get("Properties");
            foreach (var propertyTag in propertiesTag.List)
            {
                Property property = new Property();
                property.Name = propertyTag.Get("Name").StringValue;
                property.DisplayName = propertyTag.Get("DisplayName").StringValue;
                property.Type = (Property.PropertyType)propertyTag.Get("Type").ByteValue;
                Properties.Add(property);
            }
            Passes.Clear();
            var passesTag = value.Get("Passes");
            foreach (var passTag in passesTag.List)
            {
                ShaderPass pass = new ShaderPass();
                pass.State = Serializer.Deserialize<RasterizerState>(passTag.Get("State"), ctx);
                pass.Vertex = passTag.Get("Vertex").StringValue;
                pass.Fragment = passTag.Get("Fragment").StringValue;
                Passes.Add(pass);
            }
            if (value.TryGet("ShadowPass", out var shadowPassTag))
            {
                ShaderShadowPass shadowPass = new ShaderShadowPass();
                shadowPass.State = Serializer.Deserialize<RasterizerState>(shadowPassTag.Get("State"), ctx);
                shadowPass.Vertex = shadowPassTag.Get("Vertex").StringValue;
                shadowPass.Fragment = shadowPassTag.Get("Fragment").StringValue;
                ShadowPass = shadowPass;
            }*/
        }
    }
}