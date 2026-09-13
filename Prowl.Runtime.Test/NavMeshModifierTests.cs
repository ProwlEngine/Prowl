// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshModifier"/> and <see cref="NavMeshModifierVolume"/> at the level they
/// actually operate: <see cref="NavMeshGeometryCollector"/>'s output, since both are resolved at
/// collection time rather than by touching a baked asset. Both surface as a
/// <see cref="NavMeshAreaVolumeInput"/> now, not a per-triangle tag - Recast's own tiled pipeline paints
/// areas as convex volumes, not per input triangle. One end-to-end test confirms a volume's
/// <see cref="NavMeshAreas.NotWalkable"/> override actually punches a hole through a full bake.
/// </summary>
public class NavMeshModifierTests : RuntimeTestBase
{
    private static Mesh CreateQuad(float width, float depth)
    {
        float hw = width * 0.5f;
        float hd = depth * 0.5f;
        return new Mesh
        {
            Vertices = [new(-hw, 0, -hd), new(hw, 0, -hd), new(hw, 0, hd), new(-hw, 0, hd)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
    }

    private GameObject AddGround(Scene scene, string name, Float3 position)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        go.AddComponent<MeshRenderer>().Mesh = CreateQuad(10, 10);
        scene.Add(go);
        return go;
    }

    [Fact]
    public void IgnoreFromBuild_ExcludesThatObjectsGeometryOnly()
    {
        Scene scene = CreateScene(enable: true);
        AddGround(scene, "Included", new Float3(0, 0, 0));
        GameObject excluded = AddGround(scene, "Excluded", new Float3(100, 0, 0));
        excluded.AddComponent<NavMeshModifier>().IgnoreFromBuild = true;

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);

        Assert.Equal(2, input.TriangleCount); // one quad's worth, not two
    }

    [Fact]
    public void OverrideArea_StampsTheRequestedAreaOnItsOwnGeometry()
    {
        Scene scene = CreateScene(enable: true);
        GameObject ground = AddGround(scene, "Ground", Float3.Zero);
        var modifier = ground.AddComponent<NavMeshModifier>();
        modifier.OverrideArea = true;
        modifier.Area = 5;

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);

        Assert.Contains(input.AreaVolumes, v => v.Area == 5);
    }

    [Fact]
    public void ApplyToChildren_AppliesAnAncestorsOverrideToAChildWithNoModifierOfItsOwn()
    {
        Scene scene = CreateScene(enable: true);
        var parent = CreateGameObject("Parent");
        scene.Add(parent);
        var parentModifier = parent.AddComponent<NavMeshModifier>();
        parentModifier.OverrideArea = true;
        parentModifier.Area = 5;
        parentModifier.ApplyToChildren = true;

        var child = CreateGameObject("Child");
        child.AddComponent<MeshRenderer>().Mesh = CreateQuad(10, 10);
        child.Transform.SetParent(parent.Transform);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);

        Assert.Contains(input.AreaVolumes, v => v.Area == 5);
    }

    [Fact]
    public void OwnModifier_WinsOverAnAncestorsOverride()
    {
        Scene scene = CreateScene(enable: true);
        var parent = CreateGameObject("Parent");
        scene.Add(parent);
        var parentModifier = parent.AddComponent<NavMeshModifier>();
        parentModifier.OverrideArea = true;
        parentModifier.Area = 5;
        parentModifier.ApplyToChildren = true;

        var child = CreateGameObject("Child");
        child.AddComponent<MeshRenderer>().Mesh = CreateQuad(10, 10);
        var childModifier = child.AddComponent<NavMeshModifier>();
        childModifier.OverrideArea = true;
        childModifier.Area = 7;
        child.Transform.SetParent(parent.Transform);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);

        Assert.Contains(input.AreaVolumes, v => v.Area == 7);
        Assert.DoesNotContain(input.AreaVolumes, v => v.Area == 5);
    }

    [Fact]
    public void ModifierScopedToAnotherAgentType_IsTransparent()
    {
        Scene scene = CreateScene(enable: true);
        GameObject ground = AddGround(scene, "Ground", Float3.Zero);
        var modifier = ground.AddComponent<NavMeshModifier>();
        modifier.OverrideArea = true;
        modifier.Area = 5;
        modifier.AffectAllAgentTypes = false;
        modifier.AgentTypeIds.Add(999);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything, NavMeshAgentTypes.HumanoidId);

        Assert.DoesNotContain(input.AreaVolumes, v => v.Area == 5);
    }

    [Fact]
    public void ModifierVolume_OverridesAreaOfGeometryInsideIt()
    {
        Scene scene = CreateScene(enable: true);
        AddGround(scene, "Ground", Float3.Zero);

        var volumeGo = CreateGameObject("Volume");
        volumeGo.Transform.Position = Float3.Zero;
        scene.Add(volumeGo);
        var volume = volumeGo.AddComponent<NavMeshModifierVolume>();
        volume.Size = new Float3(20, 4, 20); // comfortably covers the whole 10x10 quad
        volume.Area = NavMeshAreas.Jump;

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);

        Assert.Contains(input.AreaVolumes, v => v.Area == NavMeshAreas.Jump);
    }

    [Fact]
    public void ModifierVolume_NotWalkable_PunchesARealHoleThroughAFullBake()
    {
        Scene scene = CreateScene(enable: true);
        AddGround(scene, "Ground", Float3.Zero);

        var volumeGo = CreateGameObject("Volume");
        scene.Add(volumeGo);
        var volume = volumeGo.AddComponent<NavMeshModifierVolume>();
        volume.Size = new Float3(20, 4, 20);
        volume.Area = NavMeshAreas.NotWalkable;

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, NavMeshBakeSettings.Default);

        // Every triangle was marked not-walkable, so nothing survives to become a polygon at all.
        Assert.False(result.Success);
    }

    [Fact]
    public void AppliesTo_FalseWhenScopedToAnotherAgentType()
    {
        var modifier = CreateGameObject("Modifier").AddComponent<NavMeshModifier>();
        modifier.AffectAllAgentTypes = false;
        modifier.AgentTypeIds.Add(42);

        Assert.False(modifier.AppliesTo(NavMeshAgentTypes.HumanoidId));
        Assert.True(modifier.AppliesTo(42));

        var volume = CreateGameObject("Volume").AddComponent<NavMeshModifierVolume>();
        volume.AffectAllAgentTypes = false;
        volume.AgentTypeIds.Add(42);

        Assert.False(volume.AppliesTo(NavMeshAgentTypes.HumanoidId));
        Assert.True(volume.AppliesTo(42));
    }

    [Fact]
    public void ModifierVolume_Contains_HonoursRotationAndScale()
    {
        var go = CreateGameObject("Volume");
        go.Transform.Position = new Float3(10, 0, 0);
        go.Transform.Rotation = Quaternion.FromEuler(new Float3(0, 90, 0));
        go.Transform.LocalScale = new Float3(2, 1, 1);
        var volume = go.AddComponent<NavMeshModifierVolume>();
        volume.Size = new Float3(2, 2, 2); // local half-extent 1, scaled by 2 in local X -> world Z after the 90 deg yaw

        // A point 1.5 world units along +Z from the volume's center, after a 90 degree yaw around Y,
        // corresponds to the volume's local +X axis - within its scaled half-extent (2), not the
        // unscaled one (1).
        Assert.True(volume.Contains(new Float3(10, 0, 1.5f)));
        Assert.False(volume.Contains(new Float3(10, 0, 3f)));
    }
}
