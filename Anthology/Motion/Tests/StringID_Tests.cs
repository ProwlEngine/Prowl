namespace Prowl.Motion.Tests;

/// <summary>String ids.</summary>
public class StringID_Tests
{
    [Fact]
    public void SameString_ProducesEqualIds()
        => Assert.Equal(new StringID("hips"), new StringID("hips"));

    [Fact]
    public void DifferentStrings_AreNotEqual()
        => Assert.NotEqual(new StringID("hips"), new StringID("spine"));
}
