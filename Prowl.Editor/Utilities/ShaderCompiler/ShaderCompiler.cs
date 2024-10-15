// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using DirectXShaderCompiler.NET;

using Prowl.Runtime;

using SPIRVCross.NET;

using Veldrid;

#pragma warning disable

namespace Prowl.Editor;

public struct ShaderCreationArgs
{
    public string sourceCode;
    public EntryPoint[] entryPoints;
    public (int, int) shaderModel;
    public Dictionary<string, HashSet<string>> combinations;
}


public struct CompilationMessage
{
    public IReadOnlyList<CompilationFile> stackTrace;

    public LogSeverity severity;
    public string message;

    public string entrypoint;
    public KeywordState? keywords;


    public static CompilationMessage FromDXC(DirectXShaderCompiler.NET.CompilationMessage dxcMessage)
    {
        CompilationMessage message = new();

        message.severity = dxcMessage.severity switch
        {
            MessageSeverity.Info => LogSeverity.Normal,
            MessageSeverity.Warning => LogSeverity.Warning,
            MessageSeverity.Error => LogSeverity.Error,
        };

        message.stackTrace = dxcMessage.stackTrace;
        message.message = dxcMessage.message;

        return message;
    }
}


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


    public static ShaderDescription[] Compile(ShaderCreationArgs args, KeywordState keywords, FileIncluder includer, List<CompilationMessage> messages)
    {
        byte[][] compiledSPIRV = new byte[args.entryPoints.Length][];

        for (int i = 0; i < args.entryPoints.Length; i++)
        {
            DirectXShaderCompiler.NET.CompilerOptions options = new(StageToType(args.entryPoints[i].Stage).ToProfile(args.shaderModel.Item1, args.shaderModel.Item2));

            options.generateAsSpirV = true;
            options.useOpenGLMemoryLayout = true;
            options.entryPoint = args.entryPoints[i].Name;
            options.entrypointName = "main"; // Ensure 'main' entrypoint for OpenGL compatibility.

            foreach (var keyword in keywords.KeyValuePairs)
            {
                if (!string.IsNullOrWhiteSpace(keyword.Key) && !string.IsNullOrWhiteSpace(keyword.Value))
                    options.SetMacro(keyword.Key, keyword.Value);
            }

            CompilationResult result = DirectXShaderCompiler.NET.ShaderCompiler.Compile(args.sourceCode, options, includer.Include);

            for (int j = 0; j < result.messages.Length; j++)
            {
                CompilationMessage msg = CompilationMessage.FromDXC(result.messages[j]);

                msg.entrypoint = args.entryPoints[i].Name;
                msg.keywords = keywords;

                messages.Add(msg);
            }

            compiledSPIRV[i] = result.objectBytes;
        }

        return compiledSPIRV.Zip(args.entryPoints, (x, y) => new ShaderDescription(y.Stage, x, "main")).ToArray();
    }


    public static ShaderVariant[] GenerateVariants(ShaderCreationArgs args, FileIncluder includer, List<CompilationMessage> messages)
    {
        List<KeyValuePair<string, HashSet<string>>> combinations = [.. args.combinations];
        List<ShaderVariant> variantList = [];
        List<KeyValuePair<string, string>> combination = new(combinations.Count);

        using Context ctx = new Context();

        void GenerateRecursive(int depth)
        {
            if (depth == combinations.Count) // Reached the end for this permutation, add a result.
            {
                variantList.Add(GenerateVariant(ctx, args, new(combination), includer, messages));
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

        return variantList.ToArray();
    }


    public static ShaderVariant GenerateVariant(Context ctx, ShaderCreationArgs args, KeywordState state, FileIncluder includer, List<CompilationMessage> messages)
    {
        ShaderDescription[] compiledSPIRV = Compile(args, state, includer, messages);

        foreach (ShaderDescription desc in compiledSPIRV)
            if (desc.ShaderBytes == null || desc.ShaderBytes.Length == 0)
                return null;

        ReflectedResourceInfo info = Reflect(ctx, compiledSPIRV);

        ShaderVariant variant = new ShaderVariant(state);

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
}
