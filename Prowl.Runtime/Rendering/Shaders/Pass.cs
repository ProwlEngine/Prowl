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


        // Encapsulates a compiled program for this pass which can be identified by a keyword state
        public class Program
        {
            public List<VertexLayoutDescription> vertexInputs = new();
            public List<ResourceLayoutDescription> resourceDescriptions = new();
            public Veldrid.Shader[] passPrograms;
        }


        private Dictionary<KeywordState, Program> variantPrograms = new();


        public Pass(
            string name,
            (string, string)[] tags)
        {
            this.name = name;

            for (int i = 0; i < tags.Length; i++)
                this.tags.Add(tags[i].Item1, tags[i].Item2);
        }

        public void CreateProgram(Veldrid.Shader[] compiledShaders, KeywordState? keywordID = null)
        {
            Program program = new() 
            {
                passPrograms = compiledShaders
            };

            variantPrograms.Add(keywordID ?? KeywordState.Empty, program);
        }

        public Program GetProgram(KeywordState? keywordID = null)
        {
            if (variantPrograms.TryGetValue(keywordID ?? KeywordState.Empty, out Program program))
                return program;
            else
                throw new Exception("Could not find program for keyword ID");
        }

        public void AddVertexInput(string name, VertexElementSemantic semantic, VertexElementFormat format, KeywordState? keywordID = null)
        {
            if (variantPrograms.TryGetValue(keywordID ?? KeywordState.Empty, out Program program))
                program.vertexInputs.Add(new VertexLayoutDescription(new VertexElementDescription(name, semantic, format)));
            else
                throw new Exception("Could not find program for keyword ID");
        }

        public void AddResourceElement(ResourceLayoutElementDescription[] elements, KeywordState? keywordID = null)
        {
            if (variantPrograms.TryGetValue(keywordID ?? KeywordState.Empty, out Program program))
                program.resourceDescriptions.Add(new ResourceLayoutDescription(elements));
            else
                throw new Exception("Could not find program for keyword ID");
        }

        public void Dispose()
        {
            foreach (Program program in variantPrograms.Values)
                foreach (Veldrid.Shader shader in program.passPrograms)
                    shader.Dispose();
        }
    }
}