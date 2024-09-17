// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using DotRecast.Recast;
using DotRecast.Recast.Toolset;
using DotRecast.Recast.Toolset.Builder;
using DotRecast.Recast.Toolset.Geom;

namespace Prowl.Runtime;

public class NavMeshSurface : MonoBehaviour
{
    #region Public - Inspector

    public readonly bool useStaticGeometry = true;
    public LayerMask GeometryLayers;

    [ShowIf("useStaticGeometry")]
    public readonly List<Staticbody> staticGeometry = new();

    [ShowIf("useStaticGeometry", true)]
    public readonly List<MeshRenderer> meshGeometry = new();

    public BuildSettings navSettings = new();

    public DtNavMeshQuery? Query
    {
        get
        {
            if (navMesh == null) return null;
            _query ??= new DtNavMeshQuery(navMesh);
            return _query;
        }
    }

    public bool IsReady => ready;

    #endregion

    #region Private

    private bool ready = false;

    [HideInInspector] public DtNavMesh navMesh;
    private DtNavMeshQuery _query;


    private DtCrowd crowd;
    private List<NavMeshAgent> agents;

    #region Debug

    private bool triedDebugData = false;
    private Bounds debug_bounds;
    private Vector3[][][] debug_polygons;

    // private float timer = 0;

    #endregion

    #endregion

    public override void Awake()
    {
        if (navMesh == null) return;

        agents = new();
        DtCrowdConfig config = new DtCrowdConfig(0.6f);
        crowd = new DtCrowd(config, navMesh);
        DtObstacleAvoidanceParams option = new DtObstacleAvoidanceParams();
        option.velBias = 0.5f;
        option.adaptiveDivs = 5;
        option.adaptiveRings = 2;
        option.adaptiveDepth = 1;
        crowd.SetObstacleAvoidanceParams(0, option);

        ready = true;
    }

    public override void Update()
    {
        if (!ready) return;

        crowd.Update(Time.deltaTimeF, null);

        //timer += Time.deltaTimeF;
        //if (timer > 5f)
        //{
        //    timer = 0;
        //    RcRand f = new RcRand(Time.frameCount);
        //    IDtQueryFilter filter = new DtQueryDefaultFilter();
        //
        //    var status = Query.FindRandomPoint(filter, f, out var randomRef, out var randomPt);
        //    MoveAllToTarget(randomPt, false);
        //}
    }

