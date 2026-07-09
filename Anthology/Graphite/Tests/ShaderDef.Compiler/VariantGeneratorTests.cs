using Prowl.Graphite.ShaderDef;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Compiler.Tests;


// The compiler suite is intentionally minimal; the platform reflection/compilation paths are not
// complete yet. These cover the pure variant-enumeration logic, which needs no Slang session.
public class VariantGeneratorTests
{
    [Fact]
    public void GeneratesCartesianProductOfVariantSpaces()
    {
        VariantSpace[] spaces =
        [
            new VariantSpace("X", "int", ["0", "1"]),
            new VariantSpace("Y", "int", ["0", "1", "2"]),
        ];

        Keyword[][] variants = VariantCombos.Generate(spaces);

        Assert.Equal(6, variants.Length); // 2 * 3
        foreach (Keyword[] combo in variants)
            Assert.Equal(2, combo.Length);
    }


    [Fact]
    public void NoSpaces_ProducesSingleEmptyVariant()
    {
        Keyword[][] variants = VariantCombos.Generate([]);

        Assert.Single(variants);
        Assert.Empty(variants[0]);
    }
}
