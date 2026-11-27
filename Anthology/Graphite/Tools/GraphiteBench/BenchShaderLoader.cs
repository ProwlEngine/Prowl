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

    private static readonly GlslShaderCompiler s_compiler = new(new GlslCompilerOptions
    {
        ResolveInclude = (name, _) =>
        {
            string path = Path.Combine(ShaderDirectory, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        },
    });

    public static string ReadSource(string file) => File.ReadAllText(Path.Combine(ShaderDirectory, file));

    public static GraphicsProgram Create(GraphicsDevice gd, string file) => CreateFromSource(gd, ReadSource(file));

    public static GraphicsProgram CreateFromSource(GraphicsDevice gd, string source)
        => CreateGraphics(gd, s_compiler.Compile(Pass(source), [], gd.BackendType));

    public static GraphicsProgram[] CreateVariants(GraphicsDevice gd, string file, int count)
    {
        ShaderPass pass = Pass(ReadSource(file));
        IEnumerable<Keyword[]> combos = [[]];
        foreach (VariantSpace axis in s_compiler.GetAxes(pass))
            combos = combos.SelectMany(combo => axis.Values.Select(value => (Keyword[])[.. combo, new Keyword(axis.Name, value)]));

        return combos.Take(count).Select(combo => CreateGraphics(gd, s_compiler.Compile(pass, combo, gd.BackendType))).ToArray();
    }

    public static ComputeProgram CreateCompute(GraphicsDevice gd, string file)
        => gd.ResourceFactory.CreateComputeProgram(s_compiler.CompileProgram(Pass(ReadSource(file)), []).ToCompute());

    private static ShaderPass Pass(string source) => new() { State = new PassState(), Source = source };

    private static GraphicsProgram CreateGraphics(GraphicsDevice gd, ShaderDescription description)
    {
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        return gd.ResourceFactory.CreateGraphicsProgram(description);
    }
}
