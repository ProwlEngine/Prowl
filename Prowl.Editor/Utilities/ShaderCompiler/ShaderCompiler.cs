// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

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
    public bool isSourceFile;
    public string? filename;
    public int line;
    public int column;
}


public struct CompilationMessage
{
    public CompilationFile? file;

    public LogSeverity severity;
    public string message;


    public CompilationMessage()
    {
        severity = LogSeverity.Normal;
        message = "";
        file = null;
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


    private static (string, string) SplitFirst(string text, char delim)
    {
        int ind = text.IndexOf(delim);

        if (ind < 0)
            return (text, "");

        return (text.Substring(0, ind), text.Substring(Math.Min(text.Length, ind + 1)));
    }


    private static IEnumerable<int> IndicesOf(string text, char delim)
    {
        for (int i = text.IndexOf(delim); i > -1; i = text.IndexOf(delim, i + 1))
            yield return i;
    }


    private static CompilationMessage? ParseMessage(string messageText, int sourceOffset, FileIncluder includer)
    {
        if (string.IsNullOrWhiteSpace(messageText))
            return null;

        (string severityText, string messageNext) = SplitFirst(messageText, ':');

        LogSeverity severity = severityText switch
        {
            "WARNING" => LogSeverity.Warning,
            "ERROR" => LogSeverity.Error,
            _ => LogSeverity.Normal
        };

        bool hasFile = false;
        CompilationFile file = new();

        int[] indices = IndicesOf(messageNext, ':').ToArray();

        for (int i = 0; i < indices.Length; i++)
        {
            string substr = messageNext.Substring(0, indices[i]).TrimStart();

            if (substr.Length == 1 && substr[0] == '0')
            {
                file.isSourceFile = true;
                file.filename = includer.SourceFilePath;
            }
            else if (includer.GetFullFilePath(substr, out string? fullPath))
            {
                file.isSourceFile = false;
                file.filename = fullPath;
            }
            else
            {
                continue;
            }

            string lnText = messageNext.Substring(indices[i] + 1, (indices[i + 1] - indices[i]) - 1);
            string colText = messageNext.Substring(indices[i + 1] + 1, (indices[i + 2] - indices[i + 1]) - 1);

            messageNext = messageNext.Substring(indices[i + 2] + 1);

            if (!int.TryParse(lnText, out file.line))
                continue;

            if (!int.TryParse(colText, out file.column))
                continue;

            if (file.isSourceFile)
                file.line -= sourceOffset;

            hasFile = true;
            break;
        }

        return new CompilationMessage()
        {
            file = hasFile ? file : null,
            severity = severity,
            message = messageNext.TrimStart(),
        };
    }



    private static void CheckMessages(string messageText, int sourceOffset, FileIncluder includer, List<CompilationMessage> messages)
    {
        // ERROR: File:Line:Column: Error text: Additional message.
        // ERROR: Error text: Additional message.

        if (string.IsNullOrWhiteSpace(messageText))
            return;

        string[] messagesSplit = messageText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string messageLine in messagesSplit)
        {
            CompilationMessage? message = ParseMessage(messageLine, sourceOffset, includer);

            if (message != null)
                messages.Add(message.Value);
        }
    }


    private static string AddDefines(string sourceCode, KeywordState keywords)
    {
        StringBuilder builder = new StringBuilder();

        foreach (var pair in keywords.KeyValuePairs)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            builder.Append("#define ");
            builder.Append(pair.Key);
            builder.Append(" ");
            builder.AppendLine(pair.Value);
        }

        builder.Append(sourceCode);

        return builder.ToString();
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
            code = AddDefines(args.sourceCode, keywords),
            hlslFunctionality1 = true,
            defaultProfile = ShaderProfile.None,
            forceDefaultVersionAndProfile = false,
            forwardCompatible = false,
            messages = MessageType.Enhanced | MessageType.ReadHlsl | MessageType.DisplayErrorColumn | MessageType.CascadingErrors,
        };

        string fileName = Path.GetFileName(includer.SourceFile);

        foreach (EntryPoint entrypoint in args.entryPoints)
        {
            input.sourceEntrypoint = entrypoint.Name;
            input.stage = StageToType(entrypoint.Stage);
            input.fileIncluder = includer.GetIncluder(messages);

            Shader shader = new Shader(input);

            shader.SetOptions(
                ShaderOptions.AutoMapBindings |
                ShaderOptions.AutoMapLocations |
                ShaderOptions.MapUnusedUniforms |
                ShaderOptions.UseHLSLIOMapper
            );

            bool preprocessed = shader.Preprocess();
            bool parsed = shader.Parse();

            CheckMessages(shader.GetDebugLog(), keywords.Count, includer, messages);
            CheckMessages(shader.GetInfoLog(), keywords.Count, includer, messages);

            if (!preprocessed || !parsed)
                return null;

            program.AddShader(shader);
        }

        bool linked = program.Link(MessageType.VulkanRules | MessageType.SpvRules | input.messages ?? MessageType.Default);
        bool mapIO = program.MapIO();

        CheckMessages(program.GetDebugLog(), keywords.Count, includer, messages);
        CheckMessages(program.GetInfoLog(), keywords.Count, includer, messages);

        if (!linked || !mapIO)
            return null;

        for (int i = 0; i < args.entryPoints.Length; i++)
        {
            EntryPoint entryPoint = args.entryPoints[i];

            bool generatedSPIRV = program.GenerateSPIRV(out uint[] SPIRVWords, StageToType(entryPoint.Stage));

            CheckMessages(program.GetSPIRVMessages(), keywords.Count, includer, messages);

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
