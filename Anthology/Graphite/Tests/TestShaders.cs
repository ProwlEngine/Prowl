using System;
using System.IO;

// using Prowl.Veldrid.SPIRV;

namespace Prowl.Veldrid.Tests;

internal static class TestShaders
{
    public static ShaderProgram[] LoadVertexFragment(ResourceFactory factory, string setName)
    {
        return null; // factory.CreateFromSpirv(
                     // new ShaderDescription(ShaderStages.Vertex, File.ReadAllBytes(GetPath(setName, ShaderStages.Vertex)), "main"),
                     // new ShaderDescription(ShaderStages.Fragment, File.ReadAllBytes(GetPath(setName, ShaderStages.Fragment)), "main"),
                     // new CrossCompileOptions(false, false));
    }

    public static ShaderProgram LoadCompute(ResourceFactory factory, string setName)
    {
        return null;//factory.CreateFromSpirv(
                    // new ShaderDescription(ShaderStages.Compute, File.ReadAllBytes(GetPath(setName, ShaderStages.Compute)), "main"),
                    // new CrossCompileOptions(false, false));
    }

    public static string GetPath(string setName, ShaderStages stage)
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "Shaders",
            $"{setName}.{stage.ToString().ToLowerInvariant().Substring(0, 4)}");
    }
}
