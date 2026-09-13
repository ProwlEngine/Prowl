// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// NavMesh Demo
//
// One scene showing the whole navigation subsystem working together:
// - NavMeshSurface: baked at runtime (no editor involved) over two ground slabs with a gap
// - NavMeshModifierVolume: excludes a region around a pillar, so the swarm visibly detours around it
// - NavMeshLink: bridges the gap between the two slabs - agents hop across it (see NavMeshAgent.JumpHeight)
// - NavMeshAgent: a swarm split by which slab it started on - the left side chases the player-controlled
//   cube, the right side flees from it, and both wander randomly near home when the player is too far
//   away to have noticed
// - Tiled baking: a second, separate ground ("the playground") baked with a small NavMeshBakeSettings.TileSize
//   so it comes out of one ordinary bake already split into a grid of tiles (drawn as yellow wire boxes) -
//   green "explorer" agents wander across it, crossing tile boundaries in a single, ordinary
//   SetDestination call, exactly the way the swarm above crosses within one unsplit tile
// - NavMeshObstacle: a red box sweeping back and forth across the playground, carving (and un-carving) a
//   real hole via DtTileCache as it moves - a small, bounded update regardless of how big the map is,
//   and one that never disrupts an explorer already walking through the tile it touches
// - NavMeshObstacle, in reverse: a sealed room at the far end of the playground, walled off on every
//   side (three permanent walls plus a door slab blocking the fourth) - the door's wall is itself just
//   a NavMeshObstacle, so disabling it partway through the demo "collapses" it, reconnecting the room's
//   own already-baked floor to the rest of the tile graph in the same one small carve
//
// Controls:
//   Move the cube: WASD
//
// Gizmos are on, so the baked mesh, the link, the modifier volume, the region boundaries, and each
// agent's capsule/path all render alongside the visible meshes.
//

using System;
using System.Collections.Generic;

using Prowl.Runtime;
using Prowl.Runtime.Navigation;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace NavMeshDemo;

internal class Program
{
    private static void Main(string[] args)
    {
        new MyGame().Run("NavMesh Demo", 1280, 720);
    }
}

public sealed class MyGame : Game
{
    /// <summary>How fast the player's cube moves, in world units per second.</summary>
    private const float PlayerSpeed = 6f;

    /// <summary>How close the player has to be before a swarm member notices it at all - inside this,
    /// chasers chase and fleers flee; outside it, both just wander near home.</summary>
    private const float DetectionRadius = 9f;

    /// <summary>How far a fleeing agent tries to put between itself and the player each time it retargets.</summary>
    private const float FleeDistance = 6f;

    /// <summary>How far from home a wandering agent's next random point can land.</summary>
    private const float WanderRadius = 4f;

    /// <summary>One swarm member: which behavior it runs (fixed by which slab it started on) and when
    /// it next re-evaluates its destination.</summary>
    private sealed class SwarmMember
    {
        public required NavMeshAgent Agent;
        public required bool IsChaser;
        public required Float3 Home;
        public float NextRetargetTime;
    }

    /// <summary>One region-playground explorer: just an agent and when it next picks a new random point
    /// somewhere on the playground - unlike <see cref="SwarmMember"/> it doesn't react to the player, so
    /// its own wandering is the only thing demonstrating cross-region routing.</summary>
    private sealed class RegionExplorer
    {
        public required NavMeshAgent Agent;
        public float NextRetargetTime;
    }

    private readonly List<SwarmMember> _swarm = [];
    private readonly List<RegionExplorer> _explorers = [];
    private readonly Random _rng = new();

    private Scene? _scene;
    private NavMeshSurface? _surface;
    private GameObject? _player;
    private bool _baked;

    /// <summary>How long after the playground bakes before the sealed room's door collapses.</summary>
    private const float RoomCollapseDelay = 15f;

    private int _regionWalkerTypeId;
    private NavMeshSurface? _regionRootSurface;
    private AABB _regionRootBounds;
    private GameObject? _obstacleGO;

