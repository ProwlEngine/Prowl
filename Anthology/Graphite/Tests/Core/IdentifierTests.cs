#nullable enable

using Xunit;

namespace Prowl.Veldrid.Tests;

public class IdentifierTests
{
    // ------------------------------------------------------------------
    // PropertyID
    // ------------------------------------------------------------------

    [Fact]
    public void PropertyID_Intern_SameName_SameId()
    {
        PropertyID a = PropertyID.Intern("PropertyID_Intern_SameName");
        PropertyID b = PropertyID.Intern("PropertyID_Intern_SameName");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void PropertyID_Intern_DifferentNames_DifferentIds()
    {
        PropertyID a = PropertyID.Intern("PropertyID_diff_a");
        PropertyID b = PropertyID.Intern("PropertyID_diff_b");

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void PropertyID_ImplicitFromString_EqualsIntern()
    {
        PropertyID implicitId = "PropertyID_implicit";
        PropertyID explicitId = PropertyID.Intern("PropertyID_implicit");

        Assert.Equal(explicitId, implicitId);
    }

    [Fact]
    public void PropertyID_Default_IsInvalid()
    {
        PropertyID def = default;

        Assert.False(def.IsValid);
    }

    [Fact]
    public void PropertyID_Interned_IsValid()
    {
        PropertyID id = PropertyID.Intern("PropertyID_valid");

        Assert.True(id.IsValid);
    }

    [Fact]
    public void PropertyID_ReverseLookup_ReturnsName()
    {
        PropertyID id = PropertyID.Intern("PropertyID_reverse");

        Assert.Equal("PropertyID_reverse", PropertyID.ToString(id));
    }

    [Fact]
    public void PropertyID_ReverseLookup_Unknown_ReturnsNull()
    {
        Assert.Null(PropertyID.ToString(default));
    }

    // ------------------------------------------------------------------
    // ShaderID
    // ------------------------------------------------------------------

    [Fact]
    public void ShaderID_Intern_SameName_SameId()
    {
        ShaderID a = ShaderID.Intern("ShaderID_same");
        ShaderID b = "ShaderID_same";

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void ShaderID_Default_IsInvalid()
    {
        Assert.False(default(ShaderID).IsValid);
    }

    [Fact]
    public void ShaderID_ReverseLookup_RoundTrips()
    {
        ShaderID id = ShaderID.Intern("ShaderID_reverse");

        Assert.Equal("ShaderID_reverse", ShaderID.ToString(id));
    }

    // ------------------------------------------------------------------
    // VertexAttributeID
    // ------------------------------------------------------------------

    [Fact]
    public void VertexAttributeID_Intern_SameName_SameId()
    {
        VertexAttributeID a = VertexAttributeID.Intern("POSITION");
        VertexAttributeID b = "POSITION";

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void VertexAttributeID_DifferentNames_DifferentIds()
    {
        VertexAttributeID a = VertexAttributeID.Intern("VA_a");
        VertexAttributeID b = VertexAttributeID.Intern("VA_b");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void VertexAttributeID_ReverseLookup_RoundTrips()
    {
        VertexAttributeID id = VertexAttributeID.Intern("TEXCOORD0");

        Assert.Equal("TEXCOORD0", VertexAttributeID.ToString(id));
    }

    [Fact]
    public void IdSpaces_AreIndependent_AcrossIdTypes()
    {
        // Each ID struct owns a private interner, so the same string is free to map
        // to the same underlying counter value without colliding across types.
        PropertyID p = PropertyID.Intern("shared-name");
        ShaderID s = ShaderID.Intern("shared-name");
        VertexAttributeID v = VertexAttributeID.Intern("shared-name");

        Assert.True(p.IsValid);
        Assert.True(s.IsValid);
        Assert.True(v.IsValid);
        Assert.Equal("shared-name", PropertyID.ToString(p));
        Assert.Equal("shared-name", ShaderID.ToString(s));
        Assert.Equal("shared-name", VertexAttributeID.ToString(v));
    }
}
