// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Rendering;

using Veldrid;

using Debug = Prowl.Runtime.Debug;

namespace Prowl.Editor;


public static class ComputeParser
{
    public static bool ParseShader(string name, string input, FileIncluder includer, out ComputeShader? shader)
    {
        shader = null;

        if (!ParseKernels(input, includer, out string[] kernelNames, out (int, int) shaderModel))
            return false;

        ComputeKernel[] kernels = new ComputeKernel[kernelNames.Length];

        ShaderCreationArgs args;
        args.sourceCode = input;
        args.shaderModel = shaderModel;
        args.entryPoints = [new EntryPoint(ShaderStages.Compute, "")];

        using SPIRVCross.NET.Context context = new();

        for (int i = 0; i < kernelNames.Length; i++)
        {
            string kernelName = kernelNames[i];
            args.entryPoints[0].Name = kernelName;

            Debug.Log("Compiling kernel " + kernelName);

            Dictionary<string, HashSet<string>> keywords = [];
            List<KeywordState> permutations = VariantCompiler.GeneratePermutations([.. keywords]);

            List<CompilationMessage> messages = [];

            ComputeVariant[] variants = new ComputeVariant[permutations.Count];

            bool compiled = true;
            for (int j = 0; j < permutations.Count; j++)
            {
                ComputeVariant? variant = VariantCompiler.CompileComputeVariant(context, args, permutations[j], includer, messages);

                if (variant == null)
                    compiled = false;
                else
                    variants[j] = variant!;
            }

            LogCompilationMessages(kernelName, messages, includer);

            if (!compiled)
                return false;

            kernels[i] = new ComputeKernel(kernelName, keywords, variants);
        }

        shader = new ComputeShader(name, kernels);

        return true;
    }


    private static void LogCompilationError(string message, FileIncluder includer, int line, int column)
    {
        DebugStackFrame frame = new(includer.SourceFilePath, line, column);
        DebugStackTrace trace = new(frame);

        Debug.Log("Error compiling shader: " + message, LogSeverity.Error, trace);
    }


    private static void LogCompilationMessages(string shaderName, List<CompilationMessage> messages, FileIncluder includer)
    {
        foreach (CompilationMessage message in messages)
        {
            DebugStackTrace? trace = null;

            if (message.file != null)
            {
                trace = new(
                    new DebugStackFrame(
                        message.file?.filename ?? "<Unknown File>",
                        message.file?.line,
                        message.file?.column
                    )
                );
            }

            string prefix = message.severity switch
            {
                LogSeverity.Normal => "Info",
                LogSeverity.Warning => "Warning",
                _ => "Error",
            };

            prefix += $" compiling {shaderName}: ";

            Debug.Log(prefix + message.message, message.severity, trace);
        }
    }


    private static bool ParseKernels(string program, FileIncluder includer, out string[] kernels, out (int, int) shaderModel)
    {
        List<string> kernelList = new();
        kernels = [];
        shaderModel = (6, 0);

        using StringReader sr = new(program);

        string? line;
        bool hasModel = false;
        int lineNumber = 0;
        while ((line = sr.ReadLine()) != null)
        {
            lineNumber++;
            string[] linesSplit = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (linesSplit.Length < 1)
                continue;

            if (linesSplit[0] != "#pragma")
                continue;

            try
            {
                switch (linesSplit[1])
                {
                    case "kernel":
                        kernelList.Add(linesSplit[2]);
                        break;

                    case "target":
                        if (hasModel)
                            throw new ParseException("target", "duplicate shader model targets defined.");

                        try
                        {
                            int major = (int)char.GetNumericValue(linesSplit[2][0]);

                            if (linesSplit[2][1] != '.')
                                throw new Exception();

                            int minor = (int)char.GetNumericValue(linesSplit[2][2]);

                            if (major < 0 || minor < 0)
                                throw new Exception();

                            shaderModel = (major, minor);
                            hasModel = true;
                        }
                        catch
                        {
                            throw new ParseException("shader model", $"invalid shader model: {linesSplit[2]}");
                        }
                        break;
                }
            }
            catch (ParseException ex)
            {
                LogCompilationError(ex.Message, includer, lineNumber, line.IndexOf("#pragma") + 7);
                return false;
            }
        }

        kernels = kernelList.ToArray();

        return true;
    }
}