    private DtNavMesh CreateNavMesh(RcNavMeshBuildSettings _navSettings, SceneMeshData input)
    {
        try
        {
            var verts = new List<System.Numerics.Vector3>();
            var indices = new List<int>();
            for (int i = 0; i < input.shapeData.Count; i++)
            {
                var shape = input.shapeData[i];
                verts.EnsureCapacity(verts.Count + shape.Vertices.Length);
                indices.EnsureCapacity(indices.Count + shape.Indices16.Length);

                for (int j = 0; j < shape.Indices16.Length; j += 3)
                {
                    indices.Add(verts.Count + shape.Indices16[j + 0]);
                    indices.Add(verts.Count + shape.Indices16[j + 1]);
                    indices.Add(verts.Count + shape.Indices16[j + 2]);
                }

                var transform = input.transformsOut[i];
                for (int l = 0; l < shape.Vertices.Length; l++)
                {
                    verts.Add(Transform.InverseTransformPoint(transform.TransformPoint(shape.Vertices[l])));
                }
            }

            // Get the backing array of this list,
            // get a span to that backing array,
            var spanToPoints = CollectionsMarshal.AsSpan(verts);
            // cast the type of span to read it as if it was a series of contiguous floats instead of contiguous vectors
            var reinterpretedPoints = MemoryMarshal.Cast<System.Numerics.Vector3, float>(spanToPoints);
            DemoInputGeomProvider geom = new DemoInputGeomProvider(reinterpretedPoints.ToArray().ToList(), indices);

            Debug.Log(geom.GetMeshBoundsMin().ToString());
            Debug.Log(geom.GetMeshBoundsMax().ToString());

            RcPartition partitionType = RcPartitionType.OfValue(_navSettings.partitioning);
            RcConfig cfg = new RcConfig(
                useTiles: true,
                _navSettings.tileSize,
                _navSettings.tileSize,
                RcConfig.CalcBorder(_navSettings.agentRadius, _navSettings.cellSize),
                partitionType,
                _navSettings.cellSize,
                _navSettings.cellHeight,
                _navSettings.agentMaxSlope,
                _navSettings.agentHeight,
                _navSettings.agentRadius,
                _navSettings.agentMaxClimb,
                (_navSettings.minRegionSize * _navSettings.minRegionSize) * _navSettings.cellSize * _navSettings.cellSize,
                (_navSettings.mergedRegionSize * _navSettings.mergedRegionSize) * _navSettings.cellSize * _navSettings.cellSize,
                _navSettings.edgeMaxLen,
                _navSettings.edgeMaxError,
                _navSettings.vertsPerPoly,
                _navSettings.detailSampleDist,
                _navSettings.detailSampleMaxError,
                _navSettings.filterLowHangingObstacles,
                _navSettings.filterLedgeSpans,
                _navSettings.filterWalkableLowHeightSpans,
                SampleAreaModifications.SAMPLE_AREAMOD_WALKABLE,
                buildMeshDetail: true);

            List<DtMeshData> dtMeshes = new();
            foreach (RcBuilderResult result in new RcBuilder().BuildTiles(geom, cfg, false, true, Environment.ProcessorCount - 1, Task.Factory))
            {
                DtNavMeshCreateParams navMeshCreateParams = DemoNavMeshBuilder.GetNavMeshCreateParams(geom, _navSettings.cellSize, _navSettings.cellHeight, _navSettings.agentHeight, _navSettings.agentRadius, _navSettings.agentMaxClimb, result);
                navMeshCreateParams.tileX = result.TileX;
                navMeshCreateParams.tileZ = result.TileZ;
                DtMeshData dtMeshData = DtNavMeshBuilder.CreateNavMeshData(navMeshCreateParams);
                if (dtMeshData != null)
                {
                    dtMeshes.Add(DemoNavMeshBuilder.UpdateAreaAndFlags(dtMeshData));
                }
            }

            DtNavMeshParams option = default;
            option.orig = geom.GetMeshBoundsMin();
            option.tileWidth = _navSettings.tileSize * _navSettings.cellSize;
            option.tileHeight = _navSettings.tileSize * _navSettings.cellSize;
            option.maxTiles = GetMaxTiles(geom, _navSettings.cellSize, _navSettings.tileSize);
            option.maxPolys = GetMaxPolysPerTile(geom, _navSettings.cellSize, _navSettings.tileSize);
            DtNavMesh navMesh = new DtNavMesh();
            navMesh.Init(option, _navSettings.vertsPerPoly);
            foreach (DtMeshData dtMeshData1 in dtMeshes)
            {
                navMesh.AddTile(dtMeshData1, 0, 0L, out _);
            }

            return navMesh;
        }
        catch (Exception e)
        {
            Debug.LogError(e.ToString());
            throw;
        }
    }

    private static int GetMaxTiles(DemoInputGeomProvider geom, float cellSize, int tileSize)
    {
        int tileBits = GetTileBits(geom, cellSize, tileSize);
        return 1 << tileBits;
    }

    private static int GetMaxPolysPerTile(DemoInputGeomProvider geom, float cellSize, int tileSize)
    {
        int num = 22 - GetTileBits(geom, cellSize, tileSize);
        return 1 << num;
    }

