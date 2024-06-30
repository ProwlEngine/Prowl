using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public sealed class Shader : EngineObject, ISerializable
    {
        const string defaultVertex = @"
            #version 450

            layout(location = 0) in vec3 Position;

            void main()
            {
                gl_Position = vec4(Position.xyz, 1.0);
            }
            ";

        const string defaultFragment = @"
            #version 450

            layout(location = 0) out vec4 Color;

            void main()
            {
                Color = vec4(1.0, 1.0, 1.0, 1.0);
            }
            ";

        public static readonly Shader Default = CreateDefault();

        private static Shader CreateDefault()
        {
            ShaderPass pass = new ShaderPass("Default Pass", null, 
                [ 
                    (ShaderStages.Vertex, defaultVertex),
                    (ShaderStages.Fragment, defaultFragment)
                ],
                null
            );

            pass.CompilePrograms((source, keywords) => new ShaderPass.Variant()
            {
                keywords = keywords,
                vertexInputs = 
                [
                    MeshResource.Position,
                ],
                compiledPrograms = Graphics.CreateFromSpirv(source[0].Item2, source[1].Item2)
            });

            return new("Default Shader", pass);
        }


        private List<ShaderPass> passes = new();
        private Dictionary<string, int> nameIndexLookup = new();
        private Dictionary<string, List<int>> tagIndexLookup = new(); 


        internal Shader() : base("New Shader") { }

        public Shader(string name, params ShaderPass[] passes) : base(name)
        {
            foreach (ShaderPass pass in passes)
                AddPass(pass);

            ShaderCache.RegisterShader(this);
        }

        public void AddPass(ShaderPass pass)
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
            foreach (ShaderPass pass in passes)
                pass.Dispose();
        }

        public static AssetRef<Shader> Find(string path)
        {
            return Application.AssetProvider.LoadAsset<Shader>(path);
        }

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty compoundTag = SerializedProperty.NewCompound();

            compoundTag.Add("Name", new(Name));

            if (AssetID != Guid.Empty)
            {
                compoundTag.Add("AssetID", new SerializedProperty(AssetID.ToString()));
                if (FileID != 0)
                    compoundTag.Add("FileID", new SerializedProperty(FileID));
            }

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
            Name = value.Get("Name")?.StringValue;

            if (value.TryGet("AssetID", out var assetIDTag))
            {
                AssetID = Guid.Parse(assetIDTag.StringValue);
                FileID = value.Get("FileID").UShortValue;
            }

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