// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// NavMeshModifier (per-object area/exclusion at collection time), NavMeshModifierVolume
/// (area stamped over a region post-rasterization), and the Not Walkable null-area semantics
/// both rely on.
/// </summary>
public class NavMeshModifierTests : RuntimeTestBase
{
    private const int Mud = 3; // arbitrary user area

    private GameObject AddFloorBox(Scene scene, string name, Float3 center, Float3 size)
    {
        GameObject go = CreateGameObject(name);
        scene.Add(go);
        go.AddComponent<BoxCollider>().Size = size;
        go.Transform.Position = center;
        return go;
    }

    private NavMeshSurface AddSurface(Scene scene, int agentTypeId = 0)
    {
        GameObject surfaceGo = CreateGameObject("NavMeshSurface");
        scene.Add(surfaceGo);
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        ApplyFastBakeSettings(surface);
        surface.AgentTypeId = agentTypeId;
        return surface;
    }

    private static int SampleAreaMask(Scene scene, Float3 position, int agentTypeId = 0)
    {
        var filter = new NavMeshQueryFilter { AgentTypeId = agentTypeId };
        return scene.Navigation.SamplePosition(position, out NavMeshHit hit, 0.5f, filter) ? hit.Mask : 0;
    }

    // ── NavMeshModifier ─────────────────────────────────────────────────

    /// <summary>OverrideArea stamps the modifier's area on that object's polys only.</summary>
    [Fact]
    public void Modifier_OverrideArea_LandsOnThatObjectsPolys()
    {
        Scene scene = CreateScene(enable: true);
        AddFloorBox(scene, "Plain", new Float3(-5, -0.5f, 0), new Float3(10, 1, 10));
        GameObject marked = AddFloorBox(scene, "Marked", new Float3(5, -0.5f, 0), new Float3(10, 1, 10));
        var modifier = marked.AddComponent<NavMeshModifier>();
        modifier.OverrideArea = true;
        modifier.Area = Mud;

        Assert.True(AddSurface(scene).BuildNavMesh());

        Assert.Equal(1 << NavMeshAreas.Walkable, SampleAreaMask(scene, new Float3(-5, 0.2f, 0)));
        Assert.Equal(1 << Mud, SampleAreaMask(scene, new Float3(5, 0.2f, 0)));
    }

    /// <summary>IgnoreFromBuild removes the object's geometry from the bake.</summary>
    [Fact]
    public void Modifier_IgnoreFromBuild_ExcludesGeometry()
    {
        Scene scene = CreateScene(enable: true);
        AddFloorBox(scene, "Plain", new Float3(-5, -0.5f, 0), new Float3(10, 1, 10));
        GameObject ignored = AddFloorBox(scene, "Ignored", new Float3(5, -0.5f, 0), new Float3(10, 1, 10));
        ignored.AddComponent<NavMeshModifier>().IgnoreFromBuild = true;

        Assert.True(AddSurface(scene).BuildNavMesh());

        Assert.NotEqual(0, SampleAreaMask(scene, new Float3(-5, 0.2f, 0)));
        Assert.Equal(0, SampleAreaMask(scene, new Float3(5, 0.2f, 0)));
    }

    /// <summary>A parent's modifier applies to children, but a child's own modifier wins.</summary>
    [Fact]
    public void Modifier_ChildModifier_BeatsInheritedParentModifier()
    {
        Scene scene = CreateScene(enable: true);

        GameObject parent = CreateGameObject("Parent");
        scene.Add(parent);
        var parentModifier = parent.AddComponent<NavMeshModifier>();
        parentModifier.OverrideArea = true;
        parentModifier.Area = Mud;
        parentModifier.ApplyToChildren = true;

        GameObject inheriting = AddFloorBox(scene, "Inheriting", new Float3(-5, -0.5f, 0), new Float3(10, 1, 10));
        inheriting.SetParent(parent);
        GameObject overriding = AddFloorBox(scene, "Overriding", new Float3(5, -0.5f, 0), new Float3(10, 1, 10));
        overriding.SetParent(parent);
        var childModifier = overriding.AddComponent<NavMeshModifier>();
        childModifier.OverrideArea = true;
        childModifier.Area = NavMeshAreas.Jump;

        Assert.True(AddSurface(scene).BuildNavMesh());

        Assert.Equal(1 << Mud, SampleAreaMask(scene, new Float3(-5, 0.2f, 0)));
        Assert.Equal(1 << NavMeshAreas.Jump, SampleAreaMask(scene, new Float3(5, 0.2f, 0)));
    }

