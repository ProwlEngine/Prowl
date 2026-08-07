namespace Prowl.Recast.Detour.TileCache.Tests;

public class TestTileCacheMeshProcess : IDtTileCacheMeshProcess
{
    public void Process(DtNavMeshCreateParams option)
    {
        for (int i = 0; i < option.polyCount; ++i)
        {
            option.polyFlags[i] = 1;
        }
    }
}

