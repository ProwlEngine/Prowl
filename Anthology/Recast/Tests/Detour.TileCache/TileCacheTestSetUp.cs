using System.Runtime.CompilerServices;
using Prowl.Recast.Detour.TileCache.Io.Compress;
using Prowl.Recast.Detour.TileCache.Tests.Io;

namespace Prowl.Recast.Detour.TileCache.Tests;

internal static class TileCacheTestSetUp
{
    [ModuleInitializer]
    internal static void RegisterLZ4Compressor()
    {
        DtTileCacheCompressorFactory.Shared.TryAdd(1, DtTileCacheLZ4ForTestCompressor.Shared);
    }
}
