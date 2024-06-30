using System;
using System.Collections.Generic;
using Prowl.Runtime.Utils;
using Veldrid;
using Veldrid.SPIRV;

using static System.Text.Encoding;

namespace Prowl.Runtime
{
    /// <summary>
    /// Default runtime shader cross-compiler for a vertex and fragment program. Does not compile other program types.
    /// Inputs and resources must be manually fed on creation- this class is intended to compile small shader programs
    /// </summary>
    public class RuntimeVariantCompiler : IVariantCompiler
    {
        public MeshResource[] Inputs;
        public ShaderResource[][] Resources;

        public ShaderVariant CompileVariant(ShaderSource[] sources, KeyGroup<string, string> keywords)
        {
            ShaderVariant variant = new ShaderVariant(keywords, CreateVertexFragment(sources[0].SourceCode, sources[1].SourceCode));

            variant.VertexInputs.AddRange(Inputs);
            variant.ResourceSets.AddRange(Resources);

            return variant;
        }

        public static Veldrid.Shader[] CreateVertexFragment(string vert, string frag)
        {
            CrossCompileOptions options = new()
            {
                FixClipSpaceZ = (Graphics.Device.BackendType == GraphicsBackend.OpenGL || Graphics.Device.BackendType == GraphicsBackend.OpenGLES) && !Graphics.Device.IsDepthRangeZeroToOne,
                InvertVertexOutputY = false,
                Specializations = Graphics.GetSpecializations()
            };

            ShaderDescription vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, UTF8.GetBytes(vert), "main");
            ShaderDescription fragmentShaderDesc = new ShaderDescription(ShaderStages.Fragment, UTF8.GetBytes(frag), "main");

            return Graphics.Factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc, options);
        }
    }
}