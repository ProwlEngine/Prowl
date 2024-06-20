using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public class Pass 
    {
        /// <summary>
        /// The name to identify this <see cref="Pass"/> 
        /// </summary>
        public string name;

        /// <summary>
        /// The tags to identify this <see cref="Pass"/> 
        /// </summary>
        public Dictionary<string, string> tags = new();

        /// <summary>
        /// The blending options to use when rendering this <see cref="Pass"/> 
        /// </summary>
        public BlendStateDescription blend = BlendStateDescription.SingleOverrideBlend;

        /// <summary>
        /// The depth stencil state to use when rendering this <see cref="Pass"/> 
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
            public KeywordState keywords;
            public List<(MeshResource, VertexLayoutDescription)> vertexInputs = new();
            public List<ShaderResource[]> resourceSets = new();
            public Veldrid.Shader[] compiledPrograms;
        }


        private Dictionary<KeywordState, Variant> variants = new();


        public Pass(string name, (string, string)[] tags)
        {
            this.name = name;

            if (tags == null)
                return;

            for (int i = 0; i < tags.Length; i++)
                this.tags.Add(tags[i].Item1, tags[i].Item2);
        }

        public void CreateProgram(Veldrid.Shader[] compiledPrograms, KeywordState? keywordID = null)
        {
            keywordID ??= KeywordState.Empty;

            Variant program = new() 
            {
                keywords = keywordID.Value,
                compiledPrograms = compiledPrograms
            };

            variants.Add(keywordID.Value, program);
        }

        public Variant GetVariant(KeywordState? keywordID = null)
        {
            if (variants.TryGetValue(keywordID ?? KeywordState.Empty, out Variant program))
                return program;
            else
                throw new Exception("Could not find variant for keyword ID");
        }

        public void AddVertexInput(MeshResource resource, KeywordState? keywordID = null)
        {
            if (variants.TryGetValue(keywordID ?? KeywordState.Empty, out Variant program))
                program.vertexInputs.Add((resource, default));
            else
                throw new Exception("Could not find variant for keyword ID");
        }

        public void AddCustomVertexInput(VertexLayoutDescription description, KeywordState? keywordID = null)
        {
            if (variants.TryGetValue(keywordID ?? KeywordState.Empty, out Variant program))
                program.vertexInputs.Add((MeshResource.Custom, description));
            else
                throw new Exception("Could not find variant for keyword ID");
        }

        public void AddResourceElement(ShaderResource[] resourceTypes, KeywordState? keywordID = null)
        {
            if (variants.TryGetValue(keywordID ?? KeywordState.Empty, out Variant program))
                program.resourceSets.Add(resourceTypes);
            else
                throw new Exception("Could not variant for keyword ID");
        }

        public void Dispose()
        {
            foreach (Variant program in variants.Values)
                foreach (Veldrid.Shader shader in program.compiledPrograms)
                    shader.Dispose();
        }
    }
}