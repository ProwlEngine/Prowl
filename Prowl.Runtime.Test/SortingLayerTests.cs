// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Graphite.Rendering;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public sealed class SortingLayerTests : RuntimeTestBase
{
    private const int Background = 3, Foreground = 5;

    public SortingLayerTests()
    {
        TagLayerManager.SetSortingLayers([
            new SortingLayerInfo { Id = Background, Name = "Background" },
            new SortingLayerInfo { Id = SortingLayer.DefaultId, Name = "Default" },
            new SortingLayerInfo { Id = Foreground, Name = "Foreground" },
        ]);
    }

    public override void Dispose()
    {
        TagLayerManager.ResetDefault();
        base.Dispose();
    }

    [Fact]
    public void LayerOrder_OutranksOrderInLayer()
    {
        Assert.True(TagLayerManager.SortingOrderOf(Background, short.MaxValue) < TagLayerManager.SortingOrderOf(SortingLayer.DefaultId, short.MinValue));
        Assert.True(TagLayerManager.SortingOrderOf(SortingLayer.DefaultId, short.MaxValue) < TagLayerManager.SortingOrderOf(Foreground, short.MinValue));
    }

    [Fact]
    public void DefaultLayerAtOrderZero_SortsLikeEveryOtherRenderer()
        => Assert.Equal(0, TagLayerManager.SortingOrderOf(SortingLayer.DefaultId, 0));

    [Fact]
    public void OrderInLayer_IsClampedToItsRange()
        => Assert.Equal(TagLayerManager.SortingOrderOf(SortingLayer.DefaultId, short.MaxValue), TagLayerManager.SortingOrderOf(SortingLayer.DefaultId, 1_000_000));

    [Fact]
    public void MissingLayer_SortsAsDefault()
        => Assert.Equal(TagLayerManager.SortingOrderOf(SortingLayer.DefaultId, 4), TagLayerManager.SortingOrderOf(99, 4));

    [Fact]
    public void ReorderingLayers_KeepsRenderersOnTheirLayer()
    {
        SpriteRenderer sprite = CreateGameObject().AddComponent<SpriteRenderer>();
        sprite.SortingLayer = new SortingLayer(Background);

        TagLayerManager.SetSortingLayers([
            new SortingLayerInfo { Id = SortingLayer.DefaultId, Name = "Default" },
            new SortingLayerInfo { Id = Background, Name = "Renamed" },
        ]);

        Assert.Equal("Renamed", sprite.SortingLayer.Name);
        Assert.True(sprite.SortOrder().Order > 0);
    }

    [Fact]
    public void SettingLayers_WithoutDefault_KeepsDefault()
    {
        TagLayerManager.SetSortingLayers([new SortingLayerInfo { Id = Foreground, Name = "Foreground" }]);

        Assert.Contains(TagLayerManager.SortingLayers, l => l.Id == SortingLayer.DefaultId);
    }

    [Fact]
    public void Sprite_WithoutGroup_SortsByItsOwnLayerAndOrder()
    {
        SpriteRenderer sprite = CreateGameObject().AddComponent<SpriteRenderer>();
        sprite.SortingLayer = new SortingLayer(Foreground);
        sprite.SortingOrder = 7;

        Assert.Equal(new TransparentOrder(TagLayerManager.SortingOrderOf(Foreground, 7)), sprite.SortOrder());
    }

    [Fact]
    public void Sprite_InGroup_SortsAsTheGroup_ThenByItsOwnOrder()
    {
        GameObject root = CreateGameObject("Root");
        root.Transform.Position = new Float3(1, 2, 3);
        SortingGroup group = root.AddComponent<SortingGroup>();
        group.SortingLayer = new SortingLayer(Background);
        group.SortingOrder = 2;
        GameObject child = CreateGameObject("Child");
        child.SetParent(root);
        SpriteRenderer sprite = child.AddComponent<SpriteRenderer>();
        sprite.SortingOrder = 9;

        TransparentOrder order = sprite.SortOrder();

        Assert.Equal(TagLayerManager.SortingOrderOf(SortingLayer.DefaultId, 9), order.Order);
        Assert.Equal(new SortGroupLevel(TagLayerManager.SortingOrderOf(Background, 2), (uint)group.InstanceID, new Double3(1, 2, 3)), Assert.Single(order.Groups!));
    }

    [Fact]
    public void Sprite_UnderDisabledGroup_UsesTheNextEnabledGroupUp()
    {
        GameObject outer = CreateGameObject("Outer");
        SortingGroup outerGroup = outer.AddComponent<SortingGroup>();
        GameObject inner = CreateGameObject("Inner");
        inner.SetParent(outer);
        inner.AddComponent<SortingGroup>().Enabled = false;
        GameObject child = CreateGameObject("Child");
        child.SetParent(inner);
        SpriteRenderer sprite = child.AddComponent<SpriteRenderer>();

        Assert.Equal((uint)outerGroup.InstanceID, Assert.Single(sprite.SortOrder().Groups!).Id);
    }

    [Fact]
    public void NestedGroups_ChainOutermostFirst()
    {
        GameObject outer = CreateGameObject("Outer");
        SortingGroup outerGroup = outer.AddComponent<SortingGroup>();
        GameObject inner = CreateGameObject("Inner");
        inner.SetParent(outer);
        SortingGroup innerGroup = inner.AddComponent<SortingGroup>();
        innerGroup.SortingOrder = 4;
        GameObject child = CreateGameObject("Child");
        child.SetParent(inner);
        SpriteRenderer sprite = child.AddComponent<SpriteRenderer>();

        Assert.Equal([(uint)outerGroup.InstanceID, (uint)innerGroup.InstanceID], sprite.SortOrder().Groups!.Select(g => g.Id));
        Assert.Equal(4, sprite.SortOrder().Groups![1].Order);
    }

    [Fact]
    public void GroupChain_IsTheSameArray_UntilTheGroupChanges()
    {
        SortingGroup group = CreateGameObject("Group").AddComponent<SortingGroup>();
        SortGroupLevel[] first = group.Chain();

        Assert.Same(first, group.Chain());
        group.SortingOrder = 1;
        Assert.NotSame(first, group.Chain());
    }

    [Fact]
    public void EveryRenderer_JoinsTheGroupAboveIt()
    {
        GameObject root = CreateGameObject("Root");
        SortingGroup group = root.AddComponent<SortingGroup>();
        GameObject child = CreateGameObject("Child");
        child.SetParent(root);

        TransparentOrder order = SortingGroup.OrderOf(child, new SortingLayer(Foreground), 3);

        Assert.Equal(TagLayerManager.SortingOrderOf(Foreground, 3), order.Order);
        Assert.Same(group.Chain(), order.Groups);
    }

    [Fact]
    public void WorldCanvas_SortsAsAGroupInsideTheGroupAboveIt()
    {
        GameObject root = CreateGameObject("Root");
        SortingGroup group = root.AddComponent<SortingGroup>();
        GameObject child = CreateGameObject("Canvas");
        child.SetParent(root);
        GameCanvas canvas = child.AddComponent<GameCanvas>();
        canvas.SortingLayer = new SortingLayer(Background);
        canvas.SortOrder = 5;

        SortGroupLevel[] chain = canvas.SortChain(new Double3(0, 1, 0));

        Assert.Equal((uint)group.InstanceID, chain[0].Id);
        Assert.Equal(new SortGroupLevel(TagLayerManager.SortingOrderOf(Background, 5), (uint)canvas.InstanceID, new Double3(0, 1, 0)), chain[1]);
        Assert.Same(chain, canvas.SortChain(new Double3(0, 1, 0)));
    }
}
