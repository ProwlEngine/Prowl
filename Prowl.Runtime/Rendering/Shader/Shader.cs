using System;
using System.Collections.Generic;
using System.Text;
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


        public string GetStringRepresentation()
        {
            StringBuilder builder = new();

            builder.Append($"Shader \"{Name}\"\n\n");

            builder.Append("Properties\n{\n");

            foreach (ShaderProperty property in Properties)
            {
                builder.Append($"\t{property.Name}(\"{property.DisplayName}\", {property.PropertyType})\n");
            }

            builder.Append("}\n\n");
            
            foreach (ShaderPass pass in Passes)
            {
                builder.Append($"Pass {pass.Name}\n{{\n");

                builder.Append("\tTags { ");
                foreach (var pair in pass.Tags)
                {
                    builder.Append($"\"{pair.Key}\" = \"{pair.Value}\", ");
                }
                builder.Append("}\n\n");

                builder.Append("\tFeatures \n\t{\n");
                foreach (var pair in pass.Keywords)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                        continue;

                    builder.Append($"\t\t{pair.Key} [ {string.Join(" ", pair.Value)} ]\n");
                }
                builder.Append("\t}\n\n");

                builder.Append($"\tZTest {pass.DepthClipEnabled}\n\n");

                builder.Append($"\tCull {pass.CullMode}\n\n");

                if (pass.Blend.AttachmentStates[0].BlendEnabled)
                {
                    BlendAttachmentDescription desc = pass.Blend.AttachmentStates[0];

                    builder.Append("\tBlend\n\t{\n");
                    builder.Append($"\t\tMode Alpha {desc.AlphaFunction}\n");
                    builder.Append($"\t\tMode Color {desc.ColorFunction}\n\n");
                    builder.Append($"\t\tSrc Alpha {desc.SourceAlphaFactor}\n");
                    builder.Append($"\t\tSrc Color {desc.SourceColorFactor}\n\n");
                    builder.Append($"\t\tDest Alpha {desc.DestinationAlphaFactor}\n");
                    builder.Append($"\t\tDest Color {desc.DestinationColorFactor}\n\n");

                    builder.Append($"\t\tMask {desc.ColorWriteMask}\n");
                    builder.Append("\t}\n\n");
                }

                builder.Append("\tDepthStencil\n\t{\n");

                DepthStencilStateDescription dDesc = pass.DepthStencilState;

                if (dDesc.DepthTestEnabled)
                    builder.Append($"\t\tDepthTest {dDesc.DepthComparison}\n");

                builder.Append($"\t\tDepthWrite {dDesc.DepthWriteEnabled}\n\n");

                builder.Append($"\t\tComparison {dDesc.StencilFront.Comparison} {dDesc.StencilBack.Comparison}\n");
                builder.Append($"\t\tDepthFail {dDesc.StencilFront.DepthFail} {dDesc.StencilBack.DepthFail}\n");
                builder.Append($"\t\tFail {dDesc.StencilFront.Fail} {dDesc.StencilBack.Fail}\n");
                builder.Append($"\t\tPass {dDesc.StencilFront.Pass} {dDesc.StencilBack.Pass}\n\n");
                
                builder.Append($"\t\tReadMask {dDesc.StencilReadMask}\n");
                builder.Append($"\t\tRef {dDesc.StencilReference}\n");
                builder.Append($"\t\tWriteMask {dDesc.StencilWriteMask}\n");

                builder.Append("\t}\n");

                foreach (var src in pass.ShaderSource)
                {
                    builder.Append($"\n\tPROGRAM {src.Stage.ToString().ToUpper()}\n");
                    builder.Append($"\t\t{src.SourceCode}\n");
                    builder.Append("\tENDPROGRAM\n");
                }

                builder.Append("}\n\n");
            }

            return builder.ToString();
        }
    }
}