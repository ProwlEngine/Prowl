using Prowl.Editor.ShaderParser;
using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.Text.RegularExpressions;
using Veldrid;
using Veldrid.SPIRV;
using System.Reflection;

using static System.Text.Encoding;

namespace Prowl.Editor.Assets
{
    public class SPIRVCompiler
    {
        private static bool HasSpirvHeader(byte[] bytes)
        {
            return bytes.Length > 4
                && bytes[0] == 0x03
                && bytes[1] == 0x02
                && bytes[2] == 0x23
                && bytes[3] == 0x07;
        }

        private unsafe static byte[] EnsureSpirv(ShaderDescription description, string glslSrc)
        {
            if (HasSpirvHeader(description.ShaderBytes))
            {
                return description.ShaderBytes;
            }

            fixed (byte* sourceTextPtr = description.ShaderBytes)
            {
                GlslCompileOptions options = new()
                {
                    Debug = description.Debug
                };

                return SpirvCompilation.CompileGlslToSpirv(glslSrc, null, description.Stage, options).SpirvBytes;
            }
        }

        private static byte[] GetBytes(GraphicsBackend backend, string code)
        {
            switch (backend)
            {
                case GraphicsBackend.Direct3D11:
                case GraphicsBackend.OpenGL:
                case GraphicsBackend.OpenGLES:
                    return ASCII.GetBytes(code);
                case GraphicsBackend.Metal:
                    return UTF8.GetBytes(code);
                default:
                    throw new SpirvCompilationException($"Invalid GraphicsBackend: {backend}");
            }
        }

        public static CrossCompileTarget GetCompilationTarget(GraphicsBackend backend)
        {
            return backend switch
            {
                GraphicsBackend.Direct3D11 => CrossCompileTarget.HLSL,
                GraphicsBackend.OpenGL => CrossCompileTarget.GLSL,
                GraphicsBackend.Metal => CrossCompileTarget.MSL,
                GraphicsBackend.OpenGLES => CrossCompileTarget.ESSL,
                _ => throw new SpirvCompilationException($"Invalid GraphicsBackend: {backend}"),
            };
        }

        public static ShaderDescription[] CreateFromSpirv(ShaderDescription vertDesc, string vertSrc, ShaderDescription fragDesc, string fragSrc, CrossCompileOptions options, GraphicsBackend backendType)
        {
            if (backendType == GraphicsBackend.Vulkan)
            {
                vertDesc.ShaderBytes = EnsureSpirv(vertDesc, vertSrc);
                fragDesc.ShaderBytes = EnsureSpirv(fragDesc, fragSrc);

                return [
                    vertDesc,
                    fragDesc
                ];
            }

            CrossCompileTarget compilationTarget = GetCompilationTarget(backendType);
                
            VertexFragmentCompilationResult vertexFragmentCompilationResult = SpirvCompilation.CompileVertexFragment(
                vertDesc.ShaderBytes, 
                fragDesc.ShaderBytes, 
                compilationTarget, 
                options
            );
                
            string entryPoint = (backendType == GraphicsBackend.Metal && vertDesc.EntryPoint == "main") ? "main0" : vertDesc.EntryPoint;
            byte[] bytes = GetBytes(backendType, vertexFragmentCompilationResult.VertexShader);

            vertDesc.ShaderBytes = bytes;
            vertDesc.EntryPoint = entryPoint;

            string entryPoint2 = (backendType == GraphicsBackend.Metal && fragDesc.EntryPoint == "main") ? "main0" : fragDesc.EntryPoint;
            byte[] bytes2 = GetBytes(backendType, vertexFragmentCompilationResult.FragmentShader);
                
            fragDesc.ShaderBytes = bytes2;
            fragDesc.EntryPoint = entryPoint2;
                
            return [
                vertDesc,
                fragDesc
            ];
        }
    }
}
