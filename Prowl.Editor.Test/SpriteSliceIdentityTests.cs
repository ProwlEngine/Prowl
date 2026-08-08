// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Importers;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

[Trait("Category", "Sprites")]
public class SpriteSliceIdentityTests
{
    private static SpriteSliceData Slice(string name, int x, int y, int w, int h, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Rect = new SpriteRect(x, y, w, h),
    };

    private static List<SpriteSliceData> Generated(params SpriteSliceData[] slices) => new(slices);

    [Fact]
    public void IdenticalRects_KeepIdAndName()
    {
        var previous = new List<SpriteSliceData> { Slice("hero_idle", 0, 0, 16, 16) };
        Guid original = previous[0].Id;

        var result = SpriteSliceMatcher.CarryOverIdentities(previous, Generated(Slice("tex_0", 0, 0, 16, 16)));

        Assert.Equal(original, result[0].Id);
        Assert.Equal("hero_idle", result[0].Name);
    }

    [Fact]
    public void ShiftedRects_KeepIdWhenTheyMostlyOverlap()
    {
        var previous = new List<SpriteSliceData> { Slice("hero", 0, 0, 16, 16) };
        Guid original = previous[0].Id;

        // Nudging the grid by a pixel still describes the same sprite.
        var result = SpriteSliceMatcher.CarryOverIdentities(previous, Generated(Slice("tex_0", 1, 0, 16, 16)));

        Assert.Equal(original, result[0].Id);
        Assert.Equal(1, result[0].Rect.X);
    }

    [Fact]
    public void BarelyOverlappingRects_GetAFreshId()
    {
        var previous = new List<SpriteSliceData> { Slice("hero", 0, 0, 16, 16) };
        Guid original = previous[0].Id;

        // Only a sliver in common - a different sprite, not the same one moved.
        var result = SpriteSliceMatcher.CarryOverIdentities(previous, Generated(Slice("tex_0", 14, 0, 16, 16)));

        Assert.NotEqual(original, result[0].Id);
    }

    [Fact]
    public void EachPreviousSliceIsClaimedOnce()
    {
        var previous = new List<SpriteSliceData> { Slice("a", 0, 0, 16, 16) };
        Guid original = previous[0].Id;

        // Two new rects both overlap the single old one; only one may inherit its identity.
        var result = SpriteSliceMatcher.CarryOverIdentities(previous,
            Generated(Slice("tex_0", 0, 0, 16, 16), Slice("tex_1", 2, 0, 16, 16)));

        int inherited = 0;
        foreach (SpriteSliceData s in result)
            if (s.Id == original) inherited++;

        Assert.Equal(1, inherited);
        Assert.NotEqual(result[0].Id, result[1].Id);
    }

    [Fact]
    public void ExactMatchWinsOverOverlap()
    {
        var exact = Slice("exact", 0, 0, 16, 16);
        var near = Slice("near", 1, 0, 16, 16);
        var previous = new List<SpriteSliceData> { near, exact };

        var result = SpriteSliceMatcher.CarryOverIdentities(previous, Generated(Slice("tex_0", 0, 0, 16, 16)));

        Assert.Equal(exact.Id, result[0].Id);
    }

    [Fact]
    public void RegeneratedGrid_KeepsEveryIdentity()
    {
        var previous = new List<SpriteSliceData>();
        for (int i = 0; i < 4; i++)
            previous.Add(Slice($"frame_{i}", i * 16, 0, 16, 16));

        // Same grid, regenerated from scratch with fresh Guids.
        var generated = new List<SpriteSliceData>();
        for (int i = 0; i < 4; i++)
            generated.Add(Slice($"tex_{i}", i * 16, 0, 16, 16));

        var result = SpriteSliceMatcher.CarryOverIdentities(previous, generated);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(previous[i].Id, result[i].Id);
            Assert.Equal($"frame_{i}", result[i].Name);
        }
    }

    [Fact]
    public void AuthoredBorderSurvives_ButPresetPivotFollowsTheNewSettings()
    {
        var old = Slice("hero", 0, 0, 16, 16);
        old.Border = new Float4(2, 3, 4, 5);
        old.Alignment = SpriteAlignment.BottomLeft;

        var incoming = Slice("tex_0", 0, 0, 16, 16);
        incoming.Alignment = SpriteAlignment.TopRight; // what the user just picked for this run

        var result = SpriteSliceMatcher.CarryOverIdentities(new List<SpriteSliceData> { old }, Generated(incoming));

        Assert.Equal(new Float4(2, 3, 4, 5), result[0].Border);
        Assert.Equal(SpriteAlignment.TopRight, result[0].Alignment);
    }

    [Fact]
    public void CustomPivotSurvives_BecauseNoSlicingSettingCanReproduceIt()
    {
        var old = Slice("hero", 0, 0, 16, 16);
        old.Alignment = SpriteAlignment.Custom;
        old.CustomPivot = new Float2(0.25f, 0.75f);
        old.PivotUnit = PivotUnitMode.Normalized;

        var incoming = Slice("tex_0", 0, 0, 16, 16);
        incoming.Alignment = SpriteAlignment.Center;

        var result = SpriteSliceMatcher.CarryOverIdentities(new List<SpriteSliceData> { old }, Generated(incoming));

        Assert.Equal(SpriteAlignment.Custom, result[0].Alignment);
        Assert.Equal(new Float2(0.25f, 0.75f), result[0].CustomPivot);
    }

    [Fact]
    public void NewSlicesBeyondThePreviousSetKeepTheirOwnIds()
    {
        var previous = new List<SpriteSliceData> { Slice("a", 0, 0, 16, 16) };

        var first = Slice("tex_0", 0, 0, 16, 16);
        var second = Slice("tex_1", 32, 0, 16, 16);
        Guid secondId = second.Id;

        var result = SpriteSliceMatcher.CarryOverIdentities(previous, Generated(first, second));

        Assert.Equal(previous[0].Id, result[0].Id);
        Assert.Equal(secondId, result[1].Id);
    }

    [Fact]
    public void EmptyPreviousList_IsPassedThroughUnchanged()
    {
        var generated = Generated(Slice("tex_0", 0, 0, 16, 16));
        Guid id = generated[0].Id;

        var result = SpriteSliceMatcher.CarryOverIdentities(new List<SpriteSliceData>(), generated);

        Assert.Single(result);
        Assert.Equal(id, result[0].Id);
    }
}
