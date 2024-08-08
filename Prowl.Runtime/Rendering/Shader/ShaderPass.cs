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

    public struct ShaderPassDescription()
    {
        public Dictionary<string, string>? Tags;
        public BlendStateDescription? BlendState;
        public DepthStencilStateDescription? DepthStencilState;
        public FaceCullMode? CullingMode;
        public bool? DepthClipEnabled;

        public Dictionary<string, HashSet<string>>? Keywords;


        private static T? SetDefault<T>(T? currentValue, T? defaultValue)
        {
            if (currentValue == null && defaultValue != null)
                return defaultValue;

            return currentValue;
        }

        public void ApplyDefaults(ShaderPassDescription defaults)
        {
            Tags = SetDefault(Tags, defaults.Tags);
            BlendState = SetDefault(BlendState, defaults.BlendState);
            DepthStencilState = SetDefault(DepthStencilState, defaults.DepthStencilState);
            CullingMode = SetDefault(CullingMode, defaults.CullingMode);
            DepthClipEnabled = SetDefault(DepthClipEnabled, defaults.DepthClipEnabled);
            Keywords = SetDefault(Keywords, defaults.Keywords);
        }
    }

    public sealed class ShaderPass : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector]
        private string name;

        [SerializeField, HideInInspector]
        private Dictionary<string, string> tags;

        [SerializeField, HideInInspector]
        private BlendStateDescription blend;

        [SerializeField, HideInInspector]
        private DepthStencilStateDescription depthStencilState;

        [SerializeField, HideInInspector]
        private FaceCullMode cullMode = FaceCullMode.Back;

        [SerializeField, HideInInspector]
        private bool depthClipEnabled = true;


        [NonSerialized]
        private Dictionary<string, HashSet<string>> keywords;

        [SerializeField, HideInInspector]
        private string[] serializedKeywordKeys;

        [SerializeField, HideInInspector]
        private string[][] serializedKeywordValues;


        [NonSerialized]
        private Dictionary<KeywordState, ShaderVariant> variants;

        [SerializeField, HideInInspector]
        private KeywordState[] serializedVariantKeys;
        
        [SerializeField, HideInInspector]
        private ShaderVariant[] serializedVariants;

        [SerializeField, HideInInspector]
        private IVariantCompiler compiler;


        /// <summary>
        /// The name to identify this <see cref="ShaderPass"/> 
        /// </summary>
        public string Name => name;

        /// <summary>
        /// The tags to identify this <see cref="ShaderPass"/> 
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> Tags => tags;

        /// <summary>
        /// The blending options to use when rendering this <see cref="ShaderPass"/> 
        /// </summary>
        public BlendStateDescription Blend => blend;

        /// <summary>
        /// The depth stencil state to use when rendering this <see cref="ShaderPass"/> 
        /// </summary>
        public DepthStencilStateDescription DepthStencilState => depthStencilState;

        public FaceCullMode CullMode => cullMode;
        public bool DepthClipEnabled => depthClipEnabled;


        public IEnumerable<KeyValuePair<string, HashSet<string>>> Keywords => keywords;
        public IEnumerable<KeyValuePair<KeywordState, ShaderVariant>> Variants => variants;


        private ShaderPass() { }

        public ShaderPass(string name, ShaderPassDescription description, IVariantCompiler compiler) 
        {
            this.name = name;

            this.tags = description.Tags ?? new();     
            this.blend = description.BlendState ?? BlendStateDescription.SingleOverrideBlend;
            this.depthStencilState = description.DepthStencilState ?? DepthStencilStateDescription.DepthOnlyLessEqual;
            this.cullMode = description.CullingMode ?? FaceCullMode.Back;
            this.depthClipEnabled = description.DepthClipEnabled ?? true;       
            this.keywords = description.Keywords ?? new() { { "", [] } };

            this.compiler = compiler;

            GenerateVariants();
        }

        public ShaderVariant GetVariant(KeywordState? keywordID = null)
            => GetOrCompile(ValidateKeyword(keywordID ?? KeywordState.Empty));

        public bool TryGetVariant(KeywordState? keywordID, out ShaderVariant? variant)
        {
            keywordID ??= KeywordState.Empty;
            variant = null;

            if (!variants.ContainsKey(keywordID))
                return false;

            variant = GetOrCompile(keywordID);
            return true;
        }

        private ShaderVariant GetOrCompile(KeywordState keywordID)
        {
            var variant = variants[keywordID];

            if (variant != null)
                return variant;
            
            if (compiler == null)
                throw new Exception("Cannot compile shader variant. Compiler for pass is null.");

            variant = compiler.CompileVariant(keywordID);
            variants[keywordID] = variant;

            return variant;
        }

        public bool HasTag(string tag, string? tagValue = null)
        {   
            if (tags.TryGetValue(tag, out string value))
                return tagValue == null || value == tagValue;

            return false;
        }

        // Fills the dictionary with every possible permutation for the given definitions, initializing values with the generator function
        private void GenerateVariants()
        {   
            this.variants = new();

            List<KeyValuePair<string, HashSet<string>>> combinations = keywords.ToList();
            List<KeyValuePair<string, string>> combination = new(combinations.Count);

            void GenerateRecursive(int depth)
            {
                if (depth == combinations.Count) // Reached the end for this permutation, add a result.
                {
                    KeywordState key = new(combination);
                    variants.Add(key, null);
 
                    return;
                }

                var pair = combinations[depth];
                foreach (var value in pair.Value) // Go down a level for every value
                {
                    combination.Add(new(pair.Key, value));
                    GenerateRecursive(depth + 1);
                    combination.RemoveAt(combination.Count - 1); // Go up once we're done
                }
            }

            GenerateRecursive(0);
        }

        public KeywordState ValidateKeyword(KeywordState key)
        {
            KeywordState combinedKey = new();

            foreach (var definition in keywords)
            {
                string defaultValue = definition.Value.First();
                string value = key.GetKey(definition.Key, defaultValue);
                value = definition.Value.Contains(value) ? value : defaultValue;

                combinedKey.SetKey(definition.Key, value);
            }

            return combinedKey;
        }

        public void OnBeforeSerialize()
        {
            serializedKeywordKeys = keywords.Keys.ToArray();
            serializedKeywordValues = keywords.Values.Select(x => x.ToArray()).ToArray();

            serializedVariantKeys = variants.Keys.ToArray();
            serializedVariants = variants.Values.ToArray();
        }

        public void OnAfterDeserialize()
        {
            keywords = new();

            for (int i = 0; i < serializedKeywordKeys.Length; i++)
                keywords.Add(serializedKeywordKeys[i], new(serializedKeywordValues[i]));

            variants = new();

            for (int i = 0; i < serializedVariantKeys.Length; i++)
                variants.Add(serializedVariantKeys[i], serializedVariants[i]);
        }
    }
}