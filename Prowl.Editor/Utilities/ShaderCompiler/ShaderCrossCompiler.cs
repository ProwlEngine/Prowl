// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Runtime;

using SPIRVCross.NET;
using SPIRVCross.NET.GLSL;
using SPIRVCross.NET.HLSL;
using SPIRVCross.NET.MSL;

using Veldrid;

#pragma warning disable

namespace Prowl.Editor.Utilities
{
    public struct ReflectedResourceInfo
    {
        public VertexInput[] vertexInputs;
        public ShaderUniform[] uniforms;
        public ShaderStages[] stages;
    }

    public static partial class ShaderCompiler
    {
        public static ReflectedResourceInfo Reflect(Context context, ShaderDescription[] compiledSPIRV)
        {
            VertexInput[] vertexInputs = [];

            List<ShaderUniform> uniforms = [];
            List<ShaderStages> stages = [];

            for (int i = 0; i < compiledSPIRV.Length; i++)
            {
                ShaderDescription shader = compiledSPIRV[i];

                ParsedIR IR = context.ParseSpirv(shader.ShaderBytes);

                var compiler = context.CreateReflector(IR);

                var resources = compiler.CreateShaderResources();

                if (shader.Stage == ShaderStages.Vertex)
                    vertexInputs = VertexInputReflector.GetStageInputs(compiler, resources, Mesh.MeshSemantics.TryGetValue);

                var stageUniforms = UniformReflector.GetUniforms(compiler, resources);

                MergeUniforms(uniforms, stages, stageUniforms, shader.Stage);
            }

            return new ReflectedResourceInfo() { vertexInputs = vertexInputs, uniforms = uniforms.ToArray(), stages = stages.ToArray() };
        }


        public static ShaderDescription[] CrossCompile(Context context, GraphicsBackend backend, ShaderDescription[] compiledSPIRV)
        {
            ShaderDescription[] result = new ShaderDescription[compiledSPIRV.Length];

            for (int i = 0; i < result.Length; i++)
            {
                ShaderDescription shader = compiledSPIRV[i];
                result[i] = CrossCompile(context, backend, shader.Stage, shader.EntryPoint, shader.ShaderBytes);
            }

            return result;
        }


        private static ShaderDescription CrossCompile(
            Context context,
            GraphicsBackend backend,
            ShaderStages stage,
            string entrypoint,
            byte[] sourceSPIRV)
        {
            ShaderDescription shader = new();

            shader.Stage = stage;
            shader.EntryPoint = entrypoint;

            ParsedIR IR = context.ParseSpirv(sourceSPIRV);

            shader.ShaderBytes = backend switch
            {
                GraphicsBackend.Direct3D11 => CompileHLSL(context, IR),
                GraphicsBackend.Metal => CompileMSL(context, IR),
                GraphicsBackend.OpenGL => CompileGLSL(context, IR, false),
                GraphicsBackend.OpenGLES => CompileGLSL(context, IR, true),
                _ => sourceSPIRV,
            };

            return shader;
        }


        private static byte[] CompileHLSL(Context context, ParsedIR IR)
        {
            HLSLCrossCompiler compiler = context.CreateHLSLCompiler(IR);

            compiler.hlslOptions.shaderModel = 50;
            compiler.hlslOptions.pointSizeCompat = true;

            string c = compiler.Compile();

            return Encoding.ASCII.GetBytes(c);
        }


        private static byte[] CompileMSL(Context context, ParsedIR IR)
        {
            MSLCrossCompiler compiler = context.CreateMSLCompiler(IR);

            return Encoding.UTF8.GetBytes(compiler.Compile());
        }


        private static byte[] CompileGLSL(Context context, ParsedIR IR, bool es, bool supportsCompute = true)
        {
            GLSLCrossCompiler compiler = context.CreateGLSLCompiler(IR);

            compiler.glslOptions.ES = es;

            if (supportsCompute)
                compiler.glslOptions.version = !es ? 430u : 310u;
            else
                compiler.glslOptions.version = !es ? 330u : 300u;

            compiler.BuildDummySamplerForCombinedImages(out _);
            compiler.BuildCombinedImageSamplers();

            foreach (var res in compiler.GetCombinedImageSamplers())
                compiler.SetName(res.combined_id, compiler.GetName(res.image_id));

            var resources = compiler.CreateShaderResources();

            // Removes annoying 'type_' prefix
            foreach (var res in resources.UniformBuffers)
                compiler.SetName(res.base_type_id, compiler.GetName(res.id));

            string c = compiler.Compile();

            return Encoding.ASCII.GetBytes(c);
        }


        private static void MergeUniforms(List<ShaderUniform> uniforms, List<ShaderStages> stages, ShaderUniform[] other, ShaderStages stage)
        {
            foreach (var ub in other)
            {
                int match = uniforms.FindIndex(x => x.IsEqual(ub));

                if (match == -1)
                {
                    // No match, add the uniform
                    uniforms.Add(ub);
                    stages.Add(stage);
                }
                else
                {
                    // Uniform already exists, OR the shader stage.
                    stages[match] |= stage;
                }
            }
        }
    }
}
