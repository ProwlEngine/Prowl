using DotRecast.Core.Numerics;
using DotRecast.Detour;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    public sealed class NavMesh
    {
        public DtNavMesh DetourNavMesh;

        private DtNavMeshQuery _query;
        public DtNavMeshQuery? Query {
            get {
                if (DetourNavMesh == null) return null;
                _query ??= new DtNavMeshQuery(DetourNavMesh);
                return _query;
            }
        }

        public int GetMaxTiles() => DetourNavMesh.GetMaxTiles();

        public DtMeshTile GetTile(int i) => DetourNavMesh.GetTile(i);

        public DtStatus UpdateTile(DtMeshData data, int flags) => DetourNavMesh.UpdateTile(data, flags);

        public DtStatus AddTile(DtMeshData data, int flags, long lastRef, out long result) => DetourNavMesh.AddTile(data, flags, lastRef, out result);

        public long RemoveTile(long refs) => DetourNavMesh.RemoveTile(refs);

        public bool GetPolyHeight(DtMeshTile tile, DtPoly poly, RcVec3f pos, out float height) => DetourNavMesh.GetPolyHeight(tile, poly, pos, out height);

        public void ClosestPointOnPoly(long refs, RcVec3f pos, out RcVec3f closest, out bool posOverPoly) => DetourNavMesh.ClosestPointOnPoly(refs, pos, out closest, out posOverPoly);

        public int GetTilesAt(int x, int y, DtMeshTile[] tiles, int maxTiles) => DetourNavMesh.GetTilesAt(x, y, tiles, maxTiles);

        public long GetTileRefAt(int x, int y, int layer) => DetourNavMesh.GetTileRefAt(x, y, layer);

        public DtMeshTile GetTileByRef(long refs) => DetourNavMesh.GetTileByRef(refs);

        public long GetTileRef(DtMeshTile tile) => DetourNavMesh.GetTileRef(tile);

        public DtStatus GetOffMeshConnectionPolyEndPoints(long prevRef, long polyRef, ref Vector3 startPos, ref Vector3 endPos) {
            var startPosRc = new RcVec3f((float)startPos.x, (float)startPos.y, (float)startPos.z);
            var endPosRc = new RcVec3f((float)endPos.x, (float)endPos.y, (float)endPos.z);
            var res = DetourNavMesh.GetOffMeshConnectionPolyEndPoints(prevRef, polyRef, ref startPosRc, ref endPosRc);
            startPos = new Vector3(startPosRc.X, startPosRc.Y, startPosRc.Z);
            endPos = new Vector3(endPosRc.X, endPosRc.Y, endPosRc.Z);
            return res;
        }

        public int GetMaxVertsPerPoly() => DetourNavMesh.GetMaxVertsPerPoly();

        public int GetTileCount() => DetourNavMesh.GetTileCount();

        public bool IsAvailableTileCount() => DetourNavMesh.IsAvailableTileCount();

        public DtStatus SetPolyFlags(long refs, int flags) => DetourNavMesh.SetPolyFlags(refs, flags);

        public DtStatus GetPolyFlags(long refs, out int resultFlags) => DetourNavMesh.GetPolyFlags(refs, out resultFlags);

        public DtStatus SetPolyArea(long refs, char area) => DetourNavMesh.SetPolyArea(refs, area);

        public DtStatus GetPolyArea(long refs, out int resultArea) => DetourNavMesh.GetPolyArea(refs, out resultArea);

        public Vector3 GetPolyCenter(long refs)
        {
            var res = DetourNavMesh.GetPolyCenter(refs);
            return new Vector3(res.X, res.Y, res.Z);
        }

        public void ComputeBounds(out Vector3 bmin, out Vector3 bmax){
            DetourNavMesh.ComputeBounds(out var bminRc, out var bmaxRc);
            bmin = new Vector3(bminRc.X, bminRc.Y, bminRc.Z);
            bmax = new Vector3(bmaxRc.X, bmaxRc.Y, bmaxRc.Z);
        }
    }
}
