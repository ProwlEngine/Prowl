// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Tests for the <see cref="LayerMask"/> bit operations.</summary>
public class LayerMaskTests
{
    [Fact]
    public void SetHasRemoveLayer()
    {
        var m = new LayerMask();
        Assert.False(m.HasLayer(3));

        m.SetLayer(3);
        Assert.True(m.HasLayer(3));
        Assert.False(m.HasLayer(4));

        m.RemoveLayer(3);
        Assert.False(m.HasLayer(3));
    }

    [Fact]
    public void EverythingAndNothing()
    {
        Assert.True(LayerMask.Everything.HasLayer(0));
        Assert.True(LayerMask.Everything.HasLayer(31));
        Assert.False(LayerMask.Nothing.HasLayer(0));
        Assert.False(LayerMask.Nothing.HasLayer(15));
    }

    [Fact]
    public void OrCombinesMasks()
    {
        var a = new LayerMask(); a.SetLayer(1);
        var b = new LayerMask(); b.SetLayer(2);

        var or = a | b;

        Assert.True(or.HasLayer(1));
        Assert.True(or.HasLayer(2));
        Assert.False(or.HasLayer(3));
    }

    [Fact]
    public void AndFiltersMasks()
    {
        var a = new LayerMask(); a.SetLayer(1);

        var and = LayerMask.Everything & a;

        Assert.True(and.HasLayer(1));
        Assert.False(and.HasLayer(2));
    }

    [Fact]
    public void Clear_ResetsToZero()
    {
        var m = new LayerMask();
        m.SetLayer(5);
        m.SetLayer(9);

        m.Clear();

        Assert.Equal(0u, m.Mask);
    }

    [Fact]
    public void SettingSameLayerTwice_IsIdempotent()
    {
        var m = new LayerMask();
        m.SetLayer(7);
        uint once = m.Mask;
        m.SetLayer(7);
        Assert.Equal(once, m.Mask);
    }
}
