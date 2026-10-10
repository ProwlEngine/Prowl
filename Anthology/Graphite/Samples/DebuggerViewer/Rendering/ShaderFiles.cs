using System.Text;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

namespace DebuggerViewer.Rendering;

internal static class ShaderFiles
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Shaders");

    public static ShaderPass[] Load(GraphicsDevice device, params string[] files)
    {
        SlangShaderCompiler compiler = new();
        compiler.RegisterModule(device.BackendType switch
        {
            GraphicsBackend.Vulkan => new VulkanCompiler("spirv_1_4"),
            _ => throw new NotSupportedException($"Unsupported graphics backend: {device.BackendType}"),
        });

        compiler.BeginSession([new DirectoryInfo(Directory)], Read);
        ShaderPass[] passes = new ShaderPass[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            ShaderDefinition definition = ShaderParser.Parse(File.ReadAllText(Path.Combine(Directory, files[i]), Encoding.UTF8));
            definition.Create(device, compiler, new Variant(), CompileMode.All);
            passes[i] = definition.Passes![0];
        }

        compiler.EndSession();
        return passes;
    }

    public static GraphicsProgram Interleaved(GraphicsDevice device, ShaderPass pass, string name, VertexLayoutDescription layout,
        BlendStateDescription blend, DepthStencilStateDescription depth, RasterizerStateDescription raster)
    {
        if (!pass.GetVariant(0).TryGetDescription(device.BackendType, out ShaderDescription description))
            throw new InvalidOperationException($"{name} was not compiled for backend {device.BackendType}.");

        description.VertexLayouts = [layout];
        description.BlendState = pass.State.ToBlendState(blend);
        description.DepthStencilState = pass.State.ToDepthStencilState(depth);
        description.RasterizerState = pass.State.ToRasterizerState(raster);

        GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);
        program.Name = name;
        return program;
    }

    private static Memory<byte>? Read(string path)
    {
        string full = Path.IsPathRooted(path) ? path : Path.Combine(Directory, path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }
}
