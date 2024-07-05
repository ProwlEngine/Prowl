using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Prowl.Runtime.Utils;
using Veldrid;
using System.Linq;

namespace Prowl.Runtime
{
    public sealed class ShaderVariant : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector]
        private KeywordState variantKeywords;

        [SerializeField, HideInInspector]
        private VertexLayoutDescription[] vertexInputs;

        [SerializeField, HideInInspector]
        private ShaderResource[][] resourceSets;

        [NonSerialized]
        private Dictionary<GraphicsBackend, ShaderDescription[]> compiledPrograms;

        // For serialization
        [SerializeField, HideInInspector]
        private byte[] serializedBackends;

        [SerializeField, HideInInspector]
        private ShaderDescription[][] serializedShaders;


        public KeywordState VariantKeywords => variantKeywords;
        public VertexLayoutDescription[] VertexInputs => vertexInputs;
        public ShaderResource[][] ResourceSets => resourceSets;
        public IEnumerable<KeyValuePair<GraphicsBackend, ShaderDescription[]>> CompiledPrograms => compiledPrograms;

        private ShaderVariant() { }

        public ShaderVariant(KeywordState keywords, (GraphicsBackend, ShaderDescription[])[] programs, VertexLayoutDescription[] vertexInputs, ShaderResource[][] resourceSets)
        {
            this.variantKeywords = keywords;
            this.vertexInputs = vertexInputs;
            this.resourceSets = resourceSets;
            this.compiledPrograms = new(programs.Select(x => new KeyValuePair<GraphicsBackend, ShaderDescription[]>(x.Item1, x.Item2)));
        }

        public ShaderDescription[] GetProgramsForBackend(GraphicsBackend? backend = null)
        {
            backend ??= Graphics.Device.BackendType;

            if (compiledPrograms.TryGetValue(backend.Value, out ShaderDescription[] programs))
                return programs;

            throw new Exception($"No compiled programs for graphics backend {backend}");
        }

        public void OnBeforeSerialize()
        {
            serializedBackends = compiledPrograms.Keys.Select(x => (byte)x).ToArray();
            serializedShaders = compiledPrograms.Values.ToArray();
        }

        public void OnAfterDeserialize()
        {
            compiledPrograms = new();

            for (int i = 0; i < serializedBackends.Length; i++)
                compiledPrograms.Add((GraphicsBackend)serializedBackends[i], serializedShaders[i]);
        }
    }
}