using System.Text.RegularExpressions;

using Prowl.Graphite.ShaderDef;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

public sealed class MobileSupportTests
{
    // An Adreno 740 as the Quest 3 reports it: fragment inputs reach location 27, update after bind limits far past the tables
    private static GraphicsDeviceLimits Adreno => new()
    {
        MaxTextureDimension2D = 16384,
        MaxVertexOutputComponents = 128,
        MaxFragmentInputComponents = 112,
        MaxBoundDescriptorSets = 7,
        PerStage = new DescriptorCounts(16777216, 16777216, uint.MaxValue, 16777216, 16777216, 16777216, 50331648),
        PerStageWithTables = new DescriptorCounts(16777216, 16777216, uint.MaxValue, 16777216, 16777216, 16777216, 50331648),
    };

    [Fact]
    public void RunsWithinAdrenoLimits()
    {
        Assert.Null(Renderer.Unsupported(drawIndirectBaseInstance: true, descriptorIndexing: true, Adreno));

        GraphicsDeviceLimits minimums = new()
        {
            MaxVertexOutputComponents = 112,
            MaxFragmentInputComponents = 112,
            PerStage = new DescriptorCounts(16, 12, uint.MaxValue, 4, 16, 4, 128),
            PerStageWithTables = new DescriptorCounts(500000, 12, uint.MaxValue, 500000, 500000, 500000, 500000),
            PerProgram = new DescriptorCounts(96, 72, 8, 24, 96, 24, uint.MaxValue),
            PerProgramWithTables = new DescriptorCounts(500000, 72, 8, 500000, 500000, 500000, uint.MaxValue),
        };

        Assert.Null(Renderer.Unsupported(true, true, minimums));
    }

    [Fact]
    public void LibraryVaryingsStayWithinTheReservedLocations()
    {
        InternalShaders.Register();
        int checkedFiles = 0;
        foreach (string name in ShaderLibrary.Names)
        {
            if (ShaderLibrary.Get(name) is not { } source)
                continue;
            checkedFiles++;
            foreach (Match match in Regex.Matches(source, @"location\s*=\s*(\d+)"))
                Assert.True(uint.Parse(match.Groups[1].Value) <= GraphiteBindings.MaterialSlotLocation, $"{name} uses location {match.Groups[1].Value}, past the {GraphiteBindings.MaterialSlotLocation} a 112 component fragment stage can read.");
        }
        Assert.True(checkedFiles > 0);
    }

    [Fact]
    public void LibraryTablesMatchTheRendererTables()
    {
        InternalShaders.Register();
        string textures = ShaderLibrary.Get($"{ShaderLibrary.Prefix}Textures.glsl")!;

        Assert.Contains($"Graphite_Textures2D[{RendererTextures.Textures2D}]", textures);
        Assert.Contains($"Graphite_ShadowTextures2D[{RendererTextures.Textures2D}]", textures);
        Assert.Contains($"Graphite_TexturesCube[{RendererTextures.OtherTextures}]", textures);
        Assert.Contains($"Graphite_Samplers[{RendererTextures.Samplers}]", textures);
    }
}
