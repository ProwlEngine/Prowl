using System.Text;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

namespace DebuggerViewer.Rendering;

internal static class ShaderFiles
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Shaders");

    private static readonly GlslShaderCompiler s_compiler = new(new GlslCompilerOptions
    {
        ResolveInclude = (name, _) =>
        {
            string path = Path.Combine(Directory, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        },
    });

    public static ShaderPass[] Load(GraphicsDevice device, params string[] files)
    {
        ShaderPass[] passes = new ShaderPass[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            ShaderDefinition definition = ShaderParser.Parse(File.ReadAllText(Path.Combine(Directory, files[i]), Encoding.UTF8));
            definition.Create(device, s_compiler);
            passes[i] = definition.Passes![0];
        }

        return passes;
    }

    public static GraphicsProgram Interleaved(GraphicsDevice device, ShaderPass pass, string name, VertexLayoutDescription layout,
        BlendStateDescription blend, DepthStencilStateDescription depth, RasterizerStateDescription raster)
    {
        if (!pass.GetVariant().TryGetDescription(device.BackendType, out ShaderDescription description))
            throw new InvalidOperationException($"{name} was not compiled for backend {device.BackendType}.");

        description.VertexLayouts = [layout];
        description.BlendState = pass.State.ToBlendState(blend);
        description.DepthStencilState = pass.State.ToDepthStencilState(depth);
        description.RasterizerState = pass.State.ToRasterizerState(raster);

        GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);
        program.Name = name;
        return program;
    }
}
