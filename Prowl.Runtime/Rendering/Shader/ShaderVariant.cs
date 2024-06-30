using System;
using System.Collections.Generic;
using Prowl.Runtime.Utils;
using Veldrid;

namespace Prowl.Runtime
{
    public sealed class ShaderVariant
    {
        public readonly KeyGroup<string, string> VariantKeywords;
        public readonly List<MeshResource> VertexInputs;
        public readonly List<ShaderResource[]> ResourceSets;
        public readonly Veldrid.Shader[] CompiledPrograms;

        public ShaderVariant(KeyGroup<string, string> keywords, Veldrid.Shader[] programs)
        {
            this.VariantKeywords = keywords;
            this.VertexInputs = new();
            this.ResourceSets = new();
            this.CompiledPrograms = programs;
        }

        internal void Dispose()
        {
            foreach (var shader in CompiledPrograms)
                shader.Dispose();
        }
    }
}