using System.Globalization;

using Prowl.Editor.GUI.SceneView;

using Xunit;

namespace Prowl.Editor.Test;

public class MoveSnapFormatTests
{
    [Theory]
    [InlineData(0.5f, "1/2")]
    [InlineData(0.0625f, "1/16")]
    [InlineData(1f / 64f, "1/64")]
    [InlineData(1f, "1")]
    [InlineData(128f, "128")]
    [InlineData(0.3f, "0.3")]
    [InlineData(0.75f, "0.75")]
    [InlineData(0.4999f, "0.4999")]
    [InlineData(0.0626f, "0.0626")]
    public void GridSizeShowsAsFractionOnlyForHalvingSteps(float value, string expected)
    {
        Assert.Equal(expected, value.ToString(SceneTools.MoveSnapFormat(value), CultureInfo.InvariantCulture));
    }
}