    private Float3 _roomCenter;
    private GameObject? _doorObstacleGO;
    private GameObject? _doorVisualGO;
    private bool _roomOpened;
    private float _playgroundBakedTime;

    /// <summary>Builds the ground, a bridging link, a pillar with a modifier volume excluding its
    /// footprint, the player cube, and the swarm. Baking itself happens later, in
    /// <see cref="BeginUpdate"/>, once the scene has actually become current - see that method's own
    /// comment for why.</summary>
    public override void Initialize()
    {
        DrawGizmos = true;

        _scene = new Scene();

        GameObject lightGO = new("Directional Light");
        lightGO.AddComponent<DirectionalLight>();
        lightGO.Transform.LocalEulerAngles = new Float3(-55, 35, 0);
        _scene.Add(lightGO);

        GameObject cameraGO = new("Main Camera");
        cameraGO.Tag = "Main Camera";
        cameraGO.Transform.Position = new Float3(10, 42, -18);
        cameraGO.Transform.LookAt(new Float3(10, 0, 11));
        Camera camera = cameraGO.AddComponent<Camera>();
        camera.Depth = -1;
        camera.HDR = true;
        camera.Effects = [new FXAAEffect(), new TonemapperEffect()];
        _scene.Add(cameraGO);

        var groundMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));
        CreateGround("Ground A", Float3.Zero, groundMaterial);
        CreateGround("Ground B", new Float3(20, 0, 0), groundMaterial);

        CreatePillarWithExclusion(new Float3(2, 0, -1));

        GameObject surfaceGO = new("NavMesh Surface");
        _surface = surfaceGO.AddComponent<NavMeshSurface>();
        _scene.Add(surfaceGO);

        GameObject linkGO = new("Bridge Link");
        NavMeshLink link = linkGO.AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);
        link.EndPoint = new Float3(16, 0, 0);
        link.Width = 1.5f;
        _scene.Add(linkGO);

        CreatePlayer(new Float3(10, 0, 3));

        // Left slab (Ground A): chasers, colored red. Right slab (Ground B): fleers, colored blue.
        CreateSwarmAgent("Chaser 1", new Float3(-3, 0, -2), isChaser: true);
        CreateSwarmAgent("Chaser 2", new Float3(-3, 0, 2), isChaser: true);
        CreateSwarmAgent("Fleer 1", new Float3(23, 0, -2), isChaser: false);
        CreateSwarmAgent("Fleer 2", new Float3(23, 0, 2), isChaser: false);

        CreateRegionPlayground();

        Scene.Load(_scene);
    }

    /// <summary>A 10x10 slab whose top sits exactly at Y=0.</summary>
    private void CreateGround(string name, Float3 position, Material material)
    {
        GameObject go = new(name);
        go.Transform.Position = position - new Float3(0, 0.1f, 0);
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(Float3.One);
        renderer.Material = material;
        go.Transform.LocalScale = new Float3(10, 0.2f, 10);
        _scene!.Add(go);
    }

    /// <summary>A visible pillar plus a <see cref="NavMeshModifierVolume"/> marking the ground under
    /// and around it <see cref="NavMeshAreas.NotWalkable"/> - a real hole in the bake, not just a
    /// decorative obstacle, so the swarm's path actually curves around it.</summary>
    private void CreatePillarWithExclusion(Float3 groundPosition)
    {
        GameObject pillarGO = new("Obstacle Pillar");
        pillarGO.Transform.Position = groundPosition + new Float3(0, 1, 0);
        MeshRenderer pillarRenderer = pillarGO.AddComponent<MeshRenderer>();
        pillarRenderer.Mesh = Mesh.CreateCube(Float3.One);
        pillarRenderer.Material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        pillarGO.Transform.LocalScale = new Float3(1.5f, 2f, 1.5f);
        _scene!.Add(pillarGO);

        GameObject exclusionGO = new("Obstacle Exclusion Zone");
        exclusionGO.Transform.Position = groundPosition;
        NavMeshModifierVolume exclusion = exclusionGO.AddComponent<NavMeshModifierVolume>();
        exclusion.Size = new Float3(2.5f, 4f, 2.5f);
        exclusion.Area = NavMeshAreas.NotWalkable;
        _scene.Add(exclusionGO);
    }

    /// <summary>The WASD-controlled cube the swarm reacts to. Not a <see cref="NavMeshAgent"/> - it
    /// moves by direct input, unconstrained by the navmesh, the same way a player character driven by
    /// its own controller (not pathfinding) would.</summary>
    private void CreatePlayer(Float3 start)
    {
        _player = new GameObject("Player");
        _player.Transform.Position = start;
        MeshRenderer renderer = _player.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(Float3.One);
        var playerMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));
        playerMaterial.SetColor("_MainColor", new Color(1f, 0.85f, 0.2f, 1f));
        renderer.Material = playerMaterial;
        _player.Transform.LocalScale = new Float3(0.8f, 1.6f, 0.8f);
        _scene!.Add(_player);
    }

    /// <summary>A small visible cube driven by a <see cref="NavMeshAgent"/>, registered as a
    /// <see cref="SwarmMember"/> so <see cref="BeginUpdate"/> can drive its chase/flee/wander behavior.
    /// <paramref name="start"/> becomes its home for wandering and for how far a flee target is allowed
    /// to carry it - see <see cref="ComputeDestination"/>.</summary>
    private void CreateSwarmAgent(string name, Float3 start, bool isChaser)
    {
        GameObject go = new(name);
        go.Transform.Position = start;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(Float3.One);
        var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        material.SetColor("_MainColor", isChaser ? new Color(0.9f, 0.2f, 0.2f, 1f) : new Color(0.2f, 0.5f, 1f, 1f));
        renderer.Material = material;
        go.Transform.LocalScale = new Float3(0.6f, 1.8f, 0.6f);
        NavMeshAgent agent = go.AddComponent<NavMeshAgent>();
        _scene!.Add(go);

        _swarm.Add(new SwarmMember { Agent = agent, IsChaser = isChaser, Home = start });
    }

    /// <summary>A flat, subdivided grid mesh - unlike <see cref="Mesh.CreateCube"/>'s two giant
    /// triangles, this gives <see cref="NavMeshGeometryCollector"/>'s region-bounds filter (which keeps
    /// geometry by triangle centroid) enough triangles that every region the playground gets split into
    /// actually catches some near its own shared boundaries. See <see cref="BuildRegionPlayground"/>.</summary>
    private static Mesh CreateGrid(float width, float depth, int columns, int rows)
    {
        var vertices = new List<Float3>();
        var indices = new List<uint>();
        float hw = width * 0.5f;
        float hd = depth * 0.5f;

        for (int z = 0; z <= rows; z++)
        {
            for (int x = 0; x <= columns; x++)
            {
                float px = -hw + width * x / columns;
                float pz = -hd + depth * z / rows;
                vertices.Add(new Float3(px, 0, pz));
            }
        }

        int rowStride = columns + 1;
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                uint v0 = (uint)(z * rowStride + x);
                uint v1 = (uint)(z * rowStride + x + 1);
                uint v2 = (uint)((z + 1) * rowStride + x + 1);
                uint v3 = (uint)((z + 1) * rowStride + x);
                indices.AddRange([v0, v2, v1, v0, v3, v2]);
            }
        }

        return new Mesh { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
    }

    /// <summary>The second ground area, well clear of the chase/flee slabs: one continuous, subdivided
    /// surface for a dedicated "Region Walker" agent type (kept separate from the swarm's Humanoid type
    /// purely so its own, much smaller <see cref="NavMeshSurface.TileSize"/> doesn't affect anything
    /// else). Builds the ground, the surface, and the explorer agents; baking itself happens later, in
    /// <see cref="BuildRegionPlayground"/>, for the same deferred-until-the-scene-is-live reason
    /// <see cref="BakeAndReleaseSwarm"/> is.</summary>
    private void CreateRegionPlayground()
    {
        // Combined footprint of the open playground (Z 10..28) and the sealed room at its far end
        // (Z 28..38) - one continuous floor mesh underneath both, so the room's own ground is real,
        // already-baked navmesh from the start; only its walls (and, dynamically, its door) decide
        // whether that ground is actually reachable.
        Float3 center = new(10, 0, 24);
        Float3 size = new(32, 4, 28);
        _regionRootBounds = new AABB(center - size * 0.5f, center + size * 0.5f);
        _roomCenter = new Float3(10, 0, 33);

        GameObject groundGO = new("Region Playground Ground");
        groundGO.Transform.Position = new Float3(center.X, 0, center.Z);
        MeshRenderer groundRenderer = groundGO.AddComponent<MeshRenderer>();
        groundRenderer.Mesh = CreateGrid(size.X, size.Z, columns: 16, rows: 14);
        var groundMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));
        groundMaterial.SetColor("_MainColor", new Color(0.55f, 0.6f, 0.5f, 1f));
        groundRenderer.Material = groundMaterial;
        _scene!.Add(groundGO);

        _regionWalkerTypeId = NavMeshAgentTypes.Add("Region Walker", agentRadius: 0.5f, agentHeight: 2.0f, maxSlopeAngle: 45f, maxStepHeight: 0.4f);

        GameObject rootSurfaceGO = new("Region Root Surface");
        _regionRootSurface = rootSurfaceGO.AddComponent<NavMeshSurface>();
        _regionRootSurface.AgentTypeId = _regionWalkerTypeId;
        // A small tile size relative to the playground's own footprint, so one ordinary bake already
        // comes out as a real grid of tiles - see NavMeshBakeSettings.TileSize's own doc comment. No
        // manual splitting step needed: tiling is just how every bake works now.
        _regionRootSurface.TileSize = 8f;
        _scene.Add(rootSurfaceGO);

        CreateSealedRoom();

        CreateExplorer("Explorer 1", new Float3(-2, 0, 15));
        CreateExplorer("Explorer 2", new Float3(14, 0, 20));
        CreateExplorer("Explorer 3", new Float3(22, 0, 15));
    }

    /// <summary>A small room at the far (+Z) end of the playground, walled off on every side: three
    /// permanent walls (each a visible box plus a <see cref="NavMeshModifierVolume"/> exclusion, exactly
    /// like <see cref="CreatePillarWithExclusion"/>'s pillar) and, across the fourth side, a door slab
    /// whose blocking is a <see cref="NavMeshObstacle"/> rather than a permanent volume - so disabling
    /// just that one component later (see <see cref="CollapseDoorIfDue"/>) is a real, dynamic carve that
    /// reconnects the room's own already-baked floor, not a fake toggle.</summary>
    private void CreateSealedRoom()
    {
        const float wallHeight = 3f;
        const float wallThickness = 0.6f;
        // Room interior spans X [5,15], Z [28,38] - a 10x10 pocket at the north end of the shared floor.
        CreateWallSegment("Room Wall North", new Float3(10, 0, 38), new Float3(10.6f, wallHeight, wallThickness));
        CreateWallSegment("Room Wall East", new Float3(15, 0, 33), new Float3(wallThickness, wallHeight, 10.6f));
        CreateWallSegment("Room Wall West", new Float3(5, 0, 33), new Float3(wallThickness, wallHeight, 10.6f));
        // South wall (shared with the playground) flanks a 3-unit doorway centered on X=10.
        CreateWallSegment("Room Wall South-Left", new Float3(6.75f, 0, 28), new Float3(3.5f, wallHeight, wallThickness));
        CreateWallSegment("Room Wall South-Right", new Float3(13.25f, 0, 28), new Float3(3.5f, wallHeight, wallThickness));

        // The door itself: a visible slab plugging the gap, plus the NavMeshObstacle that's actually
        // responsible for the room being unreachable until it collapses.
        _doorVisualGO = new GameObject("Room Door");
        _doorVisualGO.Transform.Position = new Float3(10, wallHeight * 0.5f, 28);
        MeshRenderer doorRenderer = _doorVisualGO.AddComponent<MeshRenderer>();
        doorRenderer.Mesh = Mesh.CreateCube(Float3.One);
        var doorMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));
        doorMaterial.SetColor("_MainColor", new Color(0.6f, 0.45f, 0.3f, 1f));
        doorRenderer.Material = doorMaterial;
        _doorVisualGO.Transform.LocalScale = new Float3(3f, wallHeight, wallThickness);
        _doorVisualGO.AddComponent<NavMeshModifier>().IgnoreFromBuild = true; // purely visual, like the door obstacle's own box
        _scene!.Add(_doorVisualGO);

        _doorObstacleGO = new GameObject("Room Door Obstacle");
        _doorObstacleGO.Transform.Position = new Float3(10, 0, 28);
        NavMeshObstacle doorObstacle = _doorObstacleGO.AddComponent<NavMeshObstacle>();
        doorObstacle.Size = new Float3(3.4f, wallHeight, 2f); // comfortably wider than the 3-unit gap
        _scene.Add(_doorObstacleGO);
    }

    /// <summary>One permanent wall segment: a visible box plus a matching <see cref="NavMeshModifierVolume"/>
    /// so the floor underneath it never bakes as walkable, the same pattern <see cref="CreatePillarWithExclusion"/>
    /// already uses for the pillar.</summary>
    private void CreateWallSegment(string name, Float3 position, Float3 size)
    {
        GameObject wallGO = new(name);
        wallGO.Transform.Position = position + new Float3(0, size.Y * 0.5f, 0);
        MeshRenderer renderer = wallGO.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(Float3.One);
        var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        material.SetColor("_MainColor", new Color(0.35f, 0.32f, 0.3f, 1f));
        renderer.Material = material;
        wallGO.Transform.LocalScale = size;
        _scene!.Add(wallGO);

        GameObject exclusionGO = new($"{name} Exclusion");
        exclusionGO.Transform.Position = position;
        NavMeshModifierVolume exclusion = exclusionGO.AddComponent<NavMeshModifierVolume>();
        exclusion.Size = size + new Float3(0.4f, 4f, 0.4f); // a little wider than the wall itself, comfortably taller than the floor
        exclusion.Area = NavMeshAreas.NotWalkable;
        _scene.Add(exclusionGO);
    }

    /// <summary>A small green cube walking the region playground as a <see cref="_regionWalkerTypeId"/>
    /// agent - a distinct color from the red/blue chase/flee swarm, since it's demonstrating a different
    /// feature (region routing) rather than reacting to the player.</summary>
    private void CreateExplorer(string name, Float3 start)
    {
        GameObject go = new(name);
        go.Transform.Position = start;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(Float3.One);
        var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        material.SetColor("_MainColor", new Color(0.25f, 0.9f, 0.4f, 1f));
        renderer.Material = material;
        go.Transform.LocalScale = new Float3(0.6f, 1.8f, 0.6f);
        NavMeshAgent agent = go.AddComponent<NavMeshAgent>();
        agent.AgentTypeId = _regionWalkerTypeId;
        _scene!.Add(go);

        _explorers.Add(new RegionExplorer { Agent = agent });
    }

    /// <summary>
    /// Bakes the navmesh once the scene <see cref="Initialize"/> queued has actually become current -
    /// <see cref="Prowl.Runtime.Resources.Scene.Load"/> defers the swap to the end of the frame, so
    /// baking inside <see cref="Initialize"/> itself would run against a scene that is not live yet.
    /// After the first bake, moves the player from WASD input and lets each swarm member re-evaluate
    /// its destination on its own schedule.
    /// </summary>
    public override void BeginUpdate()
    {
        if (!_baked && ReferenceEquals(Scene.Current, _scene))
        {
            BakeAndReleaseSwarm();
            BuildRegionPlayground();
            _baked = true;
        }

        if (!_baked || _player.IsNotValid()) return;

        MovePlayer();

        float now = Time.UnscaledTotalTime;
        foreach (SwarmMember member in _swarm)
        {
            if (!member.Agent.IsValid()) continue;
            if (now < member.NextRetargetTime && !member.Agent.HasArrived) continue;

            member.Agent.SetDestination(ComputeDestination(member));
            // A little randomness in the interval itself, on top of the randomness in wander/flee
            // targets, so the whole swarm doesn't visibly lock-step retarget on the same frame.
            member.NextRetargetTime = now + 1f + (float)_rng.NextDouble();
        }

        foreach (RegionExplorer explorer in _explorers)
        {
            if (!explorer.Agent.IsValid()) continue;
            if (now < explorer.NextRetargetTime && !explorer.Agent.HasArrived) continue;

            explorer.Agent.SetDestination(RandomPointInPlayground());
            explorer.NextRetargetTime = now + 2f + (float)_rng.NextDouble() * 2f;
        }

        MoveRegionObstacle(now);
        CollapseDoorIfDue(now);
        DrawRegionBoundaries();
    }

    /// <summary>Sweeps the obstacle back and forth across the playground's X extent - slow enough to
    /// watch it cross a region boundary and see the affected leaf's own re-bake take effect, both as it
    /// arrives (a hole appears) and after it moves on (that ground returns).</summary>
    private void MoveRegionObstacle(float now)
    {
        if (_obstacleGO.IsNotValid()) return;

        float half = _regionRootBounds.Size.X * 0.5f - 3f; // keep it comfortably inside the playground
        float x = _regionRootBounds.Center.X + MathF.Sin(now * 0.3f) * half;
        _obstacleGO!.Transform.Position = new Float3(x, 0, 19);
    }

    /// <summary>Direct WASD movement on the XZ plane - no navmesh, no collision, just input.</summary>
    private void MovePlayer()
    {
        Float3 move = Float3.Zero;
        if (Input.GetKey(KeyCode.W)) move += Float3.UnitZ;
        if (Input.GetKey(KeyCode.S)) move -= Float3.UnitZ;
        if (Input.GetKey(KeyCode.D)) move += Float3.UnitX;
        if (Input.GetKey(KeyCode.A)) move -= Float3.UnitX;

        if (Float3.Length(move) < 0.0001f) return;

        _player!.Transform.Position += Float3.Normalize(move) * PlayerSpeed * Time.DeltaTime;
    }

    /// <summary>Chase, flee, or wander, depending on <see cref="SwarmMember.IsChaser"/> and how close
    /// the player currently is. Chase/flee targets are the player's exact position or a point away from
    /// it; wander targets are random points near <see cref="SwarmMember.Home"/> - both are clamped to
    /// stay within that member's own slab, so a fleeing agent never gets sent toward the open gap.</summary>
    private Float3 ComputeDestination(SwarmMember member)
    {
        Float3 playerPosition = _player!.Transform.Position;
        Float3 agentPosition = member.Agent.Position;
        float distanceToPlayer = Float3.Distance(agentPosition, playerPosition);

        if (distanceToPlayer > DetectionRadius)
            return ClampToHomeSlab(RandomPointAround(member.Home, WanderRadius), member.Home);

        if (member.IsChaser)
            return playerPosition;

        Float3 away = agentPosition - playerPosition;
        if (Float3.Length(away) < 0.01f) away = Float3.UnitX; // degenerate: standing right on the player
        away = Float3.Normalize(away);

        // A little angular jitter so a group of fleers doesn't all run in exactly the same direction.
        float jitter = ((float)_rng.NextDouble() - 0.5f) * MathF.PI * 0.6f; // +/- 54 degrees
        float cos = MathF.Cos(jitter), sin = MathF.Sin(jitter);
        Float3 jittered = new Float3(away.X * cos - away.Z * sin, 0f, away.X * sin + away.Z * cos);

        return ClampToHomeSlab(agentPosition + jittered * FleeDistance, member.Home);
    }

    /// <summary>A random point on the XZ plane within <paramref name="radius"/> of <paramref name="center"/>.</summary>
    private Float3 RandomPointAround(Float3 center, float radius)
    {
        float x = center.X + ((float)_rng.NextDouble() * 2f - 1f) * radius;
        float z = center.Z + ((float)_rng.NextDouble() * 2f - 1f) * radius;
        return new Float3(x, center.Y, z);
    }

    /// <summary>Keeps a computed destination within the 10x10 slab <paramref name="home"/> sits on -
    /// without this, a fleer running from the player (or a wander roll) could land in the open gap
    /// between the two slabs, or past its far edge, neither of which has navmesh to snap onto. Clamps
    /// to the actual slab bounds (centered at X=0 or X=20, see <see cref="Initialize"/>) rather than to
    /// a box around <paramref name="home"/> itself, so this stays correct regardless of exactly where
    /// within its slab a member happened to spawn.</summary>
    private static Float3 ClampToHomeSlab(Float3 point, Float3 home)
    {
        float slabCenterX = home.X < 10f ? 0f : 20f;
        float x = Maths.Clamp(point.X, slabCenterX - 4f, slabCenterX + 4f);
        float z = Maths.Clamp(point.Z, -4f, 4f);
        return new Float3(x, point.Y, z);
    }

    /// <summary>Runs the same collect/build/apply pipeline the editor's Bake button does, then gives
    /// every swarm member an initial destination.</summary>
    private void BakeAndReleaseSwarm()
    {
        if (_surface == null || _scene == null) return;

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(_scene, _surface);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, _surface.EffectiveBakeSettings);
        if (!result.Success)
        {
            Debug.LogError($"[NavMeshDemo] Bake failed: {result.Error}");
            return;
        }

        var asset = new NavMesh();
        asset.Apply(result, _surface.EffectiveBakeSettings, _surface.AgentTypeId);
        _surface.NavMeshAsset = asset;
        _surface.RebuildQuery();

        foreach (SwarmMember member in _swarm)
            member.Agent.SetDestination(ComputeDestination(member));
    }

    /// <summary>
    /// Bakes the region playground exactly the same way <see cref="BakeAndReleaseSwarm"/> bakes the
    /// swarm's own surface - the only difference is <see cref="NavMeshSurface.TileSize"/> being small
    /// enough that this one bake already comes out as a real grid of tiles (see
    /// <see cref="CreateRegionPlayground"/>). Crossing between them is a single, ordinary
    /// <see cref="NavMeshAgent.SetDestination"/> call - Detour paths across tile boundaries natively,
    /// the same way it does across polygons within one tile - so there is no separate router or hand-off
    /// step to build here at all. Spawns the moving obstacle and gives every explorer its first
    /// destination once the bake succeeds.
    /// </summary>
    private void BuildRegionPlayground()
    {
        if (_regionRootSurface == null || _scene == null) return;

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(_scene, _regionRootSurface);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, _regionRootSurface.EffectiveBakeSettings);
        if (!result.Success)
        {
            Debug.LogError($"[NavMeshDemo] Region playground bake failed: {result.Error}");
            return;
        }

        var asset = new NavMesh();
        asset.Apply(result, _regionRootSurface.EffectiveBakeSettings, _regionRootSurface.AgentTypeId);
        _regionRootSurface.NavMeshAsset = asset;
        _regionRootSurface.RebuildQuery();

        CreateRegionObstacle();
        _playgroundBakedTime = Time.UnscaledTotalTime;

        foreach (RegionExplorer explorer in _explorers)
            explorer.Agent.SetDestination(RandomPointInPlayground());
    }

    /// <summary>Once, <see cref="RoomCollapseDelay"/> seconds after the playground baked, disables the
    /// door's <see cref="NavMeshObstacle"/> and hides its visual slab - the room's own floor was real,
    /// baked navmesh the whole time, so this is the entire "wall collapses" effect: one obstacle carve
    /// reconnecting already-existing ground, not a rebake or a scripted teleport. Sends the nearest
    /// explorer in to make the newly opened room immediately visible rather than waiting on chance.</summary>
    private void CollapseDoorIfDue(float now)
    {
        if (_roomOpened || now < _playgroundBakedTime + RoomCollapseDelay) return;
        if (_doorObstacleGO.IsNotValid()) return;

        _roomOpened = true;
        _doorObstacleGO!.Enabled = false;
        if (_doorVisualGO.IsValid()) _doorVisualGO!.Enabled = false;
        Debug.Log("[NavMeshDemo] The room's door has collapsed - its floor was reachable navmesh all along, just carved off until now.");

        RegionExplorer? nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (RegionExplorer explorer in _explorers)
        {
            if (!explorer.Agent.IsValid()) continue;
            float distance = Float3.Distance(explorer.Agent.Position, _roomCenter);
            if (distance < nearestDistance) { nearestDistance = distance; nearest = explorer; }
        }
        nearest?.Agent.SetDestination(_roomCenter);
    }

    /// <summary>A slow-sweeping box demonstrating <see cref="NavMeshObstacle"/>: as it moves back and
    /// forth across the playground, it carves (and, behind it, un-carves) a real hole via
    /// <c>DtTileCache</c> - a small, bounded update regardless of how big the map is, and one that never
    /// disrupts an explorer already walking through the tile it touches. See that class's own doc
    /// comment.</summary>
    private void CreateRegionObstacle()
    {
        _obstacleGO = new GameObject("Region Obstacle");
        _obstacleGO.Transform.Position = new Float3(10, 0, 19);
        MeshRenderer renderer = _obstacleGO.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(Float3.One);
        var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        material.SetColor("_MainColor", new Color(1f, 0.3f, 0.2f, 1f));
        renderer.Material = material;
        _obstacleGO.Transform.LocalScale = new Float3(3f, 3f, 3f);

        NavMeshObstacle obstacle = _obstacleGO.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(3f, 3f, 3f);
        _scene!.Add(_obstacleGO);
    }

    /// <summary>A random point anywhere on the playground - deliberately not clamped to any one region,
    /// so an explorer's own wandering is what carries it across a boundary and exercises the hand-off.</summary>
    private Float3 RandomPointInPlayground()
    {
        float x = _regionRootBounds.Min.X + (float)_rng.NextDouble() * _regionRootBounds.Size.X;
        float z = _regionRootBounds.Min.Z + (float)_rng.NextDouble() * _regionRootBounds.Size.Z;
        return new Float3(x, 0, z);
    }

    /// <summary>Draws each of the playground's actual baked tiles as a yellow wire box, so the tiling
    /// itself - not just the agents crossing it - is visible. Reads straight from the baked asset's own
    /// tile data rather than tracking anything separately: the tile grid it draws is exactly the one
    /// <see cref="NavMeshQuery"/> is really built from, not an approximation of it.</summary>
    private void DrawRegionBoundaries()
    {
        NavMeshTileCacheData? data = _regionRootSurface?.NavMeshAsset.Res?.TileCacheData;
        if (data == null) return;

        var color = new Color(1f, 0.9f, 0.1f, 1f);
        var drawn = new HashSet<(int, int)>();
        foreach (NavMeshTileLayer layer in data.Layers)
        {
            if (!drawn.Add((layer.TileX, layer.TileY))) continue;

            Float3 tileMin = data.Origin + new Float3(layer.TileX * data.TileWorldSize, 0, layer.TileY * data.TileWorldSize);
            Float3 center = tileMin + new Float3(data.TileWorldSize * 0.5f, 2f, data.TileWorldSize * 0.5f);
            Float3 halfExtents = new(data.TileWorldSize * 0.5f, 2f, data.TileWorldSize * 0.5f);
            Debug.DrawWireCube(center, halfExtents, color);
        }
    }
}
