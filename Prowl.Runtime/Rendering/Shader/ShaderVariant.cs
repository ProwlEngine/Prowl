using System;
using System.Collections.Generic;
using Prowl.Runtime.Utils;
using Veldrid;

namespace Prowl.Runtime
{
    public sealed class ShaderVariant
    {
        public readonly KeyGroup<string, string> VariantKeywords;
        public readonly VertexLayoutDescription[] VertexInputs;
        public readonly ShaderResource[][] ResourceSets;
        public readonly Veldrid.Shader[] CompiledPrograms;

        public ShaderVariant(KeyGroup<string, string> keywords, Veldrid.Shader[] programs, VertexLayoutDescription[] vertexInputs, ShaderResource[][] resourceSets)
        {
            this.VariantKeywords = keywords;
            this.VertexInputs = vertexInputs;
            this.ResourceSets = resourceSets;
            this.CompiledPrograms = programs;
        }

        internal void Dispose()
        {
            foreach (var shader in CompiledPrograms)
                shader.Dispose();
        }
    }
}