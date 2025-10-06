using Prowl.Runtime.GraphicsBackend;
using Prowl.Runtime.GraphicsBackend.Primitives;
using Prowl.Echo;
using System.Collections.Generic;
using System;

namespace Prowl.Runtime.Rendering.Shaders
{
    public sealed class ShaderPass
    {
        [SerializeField] private string _name;

        [SerializeField] private Dictionary<string, string> _tags;
        [SerializeField] private RasterizerState _rasterizerState;

        [SerializeField] private string _vertexSource;
        [SerializeField] private string _fragmentSource;
        [SerializeField] private string _fallbackAsset;

        [SerializeIgnore]
        private Dictionary<string, GraphicsProgram> _variants;


        /// <summary>
        /// The name to identify this <see cref="ShaderPass"/>
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// The tags to identify this <see cref="ShaderPass"/>
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> Tags => _tags;

        /// <summary>
        /// The blending options to use when rendering this <see cref="ShaderPass"/>
        /// </summary>
        public RasterizerState State => _rasterizerState;

        public IEnumerable<KeyValuePair<string, GraphicsProgram>> Variants => _variants;


        private ShaderPass() { }

        public ShaderPass(string name, Dictionary<string, string>? tags, RasterizerState state, string vertexSource, string fragmentSource, string fallbackAsset)
        {
            _name = name;

            _tags = tags ?? [];
            _rasterizerState = state;

            _vertexSource = vertexSource;
            _fragmentSource = fragmentSource;
            _fallbackAsset = fallbackAsset;

            _variants = [];
        }

        public bool TryGetVariantProgram(Dictionary<string, bool>? keywordID, out GraphicsProgram variant)
        {
            string keywords = string.Empty;
            if (keywordID != null)
            {
                foreach (var kvp in keywordID)
                {
                    if (kvp.Value)
                        keywords += $"{kvp.Key};";
                }
            }

            if (_variants.TryGetValue(keywords, out variant))
                return true;

            string frag = _fragmentSource;
            string vert = _vertexSource;
            if (string.IsNullOrEmpty(frag)) throw new Exception($"Failed to compile shader pass of {Name}. Fragment Shader is null or empty.");
            if (string.IsNullOrEmpty(vert)) throw new Exception($"Failed to compile shader pass of {Name}. Vertex Shader is null or empty.");

            frag = frag.Insert(0, $"#define FRAGMENT_VERSION 1\n");
            vert = vert.Insert(0, $"#define FRAGMENT_VERSION 1\n");

            if (keywordID != null)
            {
                foreach (var kvp in keywordID)
                {
                    if (!kvp.Value) continue;

                    frag = frag.Insert(0, $"#define {kvp.Key}\n");
                    vert = vert.Insert(0, $"#define {kvp.Key}\n");
                }
            }

            frag = frag.Insert(0, $"#version 410\n");
            vert = vert.Insert(0, $"#version 410\n");


            Debug.Log("Compiling shader pass " + Name + " with keywords: " + keywords);

            try
            {
                variant = Graphics.Device.CompileProgram(frag, vert, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to compile shader pass of {Name}. Exception: {e.Message}");

                // Use the Invalid shader as fallback
                var fallbackShader = Resources.Shader.LoadDefault(Resources.DefaultShader.Invalid);
                if (fallbackShader != null)
                {
                    if (!fallbackShader.GetPass(0).TryGetVariantProgram(null, out variant))
                        throw new Exception($"Failed to compile shader pass of {Name}. Fallback shader also failed to compile.");
                }
                else
                {
                    throw new Exception($"Failed to compile shader pass of {Name}. Fallback shader is null.");
                }
            }

            _variants.Add(keywords, variant);

            return true;
        }

        public bool HasTag(string tag, string? tagValue = null)
        {
            if (_tags.TryGetValue(tag, out string value))
                return tagValue == null || value == tagValue;

            return false;
        }
    }
}
