using System;
using System.Collections.Generic;
using Prowl.Runtime.Utils;
using Veldrid;

namespace Prowl.Runtime
{
    public class ShaderPass 
    {
        /// <summary>
        /// The name to identify this <see cref="ShaderPass"/> 
        /// </summary>
        public string name;

        /// <summary>
        /// The tags to identify this <see cref="ShaderPass"/> 
        /// </summary>
        public Dictionary<string, string> tags = new();

        /// <summary>
        /// The blending options to use when rendering this <see cref="ShaderPass"/> 
        /// </summary>
        public BlendStateDescription blend = BlendStateDescription.SingleOverrideBlend;

        /// <summary>
        /// The depth stencil state to use when rendering this <see cref="ShaderPass"/> 
        /// </summary>
        public DepthStencilStateDescription depthStencil = new DepthStencilStateDescription(
            depthTestEnabled: true,
            depthWriteEnabled: true,
            comparisonKind: ComparisonKind.LessEqual
        );

        public FaceCullMode cullMode = FaceCullMode.Back;
        public bool depthClipEnabled = true;


        public class Variant
        {
            public KeyGroup<string, string> keywords;
            public List<MeshResource> vertexInputs = new();
            public List<ShaderResource[]> resourceSets = new();
            public Veldrid.Shader[] compiledPrograms;
        }

        
        private (ShaderStages, string)[] shaderSource;
        private Dictionary<string, HashSet<string>> keywords;
        private PermutationMap<string, string, Variant> variants;

        public delegate Variant VariantGenerator((ShaderStages, string)[] sources, KeyGroup<string, string> keywords);

        public ShaderPass(
            string name, 
            (string, string)[] tags, 
            (ShaderStages, string)[] shaderSource, 
            Dictionary<string, HashSet<string>> keywords
        ) {
            this.name = name;
            this.shaderSource = shaderSource;

            if (keywords == null || keywords.Count == 0)
                keywords = new() { { string.Empty, [ string.Empty ] } };

            this.keywords = keywords;

            if (tags != null)
            {
                for (int i = 0; i < tags.Length; i++)
                    this.tags.Add(tags[i].Item1, tags[i].Item2);
            }
        }

        public void CompilePrograms(VariantGenerator variantGenerator)
        {
            Variant GenerateVariant(KeyGroup<string, string> keywords) =>
                variantGenerator.Invoke(shaderSource, keywords);

            variants = new(keywords, GenerateVariant);
        }

        public KeyGroup<string, string> ValidateKeyGroup(KeyGroup<string, string> keyGroup) => 
            variants.ValidateCombination(keyGroup);

        public Variant GetVariant(KeyGroup<string, string>? keywordID = null) =>
            variants.GetValue(keywordID ?? KeyGroup<string, string>.Default);

        public bool TryGetVariant(KeyGroup<string, string>? keywordID, out Variant? variant) =>
            variants.TryGetValue(keywordID ?? KeyGroup<string, string>.Default, out variant);

        public void Dispose()
        {
            foreach (Variant program in variants.Values)
                foreach (Veldrid.Shader shader in program.compiledPrograms)
                    shader.Dispose();
        }

        public override int GetHashCode()
        {
            HashCode hash = new();

            hash.Add(name);
            hash.Add(blend);
            hash.Add(depthStencil);
            hash.Add(cullMode);
            hash.Add(depthClipEnabled);            

            foreach (var pair in tags)
            {
                hash.Add(pair.Key);
                hash.Add(pair.Value);
            }

            return hash.ToHashCode();
        }
    }
}