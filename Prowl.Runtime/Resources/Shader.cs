using Prowl.Runtime.Rendering.Shaders;
using Prowl.Echo;
using System.Collections.Generic;
using System;

namespace Prowl.Runtime.Resources
{
    /// <summary>
    /// The Shader class itself doesnt do much, It stores the properties of the shader and the shader code and Keywords.
    /// This is used in conjunction with the Material class to create shader variants with the correct keywords and to render things
    /// </summary>
    public sealed class Shader : EngineObject, ISerializationCallbackReceiver
    {
        [SerializeField]
        private ShaderProperty[] _properties;
        public IEnumerable<ShaderProperty> Properties => _properties;


        [SerializeField]
        private ShaderPass[] _passes;
        public IEnumerable<ShaderPass> Passes => _passes;


        private Dictionary<string, int> _nameIndexLookup = new();
        private Dictionary<string, List<int>> _tagIndexLookup = new();


        internal Shader() : base("New Shader") { }

        public Shader(string name, ShaderProperty[] properties, ShaderPass[] passes) : base(name)
        {
            _properties = properties;
            _passes = passes;

            OnAfterDeserialize();
        }

        private void RegisterPass(ShaderPass pass, int index)
        {
            if (!string.IsNullOrWhiteSpace(pass.Name))
            {
                if (!_nameIndexLookup.TryAdd(pass.Name, index))
                    throw new InvalidOperationException($"Pass with name {pass.Name} conflicts with existing pass at index {_nameIndexLookup[pass.Name]}. Ensure no two passes have equal names.");
            }

            foreach (KeyValuePair<string, string> pair in pass.Tags)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                if (!_tagIndexLookup.TryGetValue(pair.Key, out _))
                    _tagIndexLookup.Add(pair.Key, []);

                _tagIndexLookup[pair.Key].Add(index);
            }
        }

        public ShaderPass GetPass(int passIndex)
        {
            passIndex = Math.Clamp(passIndex, 0, _passes.Length - 1);
            return _passes[passIndex];
        }

        public ShaderPass GetPass(string passName)
        {
            return _passes[GetPassIndex(passName)];
        }

        public int GetPassIndex(string passName)
        {
            return _nameIndexLookup.GetValueOrDefault(passName, -1);
        }

        public int? GetPassWithTag(string tag, string? tagValue = null)
        {
            List<int> passes = GetPassesWithTag(tag, tagValue);
            return passes.Count > 0 ? passes[0] : null;
        }

        public List<int> GetPassesWithTag(string tag, string? tagValue = null)
        {
            List<int> passes = [];

            if (_tagIndexLookup.TryGetValue(tag, out List<int> passesWithTag))
            {
                foreach (int index in passesWithTag)
                {
                    ShaderPass pass = this._passes[index];

                    if (pass.HasTag(tag, tagValue))
                        passes.Add(index);
                }
            }

            return passes;
        }

        /// <summary>
        /// Loads a shader from a file path
        /// </summary>
        public static Shader LoadFromFile(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
                throw new System.IO.FileNotFoundException($"Shader file not found: {filePath}");

            string shaderCode = System.IO.File.ReadAllText(filePath);

            if (!AssetImporting.ShaderParser.ParseShader(filePath, shaderCode, path => {
                // Include resolver for #include directives
                string? absolutePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath)!, path));
                if (System.IO.File.Exists(absolutePath))
                    return System.IO.File.ReadAllText(absolutePath);
                return null;
            }, out Shader? shader))
            {
                throw new System.Exception($"Failed to parse shader: {filePath}");
            }

            if (shader == null)
                throw new System.Exception($"Shader parsing returned null: {filePath}");

            shader.AssetPath = filePath;
            return shader;
        }

        /// <summary>
        /// Loads a default embedded shader
        /// </summary>
        public static Shader LoadDefault(DefaultShader shader)
        {
            string fileName = shader switch
            {
                DefaultShader.Standard => "Standard.shader",
                DefaultShader.Invalid => "Invalid.shader",
                DefaultShader.UI => "UI.shader",
                DefaultShader.Gizmos => "Gizmos.shader",
                DefaultShader.Blit => "Blit.shader",
                DefaultShader.Depth => "Depth.shader",
                DefaultShader.ProceduralSkybox => "ProceduralSkybox.shader",
                DefaultShader.Tonemapper => "Tonemapper.shader",
                DefaultShader.TAA => "TAA.shader",
                DefaultShader.SSR => "SSR.shader",
                DefaultShader.Bloom => "Bloom.shader",
                DefaultShader.BokehDoF => "BokehDoF.shader",
                DefaultShader.GBufferCombine => "GBuffercombine.shader",
                DefaultShader.AmbientLight => "AmbientLight.shader",
                DefaultShader.DirectionalLight => "Directionallight.shader",
                DefaultShader.PointLight => "Pointlight.shader",
                DefaultShader.SpotLight => "Spotlight.shader",
                _ => throw new ArgumentException($"Unknown default shader: {shader}")
            };

            string resourcePath = $"Assets/Defaults/{fileName}";
            string shaderCode = EmbeddedResources.ReadAllText(resourcePath);

            if (!AssetImporting.ShaderParser.ParseShader(resourcePath, shaderCode, path => {
                // Include resolver for embedded resources
                try
                {
                    return EmbeddedResources.ReadAllText(path);
                }
                catch
                {
                    return null;
                }
            }, out Shader? result))
            {
                throw new System.Exception($"Failed to parse default shader: {shader}");
            }

            if (result == null)
                throw new System.Exception($"Default shader parsing returned null: {shader}");

            result.AssetPath = $"$Default:{shader}";
            return result;
        }

        /// <summary>
        /// Loads a default shader include file (for use by shader parser)
        /// </summary>
        internal static string LoadDefaultInclude(DefaultShaderInclude include)
        {
            string fileName = include switch
            {
                DefaultShaderInclude.Fragment => "Fragment.glsl",
                DefaultShaderInclude.PBR => "PBR.glsl",
                DefaultShaderInclude.Random => "Random.glsl",
                DefaultShaderInclude.ShaderVariables => "ShaderVariables.glsl",
                DefaultShaderInclude.Utilities => "Utilities.glsl",
                DefaultShaderInclude.VertexAttributes => "VertexAttributes.glsl",
                _ => throw new ArgumentException($"Unknown shader include: {include}")
            };

            return EmbeddedResources.ReadAllText($"Assets/Defaults/{fileName}");
        }

        /// <summary>
        /// Tries to convert an include file name to a DefaultShaderInclude enum
        /// </summary>
        internal static bool TryGetDefaultInclude(string includeName, out DefaultShaderInclude include)
        {
            // Remove .glsl extension if present
            includeName = includeName.Replace(".glsl", "", StringComparison.OrdinalIgnoreCase);

            return Enum.TryParse(includeName, true, out include);
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            for (int i = 0; i < _passes.Length; i++)
                RegisterPass(_passes[i], i);
        }
    }
}
