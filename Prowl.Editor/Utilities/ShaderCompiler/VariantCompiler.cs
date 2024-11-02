// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;

using SPIRVCross.NET;

using Veldrid;

namespace Prowl.Editor;


public static class VariantCompiler
{
    public static List<KeywordState> GeneratePermutations(List<KeyValuePair<string, HashSet<string>>> combinations)
    {
        List<KeywordState> permutationList = [];
        List<KeyValuePair<string, string>> combination = new(combinations.Count);

        void GenerateRecursive(int depth)
        {
            if (depth == combinations.Count) // Reached the end for this permutation, add a result.
            {
                permutationList.Add(new(combination));
                return;
            }

            KeyValuePair<string, HashSet<string>> pair = combinations[depth];
            foreach (string value in pair.Value) // Go down a level for every value
            {
                combination.Add(new(pair.Key, value));

                GenerateRecursive(depth + 1);

                combination.RemoveAt(combination.Count - 1); // Go up once we're done
            }
        }

        GenerateRecursive(0);

        return permutationList;
    }


    public static ShaderVariant? CompileVariant(Context ctx, ShaderCreationArgs args, KeywordState state, FileIncluder includer, List<CompilationMessage> messages)
    {
        ShaderDescription[]? compiledSPIRV = ShaderCompiler.Compile(args, state, includer, messages);

        if (compiledSPIRV == null)
            return null;

        ReflectedResourceInfo info = ShaderCrossCompiler.Reflect(ctx, compiledSPIRV);

        ShaderVariant variant = new ShaderVariant(state)
        {
            Uniforms = info.uniforms,
            UniformStages = info.stages,
            VertexInputs = info.vertexInputs,

            Direct3D11Shaders = ShaderCrossCompiler.CrossCompile(ctx, GraphicsBackend.Direct3D11, compiledSPIRV),
            OpenGLShaders = ShaderCrossCompiler.CrossCompile(ctx, GraphicsBackend.OpenGL, compiledSPIRV),
            OpenGLESShaders = ShaderCrossCompiler.CrossCompile(ctx, GraphicsBackend.OpenGLES, compiledSPIRV),
            MetalShaders = ShaderCrossCompiler.CrossCompile(ctx, GraphicsBackend.Metal, compiledSPIRV),

            VulkanShaders = compiledSPIRV
        };

        return variant;
    }


    public static ComputeVariant? CompileComputeVariant(Context ctx, ShaderCreationArgs args, KeywordState state, FileIncluder includer, List<CompilationMessage> messages)
    {
        if (args.entryPoints == null || args.entryPoints.Length != 1)
            return null;

        ShaderDescription[]? compiledSPIRV = ShaderCompiler.Compile(args, state, includer, messages);

        if (compiledSPIRV == null)
            return null;

        ReflectedResourceInfo info = ShaderCrossCompiler.Reflect(ctx, compiledSPIRV);

        ComputeVariant variant = new ComputeVariant(state)
        {
            Uniforms = info.uniforms,
            ThreadGroupSizeX = info.threadsX,
            ThreadGroupSizeY = info.threadsY,
            ThreadGroupSizeZ = info.threadsZ,

            Direct3D11Shader = ShaderCrossCompiler.CrossCompile(ctx, GraphicsBackend.Direct3D11, compiledSPIRV)[0],
            OpenGLShader = ShaderCrossCompiler.CrossCompile(ctx, GraphicsBackend.OpenGL, compiledSPIRV)[0],
            OpenGLESShader = ShaderCrossCompiler.CrossCompile(ctx, GraphicsBackend.OpenGLES, compiledSPIRV)[0],
            MetalShader = ShaderCrossCompiler.CrossCompile(ctx, GraphicsBackend.Metal, compiledSPIRV)[0],

            VulkanShader = compiledSPIRV[0]
        };

        return variant;
    }
}