    /// <summary>A modifier scoped to another agent type is transparent to this bake.</summary>
    [Fact]
    public void Modifier_AgentTypeScoped_OnlyAffectsThatTypesBakes()
    {
        try
        {
            NavMeshAgentTypes.ApplyTable(
            [
                new NavMeshAgentType { Id = 0, Name = "Humanoid" },
                new NavMeshAgentType { Id = 3, Name = "Scout", Radius = 0.4f },
            ]);

            Scene scene = CreateScene(enable: true);
            GameObject floor = AddFloorBox(scene, "Floor", new Float3(0, -0.5f, 0), new Float3(20, 1, 20));
            var modifier = floor.AddComponent<NavMeshModifier>();
            modifier.IgnoreFromBuild = true;
            modifier.AffectAllAgentTypes = false;
            modifier.AffectedAgentTypeIds = [3];

            // Type 0: modifier is transparent, the floor bakes.
            Assert.True(AddSurface(scene, agentTypeId: 0).BuildNavMesh());
            Assert.NotEqual(0, SampleAreaMask(scene, new Float3(0, 0.2f, 0)));

            // Type 3: the floor is excluded and the bake has nothing.
            Assert.False(AddSurface(scene, agentTypeId: 3).BuildNavMesh());
        }
        finally
        {
            NavMeshAgentTypes.ApplyTable([new NavMeshAgentType { Id = 0, Name = "Humanoid" }]);
        }
    }

    /// <summary>Non-affecting modifiers are transparent, not shielding: a parent whose
    /// modifier is scoped to another agent type is walked PAST, so the child inherits the
    /// grandparent's override.</summary>
    [Fact]
    public void Modifier_WalksPastNonAffectingAncestor_ToHigherOne()
    {
        Scene scene = CreateScene(enable: true);

        GameObject grandparent = CreateGameObject("Grandparent");
        scene.Add(grandparent);
        var grandModifier = grandparent.AddComponent<NavMeshModifier>();
        grandModifier.OverrideArea = true;
        grandModifier.Area = Mud;
        grandModifier.ApplyToChildren = true;

        GameObject parent = CreateGameObject("Parent");
        scene.Add(parent);
        parent.SetParent(grandparent);
        var parentModifier = parent.AddComponent<NavMeshModifier>();
        parentModifier.OverrideArea = true;
        parentModifier.Area = NavMeshAreas.Jump;
        parentModifier.ApplyToChildren = true;
        parentModifier.AffectAllAgentTypes = false;
        parentModifier.AffectedAgentTypeIds = [7]; // not this bake's type

        GameObject child = AddFloorBox(scene, "Child", new Float3(0, -0.5f, 0), new Float3(10, 1, 10));
        child.SetParent(parent);

        Assert.True(AddSurface(scene).BuildNavMesh());
        Assert.Equal(1 << Mud, SampleAreaMask(scene, new Float3(0, 0.2f, 0)));
    }

    /// <summary>An object's own modifier shields inherited overrides even when it overrides
    /// nothing itself: a valid child modifier with OverrideArea off means "default area",
    /// not "fall through to the parent's override".</summary>
    [Fact]
    public void Modifier_OwnNoOpModifier_ShieldsInheritedOverride()
    {
        Scene scene = CreateScene(enable: true);

        GameObject parent = CreateGameObject("Parent");
        scene.Add(parent);
        var parentModifier = parent.AddComponent<NavMeshModifier>();
        parentModifier.OverrideArea = true;
        parentModifier.Area = Mud;
        parentModifier.ApplyToChildren = true;

        GameObject inheriting = AddFloorBox(scene, "Inheriting", new Float3(-5, -0.5f, 0), new Float3(10, 1, 10));
        inheriting.SetParent(parent);
        GameObject shielded = AddFloorBox(scene, "Shielded", new Float3(5, -0.5f, 0), new Float3(10, 1, 10));
        shielded.SetParent(parent);
        shielded.AddComponent<NavMeshModifier>(); // valid, but OverrideArea off — a no-op that still wins

        Assert.True(AddSurface(scene).BuildNavMesh());

        Assert.Equal(1 << Mud, SampleAreaMask(scene, new Float3(-5, 0.2f, 0)));
        Assert.Equal(1 << NavMeshAreas.Walkable, SampleAreaMask(scene, new Float3(5, 0.2f, 0)));
    }

