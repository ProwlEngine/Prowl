using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Graphite.ShaderDef;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


// One Slang session is reused for every pass a compiler instance sees, so a naive axis scan reports
// every axis loaded during that session's lifetime. These compile several passes through a single
// session and assert each one only sees the axes its own transitive imports declare.
public class ImportScopedVariantTests
{
    static VariantSpace[][] AxesFor(params string[] moduleNames)
        => SlangThread.Run(() =>
        {
            SlangShaderCompiler compiler = new();
            compiler.RegisterModule(new VulkanCompiler());
            compiler.BeginSession([new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "Shaders")), new DirectoryInfo(AppContext.BaseDirectory)]);

            VariantSpace[][] axes = new VariantSpace[moduleNames.Length][];

            for (int i = 0; i < moduleNames.Length; i++)
            {
                ShaderPass pass = new()
                {
                    Name = moduleNames[i],
                    State = new PassState(),
                    InlineSlang = CompilerTestHarness.ShaderSource(moduleNames[i])
                };

                axes[i] = [.. compiler.GetAxes(pass)];
            }

            compiler.EndSession();
            return axes;
        });


    [Fact]
    public void PassImportingAxisModule_EnumeratesItsAxes()
    {
        VariantSpace[] axes = AxesFor("ScopedAxisImporter")[0];

        Assert.Equal(2, axes.Length);
        Assert.Contains(axes, a => a.Name == "SCOPED_FLAG" && !a.IsEnum);
        Assert.Contains(axes, a => a.Name == "SCOPED_MODE" && a.IsEnum);
    }


    [Fact]
    public void PassWithoutTheImport_EnumeratesNoAxes()
    {
        VariantSpace[][] axes = AxesFor("ScopedAxisImporter", "ScopedAxisOutsider");

        Assert.Equal(2, axes[0].Length);
        Assert.Empty(axes[1]);
    }


    [Fact]
    public void AxesDoNotLeakBackwards_WhenTheImporterCompilesSecond()
    {
        VariantSpace[][] axes = AxesFor("ScopedAxisOutsider", "ScopedAxisImporter");

        Assert.Empty(axes[0]);
        Assert.Equal(2, axes[1].Length);
    }


    [Fact]
    public void UnrelatedShadersInOneSession_KeepTheirOwnAxes()
    {
        VariantSpace[][] axes = AxesFor("ScopedAxisImporter", "Variants", "EnumVariants", "ScopedAxisOutsider");

        Assert.Equal(["SCOPED_FLAG", "SCOPED_MODE"], axes[0].Select(a => a.Name).Order());
        Assert.Equal(["DoubleColor"], axes[1].Select(a => a.Name));
        Assert.Equal(["LightingMode", "Shadows"], axes[2].Select(a => a.Name).Order());
        Assert.Empty(axes[3]);
    }


    [Fact]
    public void ScopedImporter_CompilesEveryPermutation()
    {
        ShaderPass pass = new()
        {
            Name = "ScopedAxisImporter",
            State = new PassState(),
            InlineSlang = CompilerTestHarness.ShaderSource("ScopedAxisImporter")
        };

        CompilationResult result = CompilerTestHarness.CompilePassAll(pass, () => new VulkanCompiler());

        Assert.Equal(6, result.CompiledVariants.Length);

        foreach (VariantResult variant in result.CompiledVariants)
            Assert.NotEmpty(CompilerTestHarness.StageOf(variant.Backends.Single().Description, ShaderStages.Fragment).ShaderBytes);
    }
}
