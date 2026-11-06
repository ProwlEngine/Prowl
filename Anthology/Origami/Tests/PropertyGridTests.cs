using Prowl.OrigamiUI;

using Xunit;

namespace Origami.Tests;

public class PropertyGridTests
{
    [Fact]
    public void FormatFieldName_ReturnsLabelsAsExpected()
    {
        Assert.Equal("Test Field", PropertyGridRenderer.FormatFieldName("testField"));
        Assert.Equal("Test Field", PropertyGridRenderer.FormatFieldName("_testField"));
        Assert.Equal("Test Field", PropertyGridRenderer.FormatFieldName("m_testField"));
        Assert.Equal("Property Name", PropertyGridRenderer.FormatFieldName("<PropertyName>k__BackingField"));
    }
}
