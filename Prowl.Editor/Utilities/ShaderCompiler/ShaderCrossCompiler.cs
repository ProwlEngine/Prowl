using Prowl.Runtime;
using Veldrid;

using System.Text;

using DirectXShaderCompiler.NET;
using SPIRVCross.NET;
using SPIRVCross.NET.GLSL;
using SPIRVCross.NET.MSL;
using SPIRVCross.NET.HLSL;

namespace Prowl.Editor.Utilities
{
    public class ShaderCrossCompiler : IVariantCompiler
    {
        public (uint, uint) model;
        public EntryPoint[] entrypoints;
        public string sourceCode;

        public ShaderCrossCompiler(string source, EntryPoint[] entrypoints, (uint, uint) model)
        {
            this.sourceCode = source;
            this.entrypoints = entrypoints;
            this.model = model;
        }

        private static byte[] GetBytes(GraphicsBackend backend, string code)
        {
            return backend switch
            {
                GraphicsBackend.Direct3D11 or 
                GraphicsBackend.OpenGL or 
                GraphicsBackend.OpenGLES 
                    => Encoding.ASCII.GetBytes(code),
                
                GraphicsBackend.Metal 
                    => Encoding.UTF8.GetBytes(code),

                _ => throw new Exception($"Invalid GraphicsBackend: {backend}"),
            };
        }

        public static SPIRVCross.NET.Compiler GetCompilationTarget(GraphicsBackend backend)
        {
            return backend switch
            {
                GraphicsBackend.Direct3D11 => CrossCompileTarget.HLSL,
                GraphicsBackend.OpenGL => CrossCompileTarget.GLSL,
                GraphicsBackend.OpenGLES => CrossCompileTarget.ESSL,

                GraphicsBackend.Metal => CrossCompileTarget.MSL,
                
                _ => throw new Exception($"Invalid GraphicsBackend: {backend}"),
            };
        }

        public ShaderVariant CompileVariant(KeywordState keywords)
        {
            using Context ctx = new Context();

            
        
        }
    }
}