    private static int GetTileBits(DemoInputGeomProvider geom, float cellSize, int tileSize)
    {
        RcRecast.CalcGridSize(geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), cellSize, out var sizeX, out var sizeZ);
        int num = (sizeX + tileSize - 1) / tileSize;
        int num2 = (sizeZ + tileSize - 1) / tileSize;
        return Math.Min(DtUtils.Ilog2(DtUtils.NextPow2(num * num2)), 14);
    }

    #region Debug

    [GUIButton("Add All Geometry")]
    private void AddAllSceneStatics()
    {
        if (useStaticGeometry)
        {
            staticGeometry.Clear();
            foreach (var sBody in FindObjectsOfType<Staticbody>())
            {
                if (SceneManagement.SceneManager.Has(sBody.GameObject))
                    staticGeometry.Add(sBody);
            }
        }
        else
        {
            meshGeometry.Clear();
            foreach (var mRend in FindObjectsOfType<MeshRenderer>())
            {
                if (SceneManagement.SceneManager.Has(mRend.GameObject))
                    meshGeometry.Add(mRend);
            }
        }
    }


    private void CacheDebugData()
    {
        navMesh.ComputeBounds(out var min, out var max);
        debug_bounds = new Bounds() { min = ToV(min), max = ToV(max) };

        debug_polygons = new Vector3[navMesh.GetTileCount()][][];
        for (int i = 0; i < navMesh.GetTileCount(); i++)
        {
            var tile = navMesh.GetTile(i);
            float[] allverts = tile.data.verts;
            debug_polygons[i] = new Vector3[tile.data.polys.Length][];
            for (int j = 0; j < tile.data.polys.Length; j++)
            {
                var poly = tile.data.polys[j];
                var verts = poly.verts;

                debug_polygons[i][j] = new Vector3[poly.vertCount];
                for (int k = 0; k < poly.vertCount; k++)
                {
                    var v0 = allverts[verts[k] * 3 + 0];
                    var v1 = allverts[verts[k] * 3 + 1];
                    var v2 = allverts[verts[k] * 3 + 2];
                    debug_polygons[i][j][k] = new Vector3(v0, v1, v2);
                }
            }
        }
    }

    public override void DrawGizmosSelected()
    {
        if (navMesh == null) return;

        if (!triedDebugData)
        {
            CacheDebugData();
            triedDebugData = true;
        }

        if (debug_bounds != default)
        {
            /*
            Gizmos.Matrix = GameObject.Transform.localToWorldMatrix;
            Gizmos.Color = Color.blue;
            Gizmos.DrawCube(debug_bounds.center, debug_bounds.size);
            */
        }

        if (debug_polygons != null)
        {
            for (int i = 0; i < debug_polygons.Length; i++)
            {
                for (int j = 0; j < debug_polygons[i].Length; j++)
                {
                    for (int k = 0; k < debug_polygons[i][j].Length; k++)
                    {
                        /*
                        Gizmos.Matrix = GameObject.Transform.localToWorldMatrix;
                        Gizmos.DrawPolygon([debug_polygons[i][j][k], debug_polygons[i][j][(k + 1) % debug_polygons[i][j].Length]]);
                        */
                    }
                }
            }
        }
    }

    #endregion

    private RcVec3f ToRC(Vector3 v) => new((float)v.x, (float)v.y, (float)v.z);
    private Vector3 ToV(RcVec3f rc) => new(rc.X, rc.Y, rc.Z);

    #region Public API

    [GUIButton("Rebuild")]
    public void RebuildNavMesh()
    {
        var colliderData = new SceneMeshData();
        if (useStaticGeometry)
        {
            foreach (var sBody in staticGeometry)
            {
                if (sBody.EnabledInHierarchy && GeometryLayers.HasLayer(sBody.GameObject.layerIndex))
                    foreach (var collider in sBody.GameObject.GetComponentsInChildren<Collider>())
                        colliderData.Append(collider);
            }
        }
        else
        {
            foreach (var mRend in meshGeometry)
            {
                if (mRend.EnabledInHierarchy && mRend.Mesh.IsAvailable && GeometryLayers.HasLayer(mRend.GameObject.layerIndex))
                {
                    colliderData.shapeData.Add(mRend.Mesh.Res!);
                    colliderData.transformsOut.Add(mRend.Transform);
                }
            }
        }

        navMesh = CreateNavMesh(navSettings.ToRC(), colliderData);

        CacheDebugData();
    }

    public void RegisterAgent(NavMeshAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (!ready) throw new InvalidOperationException("Cannot register NavMeshAgent to a NavMeshSurface that has no NavMesh or hasn't been initialized!");

        agent.InternalAgent = crowd.AddAgent(ToRC(agent.Transform.position), agent.GetAgentParams());
        agents.Add(agent);
    }

    public void UnregisterAgent(NavMeshAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (!ready) throw new InvalidOperationException("Cannot unregister NavMeshAgent to a NavMeshSurface that has no NavMesh or hasn't been initialized!");

        crowd.RemoveAgent(agent.InternalAgent);
        agent.InternalAgent = null;
        agents.Remove(agent);
    }

    public void MoveAllToTarget(RcVec3f pos, bool adjust)
    {
        if (!ready) throw new InvalidOperationException("Cannot set move target on a NavMeshSurface that has no NavMesh or hasn't been initialized!");

        foreach (var ag in agents)
            MoveToTarget(ag, pos, adjust);
    }

    public void MoveToTarget(NavMeshAgent agent, RcVec3f pos, bool adjust)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(agent.InternalAgent);
        if (!ready) throw new InvalidOperationException("Cannot set move target on a NavMeshSurface that has no NavMesh or hasn't been initialized!");

        RcVec3f ext = crowd.GetQueryExtents();
        IDtQueryFilter filter = crowd.GetFilter(0);
        if (adjust)
        {
            RcVec3f vel = CalcVel(agent.InternalAgent!.npos, pos, agent.InternalAgent!.option.maxSpeed);
            crowd.RequestMoveVelocity(agent.InternalAgent!, vel);
        }
        else
        {
            Query!.FindNearestPoly(pos, ext, filter, out var nearestRef, out var nearestPt, out var _);
            crowd.RequestMoveTarget(agent.InternalAgent!, nearestRef, nearestPt);
        }
    }

    public RcVec3f CalcVel(RcVec3f pos, RcVec3f tgt, float speed)
    {
        RcVec3f vel = RcVec3f.Subtract(tgt, pos);
        vel.Y = 0.0f;
        vel = RcVec3f.Normalize(vel);
        vel = vel * speed;
        return vel;
    }


    #endregion

    class SceneMeshData
    {
        // public class MeshData
        // {
        //     public List<System.Numerics.Vector3> vertices;
        //     public List<int> indices;
        // }

        public readonly List<Mesh> shapeData = new();
        public readonly List<Transform> transformsOut = new();

        public void Append(Collider collider)
        {
            Mesh mesh = null;
            if (collider is SphereCollider sph)
            {
                mesh = Mesh.CreateSphere(sph.WorldRadius, 8, 8);
            }
            else if (collider is BoxCollider box)
            {
                mesh = Mesh.CreateCube(box.Size);
            }
            else if (collider is CylinderCollider cylinder)
            {
                mesh = Mesh.CreateCylinder(cylinder.Radius, cylinder.Length, 8);
            }
            else if (collider is CapsuleCollider capsule)
            {
#warning TODO: We need to implement a capsule mesh generator - cylinder sorta works for now
                mesh = Mesh.CreateCylinder(capsule.Radius, capsule.Length, 8);
            }
            else if (collider is TriangleCollider triangle)
            {
                mesh = Mesh.CreateTriangle(triangle.A, triangle.B, triangle.C);
            }

            if (mesh != null)
            {
                shapeData.Add(mesh);
                transformsOut.Add(collider.Transform);
            }
        }
    }

    public struct BuildSettings
    {
        public readonly float cellSize = 0.3f;
        public readonly float cellHeight = 0.2f;

        public readonly float agentHeight = 1f;
        public readonly float agentRadius = 0.5f;
        public readonly float agentMaxClimb = 0.9f;
        public readonly float agentMaxSlope = 45f;
        public readonly float agentMaxAcceleration = 8f;
        public readonly float agentMaxSpeed = 3.5f;

        public readonly int minRegionSize = 8;
        public readonly int mergedRegionSize = 20;

        public readonly bool filterLowHangingObstacles = true;
        public readonly bool filterLedgeSpans = true;
        public readonly bool filterWalkableLowHeightSpans = true;

        public readonly bool tiled;
        public readonly int tileSize = 32;

        public BuildSettings()
        {
            cellSize = 0.3f;
            cellHeight = 0.2f;

            agentHeight = 1f;
            agentRadius = 0.5f;
            agentMaxClimb = 0.9f;
            agentMaxSlope = 45f;
            agentMaxAcceleration = 8f;
            agentMaxSpeed = 3.5f;

            minRegionSize = 8;
            mergedRegionSize = 20;

            filterLowHangingObstacles = true;
            filterLedgeSpans = true;
            filterWalkableLowHeightSpans = true;

            tiled = false;
            tileSize = 32;
        }

        public RcNavMeshBuildSettings ToRC()
        {
            return new RcNavMeshBuildSettings
            {
                cellSize = cellSize,
                cellHeight = cellHeight,
                agentHeight = agentHeight,
                agentRadius = agentRadius,
                agentMaxClimb = agentMaxClimb,
                agentMaxSlope = agentMaxSlope,
                agentMaxAcceleration = agentMaxAcceleration,
                agentMaxSpeed = agentMaxSpeed,
                minRegionSize = minRegionSize,
                mergedRegionSize = mergedRegionSize,
                partitioning = RcPartitionType.WATERSHED.Value,
                filterLowHangingObstacles = filterLowHangingObstacles,
                filterLedgeSpans = filterLedgeSpans,
                filterWalkableLowHeightSpans = filterWalkableLowHeightSpans,
                edgeMaxLen = 12f,
                edgeMaxError = 1.3f,
                vertsPerPoly = 6,
                detailSampleDist = 6f,
                detailSampleMaxError = 1f,
                tiled = tiled,
                tileSize = tileSize,
                keepInterResults = false,
                buildAll = true
            };
        }
    }
}
