namespace Prowl.Motion.Tests;

public class StringIDTests
{
    [Fact]
    public void SameString_ProducesEqualIds()
        => Assert.Equal(new StringID("hips"), new StringID("hips"));

    [Fact]
    public void DifferentStrings_AreNotEqual()
        => Assert.NotEqual(new StringID("hips"), new StringID("spine"));

    [Fact]
    public void Invalid_IsNotValid()
        => Assert.False(StringID.Invalid.IsValid);

    [Fact]
    public void RetainsDebugName()
        => Assert.Equal("hips", new StringID("hips").DebugName);
}

public class FrameTimeTests
{
    [Fact]
    public void FromFloat_SplitsIntoFrameAndPercentage()
    {
        var ft = new FrameTime(3.25f);
        Assert.Equal(3, ft.FrameIndex);
        Assert.Equal(0.25, (double)ft.Percentage, 4);
    }

    [Fact]
    public void ToFloat_RecombinesFrameAndPercentage()
        => Assert.Equal(3.25, (double)new FrameTime(3, 0.25f).ToFloat(), 4);

    [Fact]
    public void Percentage_StaysInUnitRange()
    {
        var ft = new FrameTime(10, 0.75f);
        Assert.InRange(ft.Percentage, 0f, 1f);
    }
}
