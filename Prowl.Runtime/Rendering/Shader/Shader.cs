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
        public string DefaultProperty;
    }

    public sealed class Shader : EngineObject, ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector]
        private ShaderProperty[] properties;
        public IEnumerable<ShaderProperty> Properties => properties;


        [SerializeField, HideInInspector]
        private ShaderPass[] passes;
        public IEnumerable<ShaderPass> Passes => passes;
        

        private readonly Dictionary<string, int> nameIndexLookup = new();
        private readonly Dictionary<string, List<int>> tagIndexLookup = new(); 


        internal Shader() : base("New Shader") { }

        public Shader(string name, ShaderProperty[] properties, ShaderPass[] passes) : base(name)
        {
            this.properties = properties;
            this.passes = passes;

            OnAfterDeserialize();
        }

        private void RegisterPass(ShaderPass pass, int index)
        {
            if (!string.IsNullOrWhiteSpace(pass.Name))
            {
                if (!nameIndexLookup.TryAdd(pass.Name, index))
                    throw new InvalidOperationException($"Pass with name {pass.Name} conflicts with existing pass at index {nameIndexLookup[pass.Name]}. Ensure no two passes have equal names.");
            }

            foreach (var pair in pass.Tags)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                if (!tagIndexLookup.TryGetValue(pair.Key, out _))
                    tagIndexLookup.Add(pair.Key, []);

                tagIndexLookup[pair.Key].Add(index);
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

        public ShaderPass GetPassWithTag(string tag, string? tagValue = null)
        {   
            List<ShaderPass> passes = GetPassesWithTag(tag, tagValue);
            return passes.Count > 0 ? passes[0] : null;
        }

        public List<ShaderPass> GetPassesWithTag(string tag, string? tagValue = null)
        {   
            List<ShaderPass> passes = [];

            if (tagIndexLookup.TryGetValue(tag, out List<int> passesWithTag))
            {
                foreach (int index in passesWithTag)
                {
                    ShaderPass pass = passes[index];

                    if (pass.HasTag(tag, tagValue))
                        passes.Add(pass);
                }
            }

            return passes;
        }
        
        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            for (int i = 0; i < passes.Length; i++)
                RegisterPass(passes[i], i);

            Debug.Log(GetStringRepresentation());
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

                builder.Append("\tInputs\n\t{\n");

                builder.Append("\t\tVertexInputs\n\t\t{\n");
                foreach(var input in pass.GetVariant(KeywordState.Default).VertexInputs)
                {
                    builder.Append($"\t\t\t{input.Elements[0].Name}\n");
                }
                builder.Append("\t\t}\n\n");

                foreach(var set in pass.GetVariant(KeywordState.Default).ResourceSets)
                {
                    builder.Append("\t\tSet\n\t\t{\n");

                    foreach (var res in set)
                    {
                        if (res is BufferResource bufRes)
                        {
                            builder.Append("\t\t\tBuffer\n\t\t\t{\n");

                            foreach (var elem in bufRes.Resources)
                            {
                                builder.Append($"\t\t\t\t{elem.Name} {elem.Type}\n");
                            }

                            builder.Append("\t\t\t}\n");
                        }
                        else
                            builder.Append($"\t\t\t{res.GetType().Name} {res.GetResourceName()}\n");
                    }

                    builder.Append("\t\t}\n\n");
                }

                builder.Append("\t}\n");

                builder.Append("}\n\n");
            }

            return builder.ToString();
        }
    }
}