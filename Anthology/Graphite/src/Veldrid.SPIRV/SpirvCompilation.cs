using System;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Shaderc;

namespace Veldrid.SPIRV
{
    /// <summary>
    /// Static functions for cross-compiling SPIR-V bytecode to various shader languages, and for compiling GLSL to SPIR-V.
    /// </summary>
    public static unsafe class SpirvCompilation
    {
        private static readonly Shaderc s_shaderc = Shaderc.GetApi();

        /// <summary>
        /// Cross-compiles the given vertex-fragment pair into some target language.
        /// </summary>
        /// <param name="vsBytes">The vertex shader's SPIR-V bytecode or ASCII-encoded GLSL source code.</param>
        /// <param name="fsBytes">The fragment shader's SPIR-V bytecode or ASCII-encoded GLSL source code.</param>
        /// <param name="target">The target language.</param>
        /// <returns>A <see cref="VertexFragmentCompilationResult"/> containing the compiled output.</returns>
        public static VertexFragmentCompilationResult CompileVertexFragment(
            byte[] vsBytes,
            byte[] fsBytes,
            CrossCompileTarget target) => CompileVertexFragment(vsBytes, fsBytes, target, new CrossCompileOptions());

        /// <summary>
        /// Cross-compiles the given vertex-fragment pair into some target language.
        /// </summary>
        /// <param name="vsBytes">The vertex shader's SPIR-V bytecode or ASCII-encoded GLSL source code.</param>
        /// <param name="fsBytes">The fragment shader's SPIR-V bytecode or ASCII-encoded GLSL source code.</param>
        /// <param name="target">The target language.</param>
        /// <param name="options">The options for shader translation.</param>
        /// <returns>A <see cref="VertexFragmentCompilationResult"/> containing the compiled output.</returns>
        public static VertexFragmentCompilationResult CompileVertexFragment(
            byte[] vsBytes,
            byte[] fsBytes,
            CrossCompileTarget target,
            CrossCompileOptions options)
        {
            byte[] vsSpirvBytes = EnsureSpirvBytes(vsBytes, ShaderStages.Vertex, target);
            byte[] fsSpirvBytes = EnsureSpirvBytes(fsBytes, ShaderStages.Fragment, target);
            return SpirvCrossCompiler.CompileVertexFragment(vsSpirvBytes, fsSpirvBytes, target, options);
        }

        /// <summary>
        /// Cross-compiles the given compute shader into some target language.
        /// </summary>
        /// <param name="csBytes">The compute shader's SPIR-V bytecode or ASCII-encoded GLSL source code.</param>
        /// <param name="target">The target language.</param>
        /// <returns>A <see cref="ComputeCompilationResult"/> containing the compiled output.</returns>
        public static ComputeCompilationResult CompileCompute(
            byte[] csBytes,
            CrossCompileTarget target) => CompileCompute(csBytes, target, new CrossCompileOptions());

        /// <summary>
        /// Cross-compiles the given compute shader into some target language.
        /// </summary>
        /// <param name="csBytes">The compute shader's SPIR-V bytecode or ASCII-encoded GLSL source code.</param>
        /// <param name="target">The target language.</param>
        /// <param name="options">The options for shader translation.</param>
        /// <returns>A <see cref="ComputeCompilationResult"/> containing the compiled output.</returns>
        public static ComputeCompilationResult CompileCompute(
            byte[] csBytes,
            CrossCompileTarget target,
            CrossCompileOptions options)
        {
            byte[] csSpirvBytes = EnsureSpirvBytes(csBytes, ShaderStages.Compute, target);
            return SpirvCrossCompiler.CompileCompute(csSpirvBytes, target, options);
        }

