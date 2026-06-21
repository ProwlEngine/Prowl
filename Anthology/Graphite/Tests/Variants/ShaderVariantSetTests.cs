using System;

using Xunit;

namespace Prowl.Graphite.Variants.Tests;


public class VariantSetTests
{
    [Fact]
    public void SelectsBaseVariantInitially()
    {
        Keyword[][] keywords =
        [
            [new Keyword("MODE", "A")],
            [new Keyword("MODE", "B")],
            [new Keyword("MODE", "C")],
        ];

        VariantSet<string> set = new(["progA", "progB", "progC"], keywords);

        Assert.Equal("progA", set.ActiveVariant);
    }


    [Fact]
    public void SetKeyword_SwitchesActiveVariant()
    {
        Keyword[][] keywords =
        [
            [new Keyword("MODE", "A")],
            [new Keyword("MODE", "B")],
            [new Keyword("MODE", "C")],
        ];

        VariantSet<string> set = new(["progA", "progB", "progC"], keywords);

        set.SetKeyword(new Keyword("MODE", "C"));
        Assert.Equal("progC", set.ActiveVariant);

        set.SetKeyword(new Keyword("MODE", "B"));
        Assert.Equal("progB", set.ActiveVariant);
    }


    [Fact]
    public void MultipleKeywords_ResolveCombinedSelection()
    {
        // Slot order is defined by the base set (keywords[0]): X -> slot 0, Y -> slot 1.
        Keyword[][] keywords =
        [
            [new Keyword("X", "0"), new Keyword("Y", "0")], // v00
            [new Keyword("X", "0"), new Keyword("Y", "1")], // v01
            [new Keyword("X", "1"), new Keyword("Y", "0")], // v10
            [new Keyword("X", "1"), new Keyword("Y", "1")], // v11
        ];

        VariantSet<string> set = new(["v00", "v01", "v10", "v11"], keywords);

        Assert.Equal("v00", set.ActiveVariant);

        set.SetKeyword(new Keyword("X", "1"));
        Assert.Equal("v10", set.ActiveVariant);

        set.SetKeyword(new Keyword("Y", "1"));
        Assert.Equal("v11", set.ActiveVariant);

        set.SetKeywords(new Keyword("X", "0"), new Keyword("Y", "0"));
        Assert.Equal("v00", set.ActiveVariant);
    }


    [Fact]
    public void MoreVariantsThanKeywords_Throws()
    {
        Keyword[][] keywords = [[new Keyword("MODE", "A")]];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VariantSet<string>(["a", "b"], keywords));
    }
}
