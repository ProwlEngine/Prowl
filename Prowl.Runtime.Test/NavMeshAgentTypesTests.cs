// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Navigation;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshAgentTypes"/> and <see cref="NavMeshAreas"/>: the built-in entries a
/// project always has, id/index stability across a rename, and the reserved-slot/cost-clamp rules
/// each registry enforces. These are process-wide static state, so each test resets before and after.
/// </summary>
public class NavMeshAgentTypesTests
{
    public NavMeshAgentTypesTests()
    {
        NavMeshAgentTypes.ResetDefault();
        NavMeshAreas.ResetDefault();
    }

    [Fact]
    public void HumanoidType_AlwaysExistsAndCannotBeRemoved()
    {
        Assert.NotNull(NavMeshAgentTypes.GetById(NavMeshAgentTypes.HumanoidId));
        Assert.False(NavMeshAgentTypes.Remove(NavMeshAgentTypes.HumanoidId));
        Assert.NotNull(NavMeshAgentTypes.GetById(NavMeshAgentTypes.HumanoidId));
    }

    [Fact]
    public void AddedType_KeepsItsIdAfterRename()
    {
        int id = NavMeshAgentTypes.Add("Small", 0.25f, 1.0f, 45f, 0.2f);

        NavMeshAgentTypeInfo? info = NavMeshAgentTypes.GetById(id);
        Assert.NotNull(info);
        info!.Name = "Renamed";

        Assert.Same(info, NavMeshAgentTypes.GetById(id));
        Assert.Equal("Renamed", NavMeshAgentTypes.GetById(id)!.Name);
    }

    [Fact]
    public void RemovingAType_DoesNotRenumberOthers()
    {
        int a = NavMeshAgentTypes.Add("A", 0.5f, 2f, 45f, 0.4f);
        int b = NavMeshAgentTypes.Add("B", 0.5f, 2f, 45f, 0.4f);

        Assert.True(NavMeshAgentTypes.Remove(a));

        Assert.Null(NavMeshAgentTypes.GetById(a));
        Assert.NotNull(NavMeshAgentTypes.GetById(b));
        Assert.Equal("B", NavMeshAgentTypes.GetById(b)!.Name);
    }

    [Fact]
    public void ReplaceAll_ReinsertsHumanoidIfMissing()
    {
        NavMeshAgentTypes.ReplaceAll([new NavMeshAgentTypeInfo { Id = 7, Name = "OnlyCustom" }]);

        Assert.NotNull(NavMeshAgentTypes.GetById(NavMeshAgentTypes.HumanoidId));
        Assert.NotNull(NavMeshAgentTypes.GetById(7));
    }

    [Fact]
    public void ReplaceAll_NextIdNeverCollidesWithLoadedIds()
    {
        NavMeshAgentTypes.ReplaceAll([NavMeshAgentTypeInfo.CreateHumanoid(), new NavMeshAgentTypeInfo { Id = 50, Name = "Loaded" }]);

        int freshId = NavMeshAgentTypes.Add("Fresh", 0.5f, 2f, 45f, 0.4f);

        Assert.True(freshId > 50);
    }

    [Fact]
    public void AgentTypeList_RoundTripsIdsThroughEchoAfterRename()
    {
        int id = NavMeshAgentTypes.Add("Original", 0.6f, 1.8f, 40f, 0.3f);
        NavMeshAgentTypes.GetById(id)!.Name = "Renamed";

        var toSave = new List<NavMeshAgentTypeInfo>(NavMeshAgentTypes.Types);
        EchoObject echo = Serializer.Serialize(toSave);
        List<NavMeshAgentTypeInfo>? restored = Serializer.Deserialize<List<NavMeshAgentTypeInfo>>(echo);

        Assert.NotNull(restored);
        NavMeshAgentTypeInfo? restoredEntry = restored!.Find(t => t.Id == id);
        Assert.NotNull(restoredEntry);
        Assert.Equal("Renamed", restoredEntry!.Name);
    }

    [Fact]
    public void ReservedAreaSlots_NamesCannotBeChanged()
    {
        NavMeshAreas.SetAreaName(NavMeshAreas.Walkable, "Something Else");
        NavMeshAreas.SetAreaName(NavMeshAreas.NotWalkable, "Something Else");
        NavMeshAreas.SetAreaName(NavMeshAreas.Jump, "Something Else");

        Assert.Equal("Walkable", NavMeshAreas.GetAreaName(NavMeshAreas.Walkable));
        Assert.Equal("Not Walkable", NavMeshAreas.GetAreaName(NavMeshAreas.NotWalkable));
        Assert.Equal("Jump", NavMeshAreas.GetAreaName(NavMeshAreas.Jump));
    }

    [Fact]
    public void ReservedAreaSlots_CostCanStillBeChanged()
    {
        NavMeshAreas.SetAreaCost(NavMeshAreas.Jump, 5f);
        Assert.Equal(5f, NavMeshAreas.GetAreaCost(NavMeshAreas.Jump));
    }

    [Fact]
    public void CustomAreaSlot_CanBeNamedAndCosted()
    {
        NavMeshAreas.SetAreaName(3, "Mud");
        NavMeshAreas.SetAreaCost(3, 3f);

        Assert.Equal("Mud", NavMeshAreas.GetAreaName(3));
        Assert.Equal(3f, NavMeshAreas.GetAreaCost(3));
    }

    [Fact]
    public void AreaCost_IsClampedToAtLeastOne()
    {
        NavMeshAreas.SetAreaCost(3, 0.1f);
        Assert.Equal(1f, NavMeshAreas.GetAreaCost(3));

        NavMeshAreas.SetAreaCost(3, -5f);
        Assert.Equal(1f, NavMeshAreas.GetAreaCost(3));
    }

    [Theory]
    [InlineData(NavMeshAreas.Walkable)]
    [InlineData(NavMeshAreas.Jump)]
    [InlineData(10)]
    public void ToRecastArea_RoundTripsBackToTheSameProwlIndex(int prowlIndex)
    {
        int recastArea = NavMeshAreas.ToRecastArea(prowlIndex);
        Assert.Equal(prowlIndex, NavMeshAreas.FromRecastArea(recastArea));
    }

    [Fact]
    public void NotWalkable_MapsToTheRecastNullArea()
    {
        Assert.Equal(0, NavMeshAreas.ToRecastArea(NavMeshAreas.NotWalkable));
    }
}
