using Prowl.Graphite.D3D11;

using Xunit;

namespace Prowl.Graphite.Tests;

// D3D11 binds a vertex input by its raw semantic name plus an index taken from the layout's Location.
// D3D11ResourceCache.ResolveSemanticBindings produces the (SemanticName, SemanticIndex) pairs the
// input layout is built from.
public class D3D11SemanticBindingTests
{
    static VertexLayoutDescription Layout(uint location, params VertexElementDescription[] elements)
        => new(location, elements);

    // Mirrors the compiler's output: a blended user-facing name plus the raw D3D11 semantic.
    static VertexElementDescription Raw(string rawSemantic)
        => new("blended", VertexElementFormat.Float4) { D3D11SemanticName = rawSemantic };

    [Fact]
    public void UsesRawSemanticAndLocationAsIndex()
    {
        (string Name, uint Index)[] bindings = D3D11ResourceCache.ResolveSemanticBindings(
        [
            Layout(0, Raw("POSITION")),
            Layout(0, Raw("UV")),
            Layout(3, Raw("UV")),
        ]);

        Assert.Equal(("POSITION", 0u), bindings[0]);
        Assert.Equal(("UV", 0u), bindings[1]);
        Assert.Equal(("UV", 3u), bindings[2]);
    }

    [Fact]
    public void MultiElementLayout_ContinuesFromLocation()
    {
        (string Name, uint Index)[] bindings = D3D11ResourceCache.ResolveSemanticBindings(
        [
            Layout(0, Raw("UV"), Raw("UV"), Raw("UV")),
        ]);

        Assert.Equal(("UV", 0u), bindings[0]);
        Assert.Equal(("UV", 1u), bindings[1]);
        Assert.Equal(("UV", 2u), bindings[2]);
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Empty(D3D11ResourceCache.ResolveSemanticBindings([]));
    }
}