    // ── NavMeshModifierVolume ───────────────────────────────────────────

    /// <summary>The volume stamps its area only inside its footprint.</summary>
    [Fact]
    public void ModifierVolume_StampsAreaOnlyInsideFootprint()
    {
        Scene scene = CreateScene(enable: true);
        AddFloorBox(scene, "Floor", new Float3(0, -0.5f, 0), new Float3(20, 1, 20));

        GameObject volumeGo = CreateGameObject("MudZone");
        scene.Add(volumeGo);
        volumeGo.Transform.Position = new Float3(5, 0, 5);
        var volume = volumeGo.AddComponent<NavMeshModifierVolume>();
        volume.Size = new Float3(6, 3, 6); // covers x/z 2..8
        volume.Area = Mud;

        Assert.True(AddSurface(scene).BuildNavMesh());

        Assert.Equal(1 << Mud, SampleAreaMask(scene, new Float3(5, 0.2f, 5)));
        Assert.Equal(1 << NavMeshAreas.Walkable, SampleAreaMask(scene, new Float3(-5, 0.2f, -5)));
    }

    /// <summary>A Not Walkable volume erases walkability inside the footprint — the hole is
    /// real for pathing, not just recoloured.</summary>
    [Fact]
    public void ModifierVolume_NotWalkable_PunchesHole()
    {
        Scene scene = CreateScene(enable: true);
        AddFloorBox(scene, "Floor", new Float3(0, -0.5f, 0), new Float3(20, 1, 20));

        GameObject volumeGo = CreateGameObject("Hole");
        scene.Add(volumeGo);
        volumeGo.Transform.Position = new Float3(0, 0, 0);
        var volume = volumeGo.AddComponent<NavMeshModifierVolume>();
        volume.Size = new Float3(4, 3, 4);
        volume.Area = NavMeshAreas.NotWalkable;

        Assert.True(AddSurface(scene).BuildNavMesh());

        Assert.Equal(0, SampleAreaMask(scene, new Float3(0, 0.2f, 0)));
        Assert.NotEqual(0, SampleAreaMask(scene, new Float3(7, 0.2f, 7)));

        // A path across the hole routes around it: some corner deviates from the straight line.
        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
        double maxDeviation = 0;
        foreach (Float3 corner in path.Corners)
            maxDeviation = System.Math.Max(maxDeviation, System.Math.Abs(corner.Z));
        Assert.True(maxDeviation > 1.5, $"Path should route around the hole (max |z| = {maxDeviation:0.00}).");
    }

    /// <summary>A rotated volume marks its rotated footprint, not its axis-aligned box: a
    /// 45°-yawed square's corner regions stay unmarked while its center is marked.</summary>
    [Fact]
    public void ModifierVolume_RotatedFootprint_IsHonored()
    {
        Scene scene = CreateScene(enable: true);
        AddFloorBox(scene, "Floor", new Float3(0, -0.5f, 0), new Float3(24, 1, 24));

        GameObject volumeGo = CreateGameObject("Diamond");
        scene.Add(volumeGo);
        volumeGo.Transform.Position = new Float3(0, 0, 0);
        volumeGo.Transform.Rotation = Quaternion.FromEuler(new Float3(0, 45, 0));
        var volume = volumeGo.AddComponent<NavMeshModifierVolume>();
        volume.Size = new Float3(8, 3, 8); // rotated 45°: a diamond with tips at ±5.66 on the axes
        volume.Area = Mud;

        Assert.True(AddSurface(scene).BuildNavMesh());

        // Center: inside the diamond.
        Assert.Equal(1 << Mud, SampleAreaMask(scene, new Float3(0, 0.2f, 0)));
        // The axis-aligned corner (4.5, 4.5) is OUTSIDE the diamond (|x|+|z| = 9 > 5.66) but
        // inside the unrotated box's AABB — marked only if rotation were ignored.
        Assert.Equal(1 << NavMeshAreas.Walkable, SampleAreaMask(scene, new Float3(4.5f, 0.2f, 4.5f)));
    }

