using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

namespace Prowl.Graphite.Bench;

internal static class BenchShaderLoader
{
    private static string ShaderDirectory => Path.Combine(AppContext.BaseDirectory, "Shaders");

    private static Memory<byte>? LoadFile(string path)
    {
        string full = Path.IsPathRooted(path) ? path : Path.Combine(ShaderDirectory, path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    public static string ReadSource(string moduleFile) => File.ReadAllText(Path.Combine(ShaderDirectory, moduleFile));

    public static GraphicsProgram Create(GraphicsDevice gd, string moduleFile) => CreateFromSource(gd, ReadSource(moduleFile));

    public static GraphicsProgram CreateFromSource(GraphicsDevice gd, string source)
        => Compile<GraphicsProgram>(gd, source, (compiler, pass) => [CreateGraphics(gd, compiler.Compile(pass, [], gd.BackendType))])[0];

    public static GraphicsProgram[] CreateVariants(GraphicsDevice gd, string moduleFile, int count)
        => Compile<GraphicsProgram>(gd, ReadSource(moduleFile), (compiler, pass) =>
        {
            IEnumerable<Keyword[]> combos = [[]];
            foreach (VariantSpace axis in compiler.GetAxes(pass))
                combos = combos.SelectMany(combo => axis.Values.Select(value => (Keyword[])[.. combo, new Keyword(axis.Name, value)]));

            return combos.Take(count).Select(combo => CreateGraphics(gd, compiler.Compile(pass, combo, gd.BackendType))).ToArray();
        });

    public static ComputeProgram CreateCompute(GraphicsDevice gd, string moduleFile, uint groupSizeX)
        => Compile<ComputeProgram>(gd, ReadSource(moduleFile), (compiler, pass) =>
        {
            ShaderDescription description = compiler.Compile(pass, [], gd.BackendType);
            return [gd.ResourceFactory.CreateComputeProgram(new ComputeDescription(description.Stages[0], description.ResourceLayouts, groupSizeX, 1, 1))];
        })[0];

    private static T[] Compile<T>(GraphicsDevice gd, string source, Func<SlangShaderCompiler, ShaderPass, T[]> build)
    {
        SlangShaderCompiler compiler = new();
        compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
        compiler.BeginSession([new DirectoryInfo(ShaderDirectory)], LoadFile);

        try
        {
            ShaderPass pass = new() { State = new PassState(), InlineSlang = source };
            return build(compiler, pass);
        }
        finally
        {
            compiler.EndSession();
        }
    }

    private static GraphicsProgram CreateGraphics(GraphicsDevice gd, ShaderDescription description)
    {
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        return gd.ResourceFactory.CreateGraphicsProgram(description);
    }
}
