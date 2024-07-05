using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Toolset;
using DotRecast.Recast.Toolset.Builder;
using DotRecast.Recast.Toolset.Geom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Prowl.Runtime
{
    public class NavMeshSurface : MonoBehaviour
    {
        public List<Staticbody> staticGeometry = new();

        public BuildSettings navSettings = new();

        [HideInInspector] public NavMesh navMesh;

        private bool triedDebugData = false;
        private Bounds debug_bounds;
        private Vector3[][][] debug_polygons;

        [GUIButton("Rebuild")]
        public void RebuildNavMesh()
        {
            var colliderData = new SceneColliderData();
            foreach (var sBody in staticGeometry)
            {
                foreach (var collider in sBody.GameObject.GetComponentsInChildren<Collider>())
                    colliderData.Append(collider);
            }

            navMesh = new() { DetourNavMesh = CreateNavMesh(navSettings.ToRC(), colliderData) };

            CacheDebugData();
        }

        private void CacheDebugData()
        {
            navMesh.ComputeBounds(out var min, out var max);
            debug_bounds = new Bounds() { min = min, max = max };

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

        [GUIButton("Add All Staticbodies")]
        public void AddAllSceneStatics()
        {
            staticGeometry.Clear();
            foreach (var sBody in EngineObject.FindObjectsOfType<Staticbody>())
            {
                if (SceneManagement.SceneManager.Has(sBody.GameObject))
                    staticGeometry.Add(sBody);
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
                Gizmos.Matrix = GameObject.Transform.localToWorldMatrix;
                Gizmos.Color = Color.blue;
                Gizmos.DrawCube(debug_bounds.center, debug_bounds.size);
            }

            if (debug_polygons != null)
            {
                for (int i = 0; i < debug_polygons.Length; i++)
                {
                    for (int j = 0; j < debug_polygons[i].Length; j++)
                    {
                        Gizmos.Color = Color.blue * 0.5f;
                        for (int k = 0; k < debug_polygons[i][j].Length; k++)
                        {
                            Gizmos.Matrix = GameObject.Transform.localToWorldMatrix;
                            Gizmos.DrawPolygon([debug_polygons[i][j][k], debug_polygons[i][j][(k + 1) % debug_polygons[i][j].Length]]);
                        }
                    }
                }
            }
        }

        private DtNavMesh CreateNavMesh(RcNavMeshBuildSettings _navSettings, SceneColliderData input)
        {
            try
            {
                var verts = new List<System.Numerics.Vector3>();
                var indices = new List<int>();
                for (int i = 0; i < input.shapeData.Count; i++)
                {
                    var shape = input.shapeData[i];
                    verts.EnsureCapacity(verts.Count + shape.Vertices.Length);
                    indices.EnsureCapacity(indices.Count + shape.Indices.Length);

                    for (int j = 0; j < shape.Indices.Length; j += 3)
                    {
                        indices.Add(verts.Count + (int)shape.Indices[j + 0]);
                        indices.Add(verts.Count + (int)shape.Indices[j + 1]);
                        indices.Add(verts.Count + (int)shape.Indices[j + 2]);
                    }

                    var transform = input.transformsOut[i];
                    for (int l = 0; l < shape.Vertices.Length; l++)
                    {
                        verts.Add(this.Transform.InverseTransformPoint(transform.TransformPoint(shape.Vertices[l])));
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
                foreach (RcBuilderResult result in new RcBuilder().BuildTiles(geom, cfg, false, true, 4, Task.Factory))
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
            catch(Exception e)
            {
                Debug.LogError(e.ToString());
                throw e;
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

        private static int[] GetTiles(DemoInputGeomProvider geom, float cellSize, int tileSize)
        {
            RcRecast.CalcGridSize(geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), cellSize, out var sizeX, out var sizeZ);
            int num = (sizeX + tileSize - 1) / tileSize;
            int num2 = (sizeZ + tileSize - 1) / tileSize;
            return [num, num2];
        }

        class SceneColliderData
        {
            public class MeshData
            {
                public List<System.Numerics.Vector3> vertices;
                public List<int> indices;
            }

            public List<Mesh> shapeData = new();
            public List<Transform> transformsOut = new();

            public void Append(Collider collider)
            {
                Mesh mesh = null;
                if(collider is SphereCollider sph)
                {
                    mesh = Mesh.CreateSphere(sph.WorldRadius, 8, 8);
                }
                else if(collider is BoxCollider box)
                {
                    mesh = Mesh.CreateCube(box.Size);
                }
                else if(collider is CylinderCollider cylinder)
                {
                    mesh = Mesh.CreateCylinder(cylinder.Radius, cylinder.Length, 8);
                }
                else if(collider is CapsuleCollider capsule)
                {
#warning TODO: We need to implement a capsule mesh generator - cylinder sorta works for now
                    mesh = Mesh.CreateCylinder(capsule.Radius, capsule.Length, 8);
                }
                else if(collider is TriangleCollider triangle)
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
            public float cellSize = 0.3f;
            public float cellHeight = 0.2f;

            public float agentHeight = 2f;
            public float agentRadius = 0.6f;
            public float agentMaxClimb = 0.9f;
            public float agentMaxSlope = 45f;
            public float agentMaxAcceleration = 8f;
            public float agentMaxSpeed = 3.5f;

            public int minRegionSize = 8;
            public int mergedRegionSize = 20;

            public bool filterLowHangingObstacles = true;
            public bool filterLedgeSpans = true;
            public bool filterWalkableLowHeightSpans = true;

            public bool tiled;
            public int tileSize = 32;

            public BuildSettings()
            {
                cellSize = 0.3f;
                cellHeight = 0.2f;

                agentHeight = 2f;
                agentRadius = 0.6f;
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
                return new RcNavMeshBuildSettings {
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
                    partitioning = DotRecast.Recast.RcPartitionType.WATERSHED.Value,
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

}
