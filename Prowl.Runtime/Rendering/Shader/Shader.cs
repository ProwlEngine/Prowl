using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public enum ShaderPropertyType
    {
        Float,
        Vector2,
        Vector3,
        Vector4,
        Color,
        Matrix,
        Texture2D,
        Texture3D
    }

    public struct ShaderProperty
    {
        public string Name;
        public string DisplayName;
        public ShaderPropertyType PropertyType; 
    }

    public sealed class Shader : EngineObject, ISerializable
    {
        private readonly ShaderProperty[] properties;
        public IEnumerable<ShaderProperty> Properties => properties;

        private readonly ShaderPass[] passes;
        public IEnumerable<ShaderPass> Passes => passes;
        
        private readonly Dictionary<string, int> nameIndexLookup = new();
        private readonly Dictionary<string, List<int>> tagIndexLookup = new(); 

        internal Shader() : base("New Shader") { }

        public Shader(string name, ShaderProperty[] properties, ShaderPass[] passes) : base(name)
        {
            this.properties = properties;
            this.passes = passes;

            for (int i = 0; i < passes.Length; i++)
                RegisterPass(passes[i], i);

            ShaderCache.RegisterShader(this);
        }

        private void RegisterPass(ShaderPass pass, int index)
        {
            if (!string.IsNullOrWhiteSpace(pass.Name))
            {
                if (!nameIndexLookup.TryAdd(pass.Name, index))
                    throw new InvalidOperationException($"Pass with name {pass.Name} conflicts with existing pass at index {nameIndexLookup[pass.Name]}. Ensure no two passes have equal names.");
            }

            foreach (var key in pass.Tags.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!tagIndexLookup.TryGetValue(key, out _))
                    tagIndexLookup.Add(key, []);

                tagIndexLookup[key].Add(index);
            }
        }

        public ShaderPass GetPass(int passIndex)
        {
            return passes[passIndex];
        }

        public int GetPassIndex(string passName)
        {   
            return nameIndexLookup.GetValueOrDefault(passName, -1);
        }

        public ShaderPass GetPassWithTag(string tag, string? tagValue)
        {   
            List<ShaderPass> passes = GetPassesWithTag(tag, tagValue);
            return passes.Count > 0 ? passes[0] : null;
        }

        public List<ShaderPass> GetPassesWithTag(string tag, string? tagValue)
        {   
            List<ShaderPass> passes = [];

            if (tagIndexLookup.TryGetValue(tag, out List<int> passesWithTag))
            {
                foreach (int index in passesWithTag)
                {
                    ShaderPass pass = passes[index];

                    if (tagValue != null)
                    {
                        if (pass.Tags[tag] == tagValue)
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
            foreach (ShaderPass pass in passes)
                pass.Dispose();
        }

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty compoundTag = SerializedProperty.NewCompound();

            SerializeHeader(compoundTag);

            /*
            SerializedProperty propertiesTag = SerializedProperty.NewList();
            foreach (var property in Properties)
            {
                SerializedProperty propertyTag = SerializedProperty.NewCompound();
                propertyTag.Add("Name", new(property.Name));
                propertyTag.Add("DisplayName", new(property.DisplayName));
                propertyTag.Add("Type", new((byte)property.Type));
                propertiesTag.ListAdd(propertyTag);
            }
            compoundTag.Add("Properties", propertiesTag);
            */

            return compoundTag;
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            DeserializeHeader(value);

            /*
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
            }
            */
        }
    }
}