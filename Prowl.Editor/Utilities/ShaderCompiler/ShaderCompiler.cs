// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Glslang.NET;

using Prowl.Runtime;
using Prowl.Runtime.Rendering;

using SPIRVCross.NET;

using Veldrid;

using Program = Glslang.NET.Program;
using Shader = Glslang.NET.Shader;

#pragma warning disable

namespace Prowl.Editor;

public struct ShaderCreationArgs
{
    public string sourceCode;
    public EntryPoint[] entryPoints;
    public (int, int) shaderModel;
    public Dictionary<string, HashSet<string>> combinations;
}


public struct CompilationFile
{
    public string filename;
    public int line;
    public int column;
}


public struct CompilationMessage
{
    public IReadOnlyList<CompilationFile> stackTrace;

    public LogSeverity severity;
    public string message;

    public string entrypoint;
    public KeywordState? keywords;


    public CompilationMessage()
    {
        severity = LogSeverity.Normal;
        message = "";
        entrypoint = "";
        keywords = null;
        stackTrace = [];
    }
}


public static partial class ShaderCompiler
{
    private static ShaderStage StageToType(ShaderStages stages)
    {
        return stages switch
        {
            ShaderStages.Vertex => ShaderStage.Vertex,
            ShaderStages.Geometry => ShaderStage.Geometry,
            ShaderStages.TessellationControl => ShaderStage.TessControl,
            ShaderStages.TessellationEvaluation => ShaderStage.TessEvaluation,
            ShaderStages.Fragment => ShaderStage.Fragment,
            ShaderStages.Compute => ShaderStage.Compute,
        };
    }


    private static void CheckMessages(string messageText, List<CompilationMessage> messages)
    {
        if (!string.IsNullOrWhiteSpace(messageText))
            Debug.Log(messageText);
    }


    public static ShaderDescription[] Compile(ShaderCreationArgs args, KeywordState keywords, FileIncluder includer, List<CompilationMessage> messages)
    {
        ShaderDescription[] outputs = new ShaderDescription[args.entryPoints.Length];

        using Glslang.NET.Program program = new Glslang.NET.Program();

        CompilationInput input = new CompilationInput()
        {
            language = SourceType.HLSL,
            stage = ShaderStage.Fragment,
            client = ClientType.Vulkan,
            clientVersion = TargetClientVersion.Vulkan_1_1,
            targetLanguage = TargetLanguage.SPV,
            targetLanguageVersion = TargetLanguageVersion.SPV_1_3,
            defaultVersion = 500,
            code = args.sourceCode,
            hlslFunctionality1 = true,
            defaultProfile = ShaderProfile.None,
            forceDefaultVersionAndProfile = false,
            forwardCompatible = false,
            fileIncluder = includer.Include,
            messages = MessageType.Enhanced | MessageType.ReadHlsl | MessageType.DisplayErrorColumn,
        };

        string fileName = Path.GetFileName(includer.SourceFile);

        foreach (EntryPoint entrypoint in args.entryPoints)
        {
            input.sourceEntrypoint = entrypoint.Name;
            input.stage = StageToType(entrypoint.Stage);

            Shader shader = new Shader(input);

            shader.SetSourceFile(fileName);

            shader.SetOptions(
                ShaderOptions.AutoMapBindings |
                ShaderOptions.AutoMapLocations |
                ShaderOptions.MapUnusedUniforms |
                ShaderOptions.UseHLSLIOMapper
            );

            bool preprocessed = shader.Preprocess();

            CheckMessages(shader.GetDebugLog(), messages);
            CheckMessages(shader.GetInfoLog(), messages);

            if (!preprocessed)
                return null;

            bool parsed = shader.Parse();

            CheckMessages(shader.GetDebugLog(), messages);
            CheckMessages(shader.GetInfoLog(), messages);

            if (!parsed)
                return null;

            program.AddShader(shader);
        }

        bool linked = program.Link(MessageType.VulkanRules | MessageType.SpvRules | input.messages ?? MessageType.Default);

        CheckMessages(program.GetDebugLog(), messages);
        CheckMessages(program.GetInfoLog(), messages);

        if (!linked)
            return null;

        bool mapIO = program.MapIO();

        CheckMessages(program.GetDebugLog(), messages);
        CheckMessages(program.GetInfoLog(), messages);

        if (!mapIO)
            return null;

        for (int i = 0; i < args.entryPoints.Length; i++)
        {
            EntryPoint entryPoint = args.entryPoints[i];

            bool generatedSPIRV = program.GenerateSPIRV(out uint[] SPIRVWords, StageToType(entryPoint.Stage));

            CheckMessages(program.GetSPIRVMessages(), messages);

            if (!generatedSPIRV)
                return null;

            outputs[i].EntryPoint = "main";
            outputs[i].Stage = entryPoint.Stage;
            outputs[i].ShaderBytes = GetBytes(SPIRVWords);
        }

        return outputs;
    }


    private static byte[] GetBytes(uint[] arr)
    {
        byte[] byteArr = new byte[arr.Length * sizeof(uint)];
        Buffer.BlockCopy(arr, 0, byteArr, 0, arr.Length * sizeof(uint));
        return byteArr;
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

        if (compiledSPIRV == null)
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


    public static ComputeVariant[] GenerateComputeVariants(ShaderCreationArgs args, FileIncluder includer, List<CompilationMessage> messages)
    {
        List<KeyValuePair<string, HashSet<string>>> combinations = [.. args.combinations];
        List<ComputeVariant> variantList = [];
        List<KeyValuePair<string, string>> combination = new(combinations.Count);

        using Context ctx = new Context();

        void GenerateRecursive(int depth)
        {
            if (depth == combinations.Count) // Reached the end for this permutation, add a result.
            {
                variantList.Add(GenerateComputeVariant(ctx, args, new(combination), includer, messages));
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


    public static ComputeVariant GenerateComputeVariant(Context ctx, ShaderCreationArgs args, KeywordState state, FileIncluder includer, List<CompilationMessage> messages)
    {
        if (args.entryPoints == null || args.entryPoints.Length != 1)
            return null;

        ShaderDescription[] compiledSPIRV = Compile(args, state, includer, messages);

        if (compiledSPIRV == null)
            return null;

        ReflectedResourceInfo info = Reflect(ctx, compiledSPIRV);

        ComputeVariant variant = new ComputeVariant(state);

        variant.Uniforms = info.uniforms;
        variant.ThreadGroupSizeX = info.threadsX;
        variant.ThreadGroupSizeY = info.threadsY;
        variant.ThreadGroupSizeZ = info.threadsZ;

        variant.Direct3D11Shader = CrossCompile(ctx, GraphicsBackend.Direct3D11, compiledSPIRV)[0];
        variant.OpenGLShader = CrossCompile(ctx, GraphicsBackend.OpenGL, compiledSPIRV)[0];
        variant.OpenGLESShader = CrossCompile(ctx, GraphicsBackend.OpenGLES, compiledSPIRV)[0];
        variant.MetalShader = CrossCompile(ctx, GraphicsBackend.Metal, compiledSPIRV)[0];

        variant.VulkanShader = compiledSPIRV[0];

        return variant;
    }
}
