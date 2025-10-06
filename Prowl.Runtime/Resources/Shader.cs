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

        public static AssetRef<Shader> Find(string assetPath) => Game.AssetProvider.LoadAsset<Shader>(assetPath);

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            for (int i = 0; i < _passes.Length; i++)
                RegisterPass(_passes[i], i);
        }
    }
}