    // ── Partial rebuilds ────────────────────────────────────────────────

    /// <summary>The collector-based RebuildTiles picks up modifier volumes automatically:
    /// spawning and removing a Not Walkable volume at runtime opens and closes the hole.</summary>
    [Fact]
    public void RebuildTiles_AppliesAndRemovesVolume()
    {
        Scene scene = CreateScene(enable: true);
        AddFloorBox(scene, "Floor", new Float3(0, -0.5f, 0), new Float3(20, 1, 20));
        NavMeshSurface surface = AddSurface(scene);
        Assert.True(surface.BuildNavMesh());
        Assert.NotEqual(0, SampleAreaMask(scene, new Float3(0, 0.2f, 0)));

        // Spawn a hole mid-game.
        GameObject volumeGo = CreateGameObject("Hole");
        scene.Add(volumeGo);
        volumeGo.Transform.Position = new Float3(0, 0, 0);
        var volume = volumeGo.AddComponent<NavMeshModifierVolume>();
        volume.Size = new Float3(4, 3, 4);
        volume.Area = NavMeshAreas.NotWalkable;

        var region = new AABB(new Float3(-3, -1, -3), new Float3(3, 2, 3));
        Assert.True(surface.RebuildTiles(region));
        Assert.Equal(0, SampleAreaMask(scene, new Float3(0, 0.2f, 0)));
        Assert.NotEqual(0, SampleAreaMask(scene, new Float3(7, 0.2f, 7)));

        // Remove it and rebuild the same region: walkability returns.
        volumeGo.Enabled = false;
        Assert.True(surface.RebuildTiles(region));
        Assert.NotEqual(0, SampleAreaMask(scene, new Float3(0, 0.2f, 0)));
    }

    /// <summary>Modifiers resolve during rebuild collection too: toggling IgnoreFromBuild on
    /// an obstacle and rebuilding its region updates the mesh.</summary>
    [Fact]
    public void RebuildTiles_HonorsModifierChanges()
    {
        Scene scene = CreateScene(enable: true);
        AddFloorBox(scene, "Floor", new Float3(0, -0.5f, 0), new Float3(20, 1, 20));
        // A wall segment initially ignored by the bake (a ghost preview, say).
        GameObject wall = AddFloorBox(scene, "Wall", new Float3(0, 2, 0), new Float3(2, 4, 20));
        var modifier = wall.AddComponent<NavMeshModifier>();
        modifier.IgnoreFromBuild = true;

        NavMeshSurface surface = AddSurface(scene);
        Assert.True(surface.BuildNavMesh());
        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);

        // The wall becomes real: stop ignoring it and rebuild its region.
        modifier.IgnoreFromBuild = false;
        Assert.True(surface.RebuildTiles(new AABB(new Float3(-2, -1, -11), new Float3(2, 5, 11))));

        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathPartial, path.Status);
    }

    // ── Not Walkable source parity ──────────────────────────────────────

    /// <summary>A source marked Not Walkable produces no navmesh at all (Unity parity): its
    /// geometry is an obstacle, not traversable "area 1" polys.</summary>
    [Fact]
    public void Source_NotWalkableArea_ProducesNoPolys()
    {
        Float3[] verts = [new(0, 0, 0), new(0, 0, 10), new(10, 0, 10), new(10, 0, 0)];
        int[] indices = [0, 1, 2, 0, 2, 3];
        var source = new NavMeshGeometrySource(verts, indices, Float4x4.Identity, NavMeshAreas.NotWalkable);

        var settings = new NavMeshBuildSettings { OverrideVoxelSize = true, VoxelSize = 0.25f };
        Assert.Null(NavMeshBuilder.Build(settings, [source]));
    }
}
