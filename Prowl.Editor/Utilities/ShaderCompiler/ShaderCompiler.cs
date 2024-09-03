// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using DirectXShaderCompiler.NET;

using Prowl.Runtime;

using SPIRVCross.NET;

using Veldrid;

#pragma warning disable

namespace Prowl.Editor.Utilities
{
    public static partial class ShaderCompiler
    {
        private static ShaderType StageToType(ShaderStages stages)
        {
            return stages switch
            {
                ShaderStages.Vertex => ShaderType.Vertex,
                ShaderStages.Geometry => ShaderType.Geometry,
                ShaderStages.TessellationControl => ShaderType.Hull,
                ShaderStages.TessellationEvaluation => ShaderType.Domain,
                ShaderStages.Fragment => ShaderType.Fragment
            };
        }


        public static ShaderDescription[] Compile(string code, EntryPoint[] entrypoints, (int, int) model, KeywordState keywords)
        {
            byte[][] compiledSPIRV = new byte[entrypoints.Length][];

            for (int i = 0; i < entrypoints.Length; i++)
            {
                DirectXShaderCompiler.NET.CompilerOptions options = new(StageToType(entrypoints[i].Stage).ToProfile(model.Item1, model.Item2));

                options.generateAsSpirV = true;
                options.useOpenGLMemoryLayout = true;
                options.entryPoint = entrypoints[i].Name;
                options.entrypointName = "main"; // Ensure 'main' entrypoint for OpenGL compatibility.

                foreach (var keyword in keywords.KeyValuePairs)
                {
                    if (!string.IsNullOrWhiteSpace(keyword.Key) && !string.IsNullOrWhiteSpace(keyword.Value))
                        options.SetMacro(keyword.Key, keyword.Value);
                }

                CompilationResult result = DirectXShaderCompiler.NET.ShaderCompiler.Compile(code, options, NoInclude);

                foreach (var res in result.messages)
                {
                    if (res.severity == CompilationMessage.MessageSeverity.Error)
                    {
                        throw new Exception($"Error compiling shader {res.filename} (at line: {res.line}, column: {res.column}): \n{res.message}");
                    }
                    else if (res.severity == CompilationMessage.MessageSeverity.Warning)
                    {
                        Debug.LogWarning($"Warning while compiling shader {res.filename} (at line: {res.line}, column: {res.column}): \n{res.message}");
                    }
                    else
                    {
                        Debug.LogWarning($"{res.message}");
                    }
                }

                compiledSPIRV[i] = result.objectBytes;
            }

            return compiledSPIRV.Zip(entrypoints, (x, y) => new ShaderDescription(y.Stage, x, "main")).ToArray();
        }


        // Fills the dictionary with every possible permutation for the given definitions, initializing values with the generator function
        public static ShaderVariant[] GenerateVariants(
            string sourceCode,
            EntryPoint[] entryPoints,
            (int, int) shaderModel,
            Dictionary<string, HashSet<string>> keywords)
        {
            List<ShaderVariant> variants = new();

            List<KeyValuePair<string, HashSet<string>>> combinations = (keywords ?? new() { { "", [""] } }).ToList();
            List<KeyValuePair<string, string>> combination = new(combinations.Count);

            using Context ctx = new Context();

            void GenerateRecursive(int depth)
            {
                if (depth == combinations.Count) // Reached the end for this permutation, add a result.
                {
                    variants.Add(GenerateVariant(ctx, sourceCode, entryPoints, shaderModel, new(combination)));

                    return;
                }

                var pair = combinations[depth];
                foreach (var value in pair.Value) // Go down a level for every value
                {
                    combination.Add(new(pair.Key, value));
                    GenerateRecursive(depth + 1);
                    combination.RemoveAt(combination.Count - 1); // Go up once we're done
                }
            }

            GenerateRecursive(0);

            return variants.ToArray();
        }


        public static ShaderVariant GenerateVariant(Context ctx, string source, EntryPoint[] entryPoints, (int, int) shaderModel, KeywordState state)
        {
            ShaderVariant variant = new ShaderVariant(state);

            ShaderDescription[] compiledSPIRV = Compile(source, entryPoints, shaderModel, state);

            ReflectedResourceInfo info = Reflect(ctx, compiledSPIRV);

            variant.Uniforms = info.uniforms;
            variant.UniformStages = info.stages;
            variant.VertexInputs = info.vertexInputs;

            variant.Direct3D11Shaders = CrossCompile(ctx, GraphicsBackend.Direct3D11, compiledSPIRV);
            variant.OpenGLShaders = CrossCompile(ctx, GraphicsBackend.OpenGL, compiledSPIRV);
            variant.OpenGLESShaders = CrossCompile(ctx, GraphicsBackend.OpenGLES, compiledSPIRV);
            variant.MetalShaders = CrossCompile(ctx, GraphicsBackend.Metal, compiledSPIRV);
            variant.VulkanShaders = compiledSPIRV;

            return variant;
        }


        static string NoInclude(string file)
        {
            return $"// Including {file}";
        }
    }
}
