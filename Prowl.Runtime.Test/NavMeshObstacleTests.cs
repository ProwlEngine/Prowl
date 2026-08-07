// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// NavMeshObstacle carving: obstacles carve holes that incremental updates apply across frames,
/// carve-only-stationary lifts the carve while moving, and the non-carving mode blocks agents by
/// local avoidance instead. Also covers the bake side that carving depends on — cache layers
/// query like finished tiles and round-trip through the asset.
/// </summary>
public class NavMeshObstacleTests : RuntimeTestBase
{
    private static bool Walkable(Scene scene, Float3 position)
        => scene.Navigation.SamplePosition(position, out _, 0.5f, NavMesh.AllAreas);

    /// <summary>A bake produces cache layers, is queryable, and the layers survive an Echo
    /// round-trip and re-instantiation.</summary>
    [Fact]
    public void Surface_BakesQueriesAndRoundTrips()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        Runtime.NavMeshData data = surface.NavMeshData.Res!;
        Assert.NotEmpty(data.CacheLayers);

        Assert.True(Walkable(scene, new Float3(0, 0.2f, 0)));
        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, -8), new Float3(8, 0, 8), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);

        // Layers round-trip through the asset serializer and instantiate again.
        EchoObject echo = Serializer.Serialize(data);
        Runtime.NavMeshData? loaded = Serializer.Deserialize<Runtime.NavMeshData>(echo);
        Assert.NotNull(loaded);
        Assert.Equal(data.CacheLayers.Count, loaded!.CacheLayers.Count);
        var cache = loaded.CreateTileCache(maxObstacles: 16);
        Assert.NotNull(cache.GetNavMesh());
        Assert.True(cache.GetNavMesh().GetMaxTiles() > 0);
    }

    /// <summary>
    /// A surface with NOTHING overridden has to produce a usable navmesh. A tile wider than a
    /// compressed layer header can describe (its dimensions are bytes) wraps to an empty layer:
    /// the bake reports success and every tile comes out with no polygons — no gizmo, no agent
    /// placement, no movement. Every other test here overrides the tile size, which is exactly
    /// why that went unseen once.
    /// </summary>
    [Fact]
    public void Bake_WithDefaultSettings_ProducesQueryableMesh()
    {
        Scene scene = CreateScene(enable: true);
        GameObject floor = CreateGameObject("Floor");
        scene.Add(floor);
        floor.AddComponent<BoxCollider>().Size = new Float3(40, 1, 40);
        floor.Transform.Position = new Float3(0, -0.5f, 0);

        GameObject surfaceGo = CreateGameObject("NavMeshSurface");
        scene.Add(surfaceGo);
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        surface.UseGeometry = NavMeshCollectGeometry.PhysicsColliders;

        Assert.True(surface.BuildNavMesh());
        Tick(scene, 2);

        Assert.True(Walkable(scene, new Float3(0, 0.2f, 0)), "A default bake must be queryable.");
        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(-15, 0, -15), new Float3(15, 0, 15), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
        // The editor overlay reads this: empty layers drew nothing, which is how it looked unbaked.
        Assert.NotEmpty(surface.NavMeshData.Res!.CalculateTriangulation().Vertices);
    }

    /// <summary>An explicit oversized tile size is clamped rather than silently producing an
    /// empty mesh, and the asset records the size actually used — the TileCache instantiated
    /// from it later reads those same settings.</summary>
    [Fact]
    public void Bake_ClampsTileSizeToLayerFormatLimit()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene(40f);
        surface.BuildOverrides.OverrideTileSize = true;
        surface.BuildOverrides.TileSize = 1024;

        Assert.True(surface.BuildNavMesh());
        Tick(scene, 2);

        Assert.Equal(NavMeshBuildSettings.MaxTileSize, surface.NavMeshData.Res!.Settings.EffectiveTileSize);
        Assert.True(Walkable(scene, new Float3(0, 0.2f, 0)));
    }

    /// <summary>
    /// Carving costs nothing until something carves. A navmesh nobody has put an obstacle on is
    /// never handed to the per-frame pump at all — that is what lets every surface be
    /// carve-capable without every surface paying for it. A carve marks it, and convergence
    /// clears the mark again.
    /// </summary>
    [Fact]
    public void Surface_WithoutPendingWork_IsNotPumped()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        // Registration seeds every tile synchronously, so a fresh instance starts settled.
        NavMeshInstance instance = surface.Instance!;
        Assert.False(instance.CachePending);
        Tick(scene, 5);
        Assert.False(instance.CachePending);

        GameObject crate = CreateGameObject("Crate");
        scene.Add(crate);
        crate.Transform.Position = new Float3(0, 1, 0);
        crate.AddComponent<NavMeshObstacle>().Size = new Float3(4, 3, 4);
        Assert.True(instance.CachePending, "Queuing a carve must hand the instance to the pump.");

        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0);
        Assert.True(TickUntil(scene, () => !instance.CachePending) >= 0,
            "Once the carve has converged the instance should drop out of the pump again.");
    }

    /// <summary>An obstacle carves a hole (paths detour, the hole is unwalkable) and removing
    /// it restores the surface — all through incremental frame updates, no rebake.</summary>
    [Fact]
    public void Obstacle_CarvesHole_AndRemovalRestores()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());
        Tick(scene, 2);
        Assert.True(Walkable(scene, new Float3(0, 0.2f, 0)));

        GameObject obstacleGo = CreateGameObject("Crate");
        scene.Add(obstacleGo);
        obstacleGo.Transform.Position = new Float3(0, 1, 0);
        var obstacle = obstacleGo.AddComponent<NavMeshObstacle>();
        obstacle.Shape = NavMeshObstacleShape.Box;
        obstacle.Size = new Float3(4, 3, 4);

        // The obstacle sits on the 2x2 tile grid's crossing point, so the hole spans four
        // tiles that rebuild incrementally over several frames — wait until every quadrant of
        // the hole is carved, not just the first rebuilt tile.
        int carveTicks = TickUntil(scene, () =>
            !Walkable(scene, new Float3(1, 0.2f, 1)) && !Walkable(scene, new Float3(-1, 0.2f, 1))
            && !Walkable(scene, new Float3(1, 0.2f, -1)) && !Walkable(scene, new Float3(-1, 0.2f, -1)));
        Assert.True(carveTicks >= 0, "Obstacle should carve all affected tiles within the tick budget.");
        // Surrounding floor survives the carve.
        Assert.True(Walkable(scene, new Float3(7, 0.2f, 7)));

        // A path across detours around the hole.
        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
        double maxDeviation = 0;
        foreach (Float3 corner in path.Corners)
            maxDeviation = System.Math.Max(maxDeviation, System.Math.Abs(corner.Z));
        Assert.True(maxDeviation > 1.5, $"Path should route around the carved hole (max |z| = {maxDeviation:0.00}).");

        // Removal restores.
        obstacleGo.Enabled = false;
        int restoreTicks = TickUntil(scene, () => Walkable(scene, new Float3(0, 0.2f, 0)));
        Assert.True(restoreTicks >= 0, "Removing the obstacle should restore walkability.");
    }

    /// <summary>With CarveOnlyStationary, the carve lifts while the obstacle moves and
    /// re-applies after it has settled for CarvingTimeToStationary.</summary>
    [Fact]
    public void Obstacle_CarveOnlyStationary_LiftsWhileMoving()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        GameObject obstacleGo = CreateGameObject("Cart");
        scene.Add(obstacleGo);
        obstacleGo.Transform.Position = new Float3(0, 1, 0);
        var obstacle = obstacleGo.AddComponent<NavMeshObstacle>();
        obstacle.Shape = NavMeshObstacleShape.Box;
        obstacle.Size = new Float3(4, 3, 4);
        obstacle.CarveOnlyStationary = true;
        obstacle.CarvingTimeToStationary = 0.2f;

        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0,
            "A spawned-still obstacle should carve.");

        // Drag it along: each tick moves beyond the threshold, so the carve lifts and stays
        // lifted while in motion.
        for (int i = 0; i < 30; i++)
        {
            obstacleGo.Transform.Position += new Float3(0.3f, 0, 0);
            Tick(scene, 1);
        }
        Assert.True(TickUntil(scene, () => Walkable(scene, new Float3(0, 0.2f, 0))) >= 0,
            "The original spot should be restored once the obstacle moved away.");
        Assert.True(Walkable(scene, new Float3(obstacleGo.Transform.Position.X, 0.2f, 0)),
            "A moving obstacle should not carve.");

        // Settle: after the stationary time it carves at the new spot.
        Float3 rest = obstacleGo.Transform.Position;
        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(rest.X, 0.2f, 0))) >= 0,
            "A settled obstacle should carve at its new position.");
    }

    /// <summary>Flipping Carve off at runtime must remove the existing hole, not leave it
    /// carved forever (the non-carving mode is unsupported and does nothing).</summary>
    [Fact]
    public void Obstacle_CarveTurnedOff_RemovesExistingHole()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        GameObject obstacleGo = CreateGameObject("Ghost");
        scene.Add(obstacleGo);
        obstacleGo.Transform.Position = new Float3(0, 1, 0);
        var obstacle = obstacleGo.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(4, 3, 4);

        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0);

        obstacle.Carve = false;
        Assert.True(TickUntil(scene, () => Walkable(scene, new Float3(0, 0.2f, 0))) >= 0,
            "Turning Carve off should remove the hole.");

        obstacle.Carve = true;
        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0,
            "Turning Carve back on should re-carve.");
    }

    /// <summary>
    /// A yawed box carves its ORIENTED footprint. The box is deliberately non-square (long
    /// axis local Z), so a yaw-sign mismatch against Detour's convention would mirror the
    /// carve onto the other diagonal and fail both assertions.
    /// </summary>
    [Fact]
    public void Obstacle_RotatedBox_CarvesOrientedFootprint()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        GameObject obstacleGo = CreateGameObject("Barrier");
        scene.Add(obstacleGo);
        obstacleGo.Transform.Position = new Float3(0, 1, 0);
        obstacleGo.Transform.Rotation = Quaternion.FromEuler(new Float3(0, 45, 0));
        var obstacle = obstacleGo.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(1.5f, 3, 7); // long axis local Z, yawed 45°

        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0,
            "The rotated obstacle should carve at its center.");

        // Which world diagonal the long axis lies along after +45° yaw — measured from the
        // Transform so the assertion tracks Prowl's yaw convention rather than assuming it.
        Float3 along = obstacleGo.Transform.Forward;
        var farAlong = new Float3((float)(along.X * 2.5f), 0.2f, (float)(along.Z * 2.5f));
        var farAcross = new Float3((float)(-along.Z * 2.5f), 0.2f, (float)(along.X * 2.5f));

        // The far end of a long obstacle reaches tiles the centre does not, and the cache
        // rebuilds one tile per frame — waiting only for the centre samples the rest too early.
        Assert.True(TickUntil(scene, () => !Walkable(scene, farAlong)) >= 0,
            "The strip along the box's long axis should be carved.");
        Assert.True(Walkable(scene, farAcross),
            "Perpendicular to the long axis (outside the 1.5-wide footprint) should stay walkable.");
    }

    // ── Layer regeneration (geometry rebuilds) ─────────────────────────

    /// <summary>RebuildTiles regenerates the affected tiles' compressed layers: dropped geometry
    /// blocks paths, removal restores them — no rebake.</summary>
    [Fact]
    public void RebuildTiles_ReflectsGeometry()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);

        GameObject wall = CreateGameObject("Wall");
        scene.Add(wall);
        wall.AddComponent<BoxCollider>().Size = new Float3(2, 4, 20);
        wall.Transform.Position = new Float3(0, 2, 0);

        var region = new AABB(new Float3(-2, -1, -11), new Float3(2, 5, 11));
        Assert.True(surface.RebuildTiles(region));
        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathPartial, path.Status);

        wall.Enabled = false;
        Assert.True(surface.RebuildTiles(region));
        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }

    /// <summary>
    /// The stage-3b headline: an obstacle's carve survives regeneration of the tiles it sits
    /// in. Tile replacement invalidates the refs in every obstacle's touched list (salt
    /// bump); without the refresh step the regenerated tiles rebuild WITHOUT their carves.
    /// </summary>
    [Fact]
    public void LayerRegeneration_PreservesExistingCarves()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        // Carve a hole away from the wall line but inside the tile the wall rebuild touches.
        GameObject obstacleGo = CreateGameObject("Crate");
        scene.Add(obstacleGo);
        obstacleGo.Transform.Position = new Float3(4, 1, 4);
        var obstacle = obstacleGo.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(3, 3, 3);
        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(4, 0.2f, 4))) >= 0);

        // Destructible-world event: a wall drops and its tiles regenerate.
        GameObject wall = CreateGameObject("Wall");
        scene.Add(wall);
        wall.AddComponent<BoxCollider>().Size = new Float3(2, 4, 20);
        wall.Transform.Position = new Float3(0, 2, 0);
        Assert.True(surface.RebuildTiles(new AABB(new Float3(-2, -1, -11), new Float3(2, 5, 11))));

        // The new geometry is in...
        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathPartial, path.Status);
        // ...AND the carve survived the regeneration of its tile.
        Assert.False(Walkable(scene, new Float3(4, 0.2f, 4)),
            "The obstacle's carve must survive layer regeneration of its tile.");
        // Sanity: the floor between wall and carve is still there.
        Assert.True(Walkable(scene, new Float3(4, 0.2f, -4)));

        // The obstacle still owns its carve: removal restores through the incremental path.
        obstacleGo.Enabled = false;
        Assert.True(TickUntil(scene, () => Walkable(scene, new Float3(4, 0.2f, 4))) >= 0,
            "The carve must remain removable after regeneration (live obstacle refs intact).");
    }

    /// <summary>
    /// An obstacle STRADDLING a regenerated tile and an untouched one: the refresh must
    /// produce a mixed touched list (new ref for the regenerated tile + surviving ref for the
    /// untouched one). A subtle refresh bug keeps half the carve — or loses half on removal.
    /// </summary>
    [Fact]
    public void LayerRegeneration_ObstacleSpanningRebuiltAndUntouchedTiles()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        // Tile columns split at x = 6 (origin -10, tile world size 16). The crate straddles it.
        GameObject obstacleGo = CreateGameObject("Crate");
        scene.Add(obstacleGo);
        obstacleGo.Transform.Position = new Float3(6, 1, 0);
        var obstacle = obstacleGo.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(4, 3, 4); // carve x 4..8, both sides of the seam
        Assert.True(TickUntil(scene, () =>
            !Walkable(scene, new Float3(5, 0.2f, 0)) && !Walkable(scene, new Float3(7, 0.2f, 0))) >= 0);

        // Regenerate only the LEFT tile column (wall far from the seam).
        GameObject wall = CreateGameObject("Wall");
        scene.Add(wall);
        wall.AddComponent<BoxCollider>().Size = new Float3(2, 4, 20);
        wall.Transform.Position = new Float3(-6, 2, 0);
        Assert.True(surface.RebuildTiles(new AABB(new Float3(-8, -1, -11), new Float3(-4, 5, 11))));

        // Both halves of the carve survive: the regenerated-tile half re-applied, the
        // untouched-tile half undisturbed.
        Assert.False(Walkable(scene, new Float3(5, 0.2f, 0)), "Carve half in the regenerated tile must survive.");
        Assert.False(Walkable(scene, new Float3(7, 0.2f, 0)), "Carve half in the untouched tile must survive.");

        // Removal restores BOTH halves — the mixed touched list must hold the new ref and
        // the surviving ref.
        obstacleGo.Enabled = false;
        Assert.True(TickUntil(scene, () =>
            Walkable(scene, new Float3(5, 0.2f, 0)) && Walkable(scene, new Float3(7, 0.2f, 0))) >= 0,
            "Removing the spanning obstacle must restore both sides of the seam.");
    }

    /// <summary>Regenerated layers replace the asset's blobs (a later save/instantiate agrees
    /// with the live mesh), and the mutated asset still round-trips and re-instantiates.</summary>
    [Fact]
    public void LayerRegeneration_MirrorsIntoAsset()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());
        Runtime.NavMeshData data = surface.NavMeshData.Res!;
        var blobsBefore = new System.Collections.Generic.Dictionary<(int, int), byte[]>();
        foreach (Runtime.NavMeshData.NavMeshTile layer in data.CacheLayers)
            blobsBefore[(layer.X, layer.Z)] = layer.Data;

        GameObject wall = CreateGameObject("Wall");
        scene.Add(wall);
        wall.AddComponent<BoxCollider>().Size = new Float3(2, 4, 20);
        wall.Transform.Position = new Float3(0, 2, 0);
        Assert.True(surface.RebuildTiles(new AABB(new Float3(-2, -1, -11), new Float3(2, 5, 11))));

        bool anyReplaced = false;
        foreach (Runtime.NavMeshData.NavMeshTile layer in data.CacheLayers)
            if (blobsBefore.TryGetValue((layer.X, layer.Z), out byte[]? before) && !ReferenceEquals(before, layer.Data))
                anyReplaced = true;
        Assert.True(anyReplaced, "Affected tiles' blobs should be replaced in the asset.");

        EchoObject echo = Serializer.Serialize(data);
        Runtime.NavMeshData? loaded = Serializer.Deserialize<Runtime.NavMeshData>(echo);
        Assert.NotNull(loaded);
        var cache = loaded!.CreateTileCache(maxObstacles: 16);
        Assert.True(cache.GetNavMesh().GetMaxTiles() > 0);
    }

    /// <summary>The async pair: layers regenerate off the main thread and apply as a swap.</summary>
    [Fact]
    public void RebuildTilesAsync_AppliesLikeSyncPath()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        GameObject wall = CreateGameObject("Wall");
        scene.Add(wall);
        wall.AddComponent<BoxCollider>().Size = new Float3(2, 4, 20);
        wall.Transform.Position = new Float3(0, 2, 0);

        var region = new AABB(new Float3(-2, -1, -11), new Float3(2, 5, 11));
        var rebuilt = surface.RebuildTilesAsync(region, surface.CollectSources()).Result;
        Assert.NotEmpty(rebuilt);
        Assert.True(surface.ApplyRebuiltTiles(rebuilt, out int rebuiltTiles));
        Assert.True(rebuiltTiles > 0);

        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathPartial, path.Status);
    }

    /// <summary>
    /// A crowd agent routes around a carve rather than walking through it — the two halves of
    /// the feature (carving, crowd) had no test that exercised them together.
    /// </summary>
    [Fact]
    public void Agent_PathsAroundCarve()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        GameObject obstacleGo = CreateGameObject("Crate");
        scene.Add(obstacleGo);
        obstacleGo.Transform.Position = new Float3(0, 1, 0);
        var obstacle = obstacleGo.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(4, 3, 4); // straddles the straight line across
        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0);

        GameObject agentGo = CreateGameObject("Walker");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, 0);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 6f;
        agent.Acceleration = 100f;
        agent.Separation = false;
        Tick(scene, 2);

        Assert.True(agent.IsOnNavMesh, "The agent should place on a TileCache-built navmesh.");
        Assert.True(agent.SetDestination(new Float3(8, 0, 0)));
        Tick(scene, 5);
        Assert.Equal(NavMeshPathStatus.PathComplete, agent.PathStatus);

        bool arrived = false;
        for (int i = 0; i < 600 && !arrived; i++)
        {
            Tick(scene, 1);
            Float3 p = agentGo.Transform.Position;
            Assert.False(System.Math.Abs(p.X) < 1.8 && System.Math.Abs(p.Z) < 1.8,
                $"The agent walked into the carved hole at {p}.");
            arrived = p.X > 7;
        }
        Assert.True(arrived, "The agent should reach the far side by routing around the carve.");
    }

    /// <summary>
    /// A bake must not voxelize a carving obstacle's own geometry: the carve is the hole. Baking
    /// it too freezes a second hole in the mesh that stays behind forever once the obstacle
    /// moves — the carve follows, the baked one does not.
    /// </summary>
    [Fact]
    public void Bake_ExcludesCarvingObstacleGeometry()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();

        // Present at bake time, and short enough that no agent fits under it — so if its
        // geometry were collected it really would cut the floor.
        GameObject crate = CreateGameObject("Crate");
        scene.Add(crate);
        crate.Transform.Position = new Float3(6, 0.6f, 6);
        crate.AddComponent<BoxCollider>().Size = new Float3(4, 1.2f, 4);
        var obstacle = crate.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(4, 1.2f, 4);
        obstacle.CarvingTimeToStationary = 0.1f;

        Assert.True(surface.BuildNavMesh());
        Assert.Single(surface.CollectSources()); // the floor only — the crate is not collected

        // The hole is there — carved, not baked.
        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(6, 0.2f, 6))) >= 0,
            "The obstacle should carve at its resting position.");

        crate.Transform.Position = new Float3(-6, 0.6f, -6);
        Assert.True(TickUntil(scene, () => Walkable(scene, new Float3(6, 0.2f, 6))) >= 0,
            "The old spot must heal — nothing about the obstacle should be baked into the mesh.");
        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(-6, 0.2f, -6))) >= 0,
            "The carve should follow the obstacle to its new resting position.");
    }

    /// <summary>Obstacles stay out of bakes in both carve settings — they block at runtime,
    /// from wherever the object actually is.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bake_ExcludesObstacleGeometry(bool carve)
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();

        GameObject crate = CreateGameObject("Crate");
        scene.Add(crate);
        crate.Transform.Position = new Float3(6, 0.6f, 6);
        crate.AddComponent<BoxCollider>().Size = new Float3(4, 1.2f, 4);
        var obstacle = crate.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(4, 1.2f, 4);
        obstacle.Carve = carve;

        Assert.Single(surface.CollectSources()); // the floor only
    }

    /// <summary>
    /// Exclusion is by presence, not enabled state — deliberately, so a bake can never depend
    /// on when a component was last toggled. A disabled obstacle that bakes its geometry would
    /// freeze a hole at that spot, and enabling it later and moving it would strand that hole
    /// forever while the runtime blocking correctly follows.
    /// </summary>
    [Fact]
    public void Bake_ExcludesObstacleGeometry_EvenWhileDisabled()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();

        GameObject crate = CreateGameObject("Crate");
        scene.Add(crate);
        crate.Transform.Position = new Float3(6, 0.6f, 6);
        crate.AddComponent<BoxCollider>().Size = new Float3(4, 1.2f, 4);
        crate.AddComponent<NavMeshObstacle>().Enabled = false;

        Assert.Single(surface.CollectSources()); // the floor only
    }

    /// <summary>A blocker that cannot be placed on the navmesh is an invisible failure — it must
    /// say so rather than sit there avoided by nobody.</summary>
    [Fact]
    public void NonCarvingObstacle_OffNavMesh_WarnsItCannotBlock()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene(40f);
        Assert.True(surface.BuildNavMesh());

        GameObject agentGo = CreateGameObject("Walker");
        scene.Add(agentGo);
        agentGo.AddComponent<NavMeshAgent>(); // creates the crowd a blocker would join
        Tick(scene, 2);

        List<string> warnings = [];
        void Capture(string message, DebugStackTrace? trace, LogSeverity severity)
        {
            if (severity == LogSeverity.Warning && message.Contains("FloatingCrate")) warnings.Add(message);
        }

        Debug.OnLog += Capture;
        try
        {
            GameObject crate = CreateGameObject("FloatingCrate");
            scene.Add(crate);
            crate.Transform.Position = new Float3(0, 60, 0); // far above any walkable surface
            var obstacle = crate.AddComponent<NavMeshObstacle>();
            obstacle.Carve = false;
            obstacle.Size = new Float3(2, 2, 2);

            Tick(scene, 30);
            Assert.Single(warnings); // once, not once per frame
            Assert.Contains("blocker", warnings[0]);
        }
        finally
        {
            Debug.OnLog -= Capture;
        }
    }

    /// <summary>
    /// Carving works outside play mode, because that is where you place buildings: OnEnable queues
    /// the carve in the editor, and the pump must process it rather than waiting for play.
    /// </summary>
    [Fact]
    public void Obstacle_CarvesAndFollowsOutsidePlayMode()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());
        Tick(scene, 2);

        bool wasPlaying = Application.IsPlaying;
        Application.IsPlaying = false; // gameplay callbacks stop; lifecycle and [ExecuteAlways] do not
        try
        {
            GameObject crate = CreateGameObject("Crate");
            scene.Add(crate);
            crate.Transform.Position = new Float3(0, 1, 0);
            var obstacle = crate.AddComponent<NavMeshObstacle>();
            obstacle.Size = new Float3(4, 3, 4);
            obstacle.CarvingTimeToStationary = 0.1f;

            Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0,
                "An obstacle placed in the editor should carve.");

            // And dragging it in the editor moves the hole, rather than stranding it.
            crate.Transform.Position = new Float3(6, 1, 6);
            Assert.True(TickUntil(scene, () => Walkable(scene, new Float3(0, 0.2f, 0))) >= 0,
                "The old spot should heal when the obstacle is dragged away.");
            Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(6, 0.2f, 6))) >= 0,
                "The carve should follow to where the obstacle was dropped.");
        }
        finally
        {
            Application.IsPlaying = wasPlaying;
        }
    }

    /// <summary>
    /// A carved hole has to keep agents off the obstacle the way a baked wall does. A navmesh
    /// records where an agent's CENTRE may be, not where its body fits — a bake pulls the mesh
    /// back from every wall by the agent radius — so a hole cut to the obstacle's exact
    /// footprint let agents walk their centre onto its surface and stand half inside it. On a
    /// 10x10 floor the baked edge sits a full radius in from the rim; a carve must match.
    /// </summary>
    [Fact]
    public void Carve_KeepsAgentsAFullRadiusClear()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());
        Tick(scene, 2);

        GameObject crate = CreateGameObject("Crate");
        scene.Add(crate);
        crate.Transform.Position = new Float3(0, 1, 0);
        crate.AddComponent<NavMeshObstacle>().Size = new Float3(2, 3, 2); // spans -1..1 in X and Z
        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0);
        Tick(scene, 30);

        GameObject go = CreateGameObject("Walker");
        scene.Add(go);
        go.Transform.Position = new Float3(-6, 0, 0);
        var agent = go.AddComponent<NavMeshAgent>();
        agent.Speed = 3f;
        agent.Acceleration = 100f;
        Tick(scene, 2);
        Assert.True(agent.SetDestination(new Float3(6, 0, 0))); // straight through the obstacle

        double closest = double.MaxValue;
        for (int i = 0; i < 500; i++)
        {
            Tick(scene, 1);
            Float3 p = agent.Transform.Position;
            double dx = Math.Max(Math.Abs(p.X) - 1.0, 0);
            double dz = Math.Max(Math.Abs(p.Z) - 1.0, 0);
            closest = Math.Min(closest, Math.Sqrt(dx * dx + dz * dz));
            if (!agent.PathPending && agent.RemainingDistance <= 0f) break;
        }

        Assert.True(closest >= agent.Radius * 0.9,
            $"Agent centre came within {closest:0.000} of the obstacle surface, inside its own {agent.Radius} radius.");
    }

    /// <summary>
    /// A carve has to ANNOUNCE itself. The scene-view overlay caches its triangulation and rebuilds
    /// it when the world reports a change, so a carve nobody is told about leaves the overlay
    /// drawing intact floor over a real hole. Anything queued into a cache reports every frame it
    /// works AND on the frame it finishes — a carve small enough to complete inside one cache
    /// update would otherwise never announce at all.
    /// </summary>
    [Fact]
    public void Carve_FinishingInOneUpdate_StillReportsTheChange()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        surface.AlwaysShowNavMesh = true;
        Assert.True(surface.BuildNavMesh());
        Tick(scene, 4); // settle, so nothing else is pending

        int changes = 0;
        void Count() => changes++;
        scene.Navigation.NavMeshChanged += Count;
        try
        {
            // Small enough to land in one tile, which is what makes the cache converge at once.
            GameObject crate = CreateGameObject("Crate");
            scene.Add(crate);
            crate.Transform.Position = new Float3(3, 1, 3);
            crate.AddComponent<NavMeshObstacle>().Size = new Float3(2, 3, 2);

            Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(3, 0.2f, 3))) >= 0,
                "The obstacle should carve.");
            Assert.True(changes > 0, "A carve must raise NavMeshChanged, or the overlay never redraws.");

            // And once it has settled the world goes quiet again, so the overlay is not
            // re-triangulated every frame for the rest of the session.
            Tick(scene, 5);
            changes = 0;
            Tick(scene, 10);
            Assert.Equal(0, changes);
        }
        finally
        {
            scene.Navigation.NavMeshChanged -= Count;
        }
    }

    /// <summary>
    /// What the scene-view overlay draws must show the carve, or there is no way to tell a real
    /// hole from an agent merely steering around something. The live triangulation reflects it;
    /// the asset's does not, and cannot — obstacles are runtime state and never serialize — so
    /// the overlay has to be reading the live navmesh whenever one is registered.
    /// </summary>
    [Fact]
    public void LiveTriangulation_ShowsTheCarve()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());
        Tick(scene, 2);

        NavMeshTriangulation before = scene.Navigation.CalculateTriangulation(surface.AgentTypeId);
        Assert.NotEmpty(before.Vertices);

        GameObject crate = CreateGameObject("Crate");
        scene.Add(crate);
        crate.Transform.Position = new Float3(0, 1, 0);
        crate.AddComponent<NavMeshObstacle>().Size = new Float3(6, 3, 6);
        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0);

        NavMeshTriangulation after = scene.Navigation.CalculateTriangulation(surface.AgentTypeId);
        int inside = 0;
        for (int t = 0; t < after.Areas.Length; t++)
        {
            Float3 centroid = (after.Vertices[after.Indices[t * 3 + 0]]
                + after.Vertices[after.Indices[t * 3 + 1]]
                + after.Vertices[after.Indices[t * 3 + 2]]) / 3f;
            if (Math.Abs(centroid.X) < 2 && Math.Abs(centroid.Z) < 2) inside++;
        }
        Assert.Equal(0, inside); // the hole is a hole in the drawn mesh too
        Assert.NotEqual(before.Areas.Length, after.Areas.Length);
    }

    // ── Velocity obstacles (Carve off) ──────────────────────────────────

    /// <summary>
    /// Carve off is Unity's velocity-obstacle mode, not a disabled component: the mesh is
    /// untouched (the path still leads straight through) but agents steer around the obstacle
    /// locally. It joins the crowd as an immovable neighbour, so it works on Static surfaces
    /// too, where carving is impossible.
    /// </summary>
    [Fact]
    public void NonCarvingObstacle_DeflectsAgent_WithoutTouchingMesh()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene(40f);
        Assert.True(surface.BuildNavMesh());

        GameObject crate = CreateGameObject("Cart");
        scene.Add(crate);
        crate.Transform.Position = new Float3(0, 1, 0); // straight across the agent's route
        var obstacle = crate.AddComponent<NavMeshObstacle>();
        obstacle.Carve = false;
        obstacle.Size = new Float3(3, 2, 3);

        // The mesh is untouched: the straight line is still walkable and still the path.
        Tick(scene, 2);
        Assert.True(Walkable(scene, new Float3(0, 0.2f, 0)));

        GameObject agentGo = CreateGameObject("Walker");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-10, 0, 0);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 4f;
        agent.Acceleration = 20f;
        agent.Separation = false;
        agent.Radius = 0.5f;
        Tick(scene, 2);
        Assert.True(agent.SetDestination(new Float3(10, 0, 0)));

        double closest = double.MaxValue, maxLateral = 0;
        bool arrived = false;
        for (int i = 0; i < 900 && !arrived; i++)
        {
            Tick(scene, 1);
            Float3 p = agentGo.Transform.Position;
            closest = Math.Min(closest, Float3.Distance(p, new Float3(0, p.Y, 0)));
            maxLateral = Math.Max(maxLateral, Math.Abs(p.Z));
            arrived = p.X > 9;
        }

        Assert.True(arrived, "The agent should still reach the far side.");
        Assert.True(maxLateral > 0.5, $"The agent should have been pushed off the straight line (max |z| = {maxLateral:0.00}).");
        Assert.True(closest > 0.9, $"The agent should not have walked through the obstacle (closest approach {closest:0.00}).");
    }

    /// <summary>
    /// The mode's reason for existing: an obstacle that moves. A blocker has no steering of its
    /// own, so it only tracks the Transform because the component writes its position each
    /// frame — and this costs no rebuild at all, unlike carving, which lifts and re-cuts the
    /// mesh on every move.
    /// </summary>
    [Fact]
    public void NonCarvingObstacle_FollowsAMovingObstacle()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene(40f);
        Assert.True(surface.BuildNavMesh());

        GameObject agentGo = CreateGameObject("Walker");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-10, 0, 0);
        agentGo.AddComponent<NavMeshAgent>();
        Tick(scene, 2);

        GameObject cart = CreateGameObject("Cart");
        scene.Add(cart);
        cart.Transform.Position = new Float3(0, 1, 0);
        var obstacle = cart.AddComponent<NavMeshObstacle>();
        obstacle.Carve = false;
        obstacle.Size = new Float3(2, 2, 2);
        Tick(scene, 2);

        for (int i = 0; i < 40; i++)
        {
            cart.Transform.Position += new Float3(0.2f, 0, 0.1f);
            Tick(scene, 1);
        }

        Float3 expected = cart.Transform.Position;
        Prowl.Recast.Detour.Crowd.DtCrowd crowd = scene.Navigation.NativeCrowd!;
        foreach (Prowl.Recast.Detour.Crowd.DtCrowdAgent a in crowd.GetActiveAgents())
        {
            if (!ReferenceEquals(a.option.userData, obstacle)) continue;
            Assert.True(Math.Abs(a.npos.X - expected.X) < 0.05f && Math.Abs(a.npos.Z - expected.Z) < 0.05f,
                $"The blocker sat at ({a.npos.X:0.00}, {a.npos.Z:0.00}) while the obstacle moved to ({expected.X:0.00}, {expected.Z:0.00}).");
            return;
        }
        Assert.Fail("The obstacle registered no blocker with the crowd.");
    }

    /// <summary>The blocker holds its ground rather than being shoved aside by the traffic it
    /// exists to deflect.</summary>
    [Fact]
    public void NonCarvingObstacle_StaysPutUnderPressure()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene(40f);
        Assert.True(surface.BuildNavMesh());

        GameObject crate = CreateGameObject("Cart");
        scene.Add(crate);
        crate.Transform.Position = new Float3(0, 1, 0);
        var obstacle = crate.AddComponent<NavMeshObstacle>();
        obstacle.Carve = false;
        obstacle.Size = new Float3(2, 2, 2);

        for (int i = 0; i < 6; i++)
        {
            GameObject go = CreateGameObject($"Pusher{i}");
            scene.Add(go);
            go.Transform.Position = new Float3(-8, 0, -2.5f + i);
            var pusher = go.AddComponent<NavMeshAgent>();
            pusher.Speed = 5f;
            pusher.Acceleration = 50f;
            pusher.Radius = 0.4f;
            Tick(scene, 1);
            pusher.SetDestination(new Float3(10, 0, 0));
        }

        Tick(scene, 300);
        Prowl.Recast.Detour.Crowd.DtCrowd crowd = scene.Navigation.NativeCrowd!;
        foreach (Prowl.Recast.Detour.Crowd.DtCrowdAgent a in crowd.GetActiveAgents())
        {
            if (!ReferenceEquals(a.option.userData, obstacle)) continue;
            Assert.True(Math.Abs(a.npos.X) < 0.05f && Math.Abs(a.npos.Z) < 0.05f,
                $"The blocker drifted to ({a.npos.X:0.00}, {a.npos.Z:0.00}) under crowd pressure.");
            return;
        }
        Assert.Fail("The obstacle registered no blocker with the crowd.");
    }

    /// <summary>Flipping Carve at runtime swaps modes cleanly: neither the hole nor the blocker
    /// is left behind by the other.</summary>
    [Fact]
    public void Obstacle_SwitchingCarveMode_LeavesNothingBehind()
    {
        (Scene scene, NavMeshSurface surface) = CreateFloorScene();
        Assert.True(surface.BuildNavMesh());

        GameObject agentGo = CreateGameObject("Walker");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, 0);
        agentGo.AddComponent<NavMeshAgent>(); // creates the crowd blockers join
        Tick(scene, 2);

        GameObject crate = CreateGameObject("Crate");
        scene.Add(crate);
        crate.Transform.Position = new Float3(0, 1, 0);
        var obstacle = crate.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(4, 3, 4);
        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0);
        Assert.Equal(1, BlockerCount(scene, obstacle)); // carving: no blocker

        obstacle.Carve = false;
        Assert.True(TickUntil(scene, () => Walkable(scene, new Float3(0, 0.2f, 0))) >= 0,
            "Switching to velocity mode must remove the hole.");
        Assert.Equal(2, BlockerCount(scene, obstacle));

        obstacle.Carve = true;
        Assert.True(TickUntil(scene, () => !Walkable(scene, new Float3(0, 0.2f, 0))) >= 0,
            "Switching back to carving must cut the hole again.");
        Assert.Equal(1, BlockerCount(scene, obstacle));
    }

    /// <summary>1 when the obstacle has no blocker in the crowd, 2 when it has one — written as
    /// a count so a failure reports which side of the switch broke.</summary>
    private static int BlockerCount(Scene scene, NavMeshObstacle obstacle)
    {
        Prowl.Recast.Detour.Crowd.DtCrowd? crowd = scene.Navigation.NativeCrowd;
        if (crowd == null) return 0;
        int count = 1;
        foreach (Prowl.Recast.Detour.Crowd.DtCrowdAgent a in crowd.GetActiveAgents())
            if (ReferenceEquals(a.option.userData, obstacle))
                count++;
        return count;
    }
}
