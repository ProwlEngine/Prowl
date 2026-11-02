using System.Text;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;


namespace Prowl.Graphite.Samples;


public static class ShaderLoader
{
    public static GlslShaderCompiler Compiler { get; } = new(new GlslCompilerOptions
    {
        ResolveInclude = (name, _) => FileLoader.Load(name) is { } bytes ? Encoding.UTF8.GetString(bytes.Span) : null,
        CacheDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GraphiteSamples", "Shaders"),
    });


    public static GraphicsProgram CreateShader(GraphicsDevice device)
    {
        string source = Encoding.UTF8.GetString(FileLoader.Load("Shader.glsl")!.Value.Span);
        ShaderPass pass = new() { State = new PassState(), Source = source, SourceName = "Shader.glsl" };
        ShaderDescription description = Compiler.Compile(pass, [], device.BackendType);

        // Reflection fills in the stages, vertex inputs, and resource bindings. The loader owns the
        // fixed-function pipeline state the shader source does not describe.
        description.BlendState = BlendStateDescription.SingleDisabled;
        description.DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual;
        description.RasterizerState = new(FaceCullMode.Back, FrontFace.Clockwise, true, false);

        return device.ResourceFactory.CreateGraphicsProgram(description);
    }
}
