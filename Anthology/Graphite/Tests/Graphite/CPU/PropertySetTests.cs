using Xunit;

namespace Prowl.Graphite.Tests;

// Covers the CPU-only surface of PropertySet: scalar uniform writes, entry de-duplication
// by name. Resource setters (buffer/texture/sampler)
// require a live GraphicsDevice and are exercised by the GPU resource tests instead.
public class PropertySetTests
{
    [Fact]
    public void SetScalar_SameName_DifferentType_StaysOneEntry()
    {
        PropertySet set = new();

        set.SetFloat("v", 1.0f);
        set.SetInt("v", 2);

        // The entry is rewritten in place with the new scalar type rather than duplicated.
        Assert.Equal(1, set.EntryCount);
    }
}