        /// <summary>
        /// Compiles the given GLSL source code into SPIR-V.
        /// </summary>
        /// <param name="sourceText">The shader source code.</param>
        /// <param name="fileName">A descriptive name for the shader. May be null.</param>
        /// <param name="stage">The <see cref="ShaderStages"/> which the shader is used in.</param>
        /// <param name="options">Parameters for the GLSL compiler.</param>
        /// <returns>A <see cref="SpirvCompilationResult"/> containing the compiled SPIR-V bytecode.</returns>
        public static SpirvCompilationResult CompileGlslToSpirv(
            string sourceText,
            string fileName,
            ShaderStages stage,
            GlslCompileOptions options)
        {
            var shaderc = s_shaderc;
            Silk.NET.Shaderc.Compiler* compiler = null;
            CompileOptions* compileOptions = null;
            Silk.NET.Shaderc.CompilationResult* result = null;
            try
            {
                compiler = shaderc.CompilerInitialize();
                if (compiler == null)
                    throw new SpirvCompilationException("Failed to initialize shaderc compiler.");

                compileOptions = shaderc.CompileOptionsInitialize();
                if (compileOptions == null)
                    throw new SpirvCompilationException("Failed to initialize compile options.");

                if (options.Debug)
                {
                    shaderc.CompileOptionsSetGenerateDebugInfo(compileOptions);
                }
                else
                {
                    shaderc.CompileOptionsSetOptimizationLevel(compileOptions, OptimizationLevel.Performance);
                }

                foreach (var macro in options.Macros)
                {
                    byte[] nameBytes = Encoding.ASCII.GetBytes(macro.Name);
                    if (string.IsNullOrEmpty(macro.Value))
                    {
                        fixed (byte* namePtr = nameBytes)
                        {
                            shaderc.CompileOptionsAddMacroDefinition(compileOptions,
                                namePtr, (nuint)nameBytes.Length,
                                (byte*)null, 0);
                        }
                    }
                    else
                    {
                        byte[] valueBytes = Encoding.ASCII.GetBytes(macro.Value);
                        fixed (byte* namePtr = nameBytes)
                        fixed (byte* valuePtr = valueBytes)
                        {
                            shaderc.CompileOptionsAddMacroDefinition(compileOptions,
                                namePtr, (nuint)nameBytes.Length,
                                valuePtr, (nuint)valueBytes.Length);
                        }
                    }
                }

                ShaderKind shaderKind = GetShadercKind(stage);
                byte[] sourceBytes = Encoding.ASCII.GetBytes(sourceText);
                if (string.IsNullOrEmpty(fileName)) fileName = "<veldrid-spirv-input>";
                byte[] fileNameBytes = Encoding.ASCII.GetBytes(fileName + '\0');
                byte[] entryPointBytes = "main\0"u8.ToArray();

                fixed (byte* sourcePtr = sourceBytes)
                fixed (byte* fileNamePtr = fileNameBytes)
                fixed (byte* entryPtr = entryPointBytes)
                {
                    result = shaderc.CompileIntoSpv(compiler,
                        sourcePtr, (nuint)sourceBytes.Length,
                        shaderKind,
                        fileNamePtr,
                        entryPtr,
                        compileOptions);
                }

                if (result == null)
                    throw new SpirvCompilationException("Shaderc returned null result.");

                var status = shaderc.ResultGetCompilationStatus(result);
                if (status != CompilationStatus.Success)
                {
                    byte* errorMsgPtr = shaderc.ResultGetErrorMessage(result);
                    string errorMsg = errorMsgPtr != null
                        ? Marshal.PtrToStringUTF8((nint)errorMsgPtr)
                        : "Unknown error";
                    throw new SpirvCompilationException("GLSL compilation failed: " + errorMsg);
                }

                byte* bytesPtr = shaderc.ResultGetBytes(result);
                nuint length = shaderc.ResultGetLength(result);
                byte[] spirvBytes = new byte[(int)length];
                new Span<byte>(bytesPtr, (int)length).CopyTo(spirvBytes);

                return new SpirvCompilationResult(spirvBytes);
            }
            finally
            {
                if (result != null) shaderc.ResultRelease(result);
                if (compileOptions != null) shaderc.CompileOptionsRelease(compileOptions);
                if (compiler != null) shaderc.CompilerRelease(compiler);
            }
        }

        private static byte[] EnsureSpirvBytes(byte[] bytes, ShaderStages stage, CrossCompileTarget target)
        {
            if (HasSpirvHeader(bytes))
            {
                return bytes;
            }

            string sourceText = Encoding.ASCII.GetString(bytes);
            bool debug = target == CrossCompileTarget.GLSL || target == CrossCompileTarget.ESSL;
            SpirvCompilationResult result = CompileGlslToSpirv(sourceText, string.Empty, stage, new GlslCompileOptions(debug));
            return result.SpirvBytes;
        }

        internal static bool HasSpirvHeader(byte[] bytes)
        {
            return bytes.Length > 4
                && bytes[0] == 0x03
                && bytes[1] == 0x02
                && bytes[2] == 0x23
                && bytes[3] == 0x07;
        }

        private static ShaderKind GetShadercKind(ShaderStages stage)
        {
            return stage switch
            {
                ShaderStages.Vertex => ShaderKind.VertexShader,
                ShaderStages.Fragment => ShaderKind.FragmentShader,
                ShaderStages.Compute => ShaderKind.ComputeShader,
                ShaderStages.Geometry => ShaderKind.GeometryShader,
                ShaderStages.TessellationControl => ShaderKind.TessControlShader,
                ShaderStages.TessellationEvaluation => ShaderKind.TessEvaluationShader,
                _ => throw new SpirvCompilationException($"Invalid shader stage: {stage}")
            };
        }
    }
}
