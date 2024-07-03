using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Prowl.Runtime.Utils;
using Veldrid;
using System.Linq;

namespace Prowl.Runtime
{
    public struct ShaderSource
    {
        public ShaderStages Stage;
        public string SourceCode;
    }

    public struct ShaderPassDescription
    {
        public Dictionary<string, string> Tags;
        public BlendStateDescription BlendState;
        public DepthStencilStateDescription DepthStencilState;
        public FaceCullMode CullingMode;
        public bool DepthClipEnabled;

        public ShaderSource[] ShaderSources;
        public Dictionary<string, ImmutableHashSet<string>> Keywords;


        public ShaderPassDescription()
        {
            Tags = [ ];

            Keywords = new() { { string.Empty, [ string.Empty ] } };

            BlendState = BlendStateDescription.SingleOverrideBlend;
            
            DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual
            );

            CullingMode = FaceCullMode.Back;
            
            DepthClipEnabled = true;
        }
    }

    public sealed class ShaderPass 
    {
        /// <summary>
        /// The name to identify this <see cref="ShaderPass"/> 
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The tags to identify this <see cref="ShaderPass"/> 
        /// </summary>
        public readonly ImmutableDictionary<string, string> Tags;

        /// <summary>
        /// The blending options to use when rendering this <see cref="ShaderPass"/> 
        /// </summary>
        public readonly BlendStateDescription Blend;

        /// <summary>
        /// The depth stencil state to use when rendering this <see cref="ShaderPass"/> 
        /// </summary>
        public readonly DepthStencilStateDescription DepthStencilState;

        public readonly FaceCullMode CullMode = FaceCullMode.Back;
        public readonly bool DepthClipEnabled = true;

        private readonly int Hash;

        
        private readonly ShaderSource[] shaderSource;
        public IEnumerable<ShaderSource> ShaderSource => shaderSource;

        private readonly Dictionary<string, ImmutableHashSet<string>> keywords;
        public IEnumerable<KeyValuePair<string, ImmutableHashSet<string>>> Keywords => keywords;

        private PermutationMap<string, string, ShaderVariant> variants;


        public ShaderPass(string name, ShaderPassDescription description) 
        {
            this.Name = name;
            
            this.shaderSource = description.ShaderSources;

            this.Blend = description.BlendState;
            this.DepthStencilState = description.DepthStencilState;
            this.DepthClipEnabled = description.DepthClipEnabled;
            this.CullMode = description.CullingMode;

            this.keywords = new();

            if (description.Keywords != null && description.Keywords.Count != 0)
            {
                // Copy keywords
                foreach (var value in description.Keywords)
                    keywords.Add(value.Key, ImmutableHashSet.CreateRange(value.Value));
            }
            else
            {
                keywords.Add(string.Empty, [ string.Empty ]);
            }

            this.Tags = ImmutableDictionary.CreateRange(description.Tags);
            this.Hash = GenerateHash();
        }

        public void CompilePrograms(IVariantCompiler variantGenerator)
        {
            ShaderVariant GenerateVariant(KeyGroup<string, string> keywords)
            {
                return variantGenerator.CompileVariant(shaderSource, keywords);
            }

            variants = new(keywords, GenerateVariant);
        }

        public KeyGroup<string, string> ValidateKeyGroup(KeyGroup<string, string> keyGroup) => 
            variants.ValidateCombination(keyGroup);

        public ShaderVariant GetVariant(KeyGroup<string, string>? keywordID = null) =>
            variants.GetValue(keywordID ?? KeyGroup<string, string>.Default);

        public bool TryGetVariant(KeyGroup<string, string>? keywordID, out ShaderVariant? variant) =>
            variants.TryGetValue(keywordID ?? KeyGroup<string, string>.Default, out variant);

        public ShaderPassDescription GetDescription()
        {
            return new ShaderPassDescription()
            {
                Tags = new(Tags),
                BlendState = Blend,
                DepthStencilState = DepthStencilState,
                CullingMode = CullMode,
                DepthClipEnabled = DepthClipEnabled,
                ShaderSources = shaderSource,
                Keywords = keywords
            };
        }

        private int GenerateHash()
        {
            HashCode hash = new();

            hash.Add(Name);
            hash.Add(Blend);
            hash.Add(DepthStencilState);
            hash.Add(CullMode);
            hash.Add(DepthClipEnabled);     

            foreach (var source in shaderSource)
            {
                hash.Add(source.Stage);
            }       

            foreach (var pair in Tags)
            {
                hash.Add(pair.Key);
                hash.Add(pair.Value);
            }

            foreach (var pair in keywords)
            {
                hash.Add(pair.Key);

                foreach (var value in pair.Value)
                    hash.Add(value);
            }

            return hash.ToHashCode();
        }

        public override int GetHashCode()
        {
            return Hash;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not ShaderPass other)
                return false;

            return Equals(other);
        }

        public bool Equals(ShaderPass other)
        {
            return Hash == other.Hash;
        }

        public void Dispose()
        {
            foreach (ShaderVariant program in variants.Values)
                program.Dispose();
        }
    }
}