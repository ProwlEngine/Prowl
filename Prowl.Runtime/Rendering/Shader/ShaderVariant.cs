using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Prowl.Runtime.Utils;
using Veldrid;
using System.Linq;

namespace Prowl.Runtime
{
    public sealed class ShaderVariant : ISerializable
    {
        [SerializeField]
        private KeywordState variantKeywords;

        [SerializeField]
        private VertexLayoutDescription[] vertexInputs;

        [SerializeField]
        private ShaderResource[][] resourceSets;

        private Dictionary<GraphicsBackend, ShaderDescription[]> compiledPrograms;

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

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            var property = Serializer.Serialize(this, ctx);

            SerializedProperty serializedPrograms = SerializedProperty.NewList();

            foreach (var program in CompiledPrograms)
            {
                SerializedProperty serializedProgram = SerializedProperty.NewCompound();

                serializedProgram.Add("Backend", new((byte)program.Key));

                SerializedProperty serializedSubPrograms = SerializedProperty.NewList();

                foreach (var value in program.Value)
                {
                    SerializedProperty serializedSubProgram = SerializedProperty.NewCompound();

                    serializedSubProgram.Add("Debug", new(value.Debug));
                    serializedSubProgram.Add("EntryPoint", new(value.EntryPoint));
                    serializedSubProgram.Add("Stage", new((byte)value.Stage));
                    serializedSubProgram.Add("CompiledBytes", new(value.ShaderBytes));

                    serializedSubPrograms.ListAdd(serializedSubProgram);
                }

                serializedProgram.Add("CompiledPrograms", serializedSubPrograms);

                serializedPrograms.ListAdd(serializedProgram);
            }

            property.Add("Programs", serializedPrograms);

            return property;
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            Serializer.DeserializeInto(value, this, ctx);

            compiledPrograms = new();

            SerializedProperty serializedPrograms = value.Get("Programs");

            for (int i = 0; i < serializedPrograms.Count; i++)
            {
                SerializedProperty serializedProgram = serializedPrograms[i];

                SerializedProperty serializedSubPrograms = serializedProgram.Get("CompiledPrograms");

                ShaderDescription[] descriptions = new ShaderDescription[compiledPrograms.Count];

                for (int j = 0; j < descriptions.Length; j++)
                {
                    SerializedProperty serializedSubProgram = serializedSubPrograms[j];

                    descriptions[j] = new ShaderDescription()
                    {
                        Debug = serializedSubProgram.Get("Debug").BoolValue,
                        EntryPoint = serializedSubProgram.Get("EntryPoint").StringValue,
                        Stage = (ShaderStages)serializedSubProgram.Get("Stage").ByteValue,
                        ShaderBytes = serializedSubProgram.Get("CompiledBytes").ByteArrayValue
                    };
                }

                compiledPrograms.Add((GraphicsBackend)serializedProgram.Get("Backend").ByteValue, descriptions);
            }
        }
    }
}