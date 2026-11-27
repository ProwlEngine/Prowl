using System;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Bins a view's lights, decals and reflection probes by screen tile and depth before its features draw, one dispatch a
/// layer, so a fragment reads the few items near it from registers rather than walking each set's trees (Drobot 2017,
/// "Improved Culling for Tiled and Clustered Rendering"). The view's binned items sit nearest first along its forward
/// axis; each depth bin holds the first and last item reaching it, and each screen tile a bit per item reaching it.
/// Compute users and host sets keep the trees. See Graphite/Internal/SpatialBins.glsl and Graphite/Trees.glsl, whose
/// layout these constants match.
/// </summary>
/// <remarks>
/// Word 0 of the buffer holds the words of a layer, then each layer has a header, <see cref="ZBins"/> words per set of
/// packed first and last item (16 bits each, all ones for none), then each set's tile masks, a tile's words together.
/// </remarks>
internal sealed class SpatialBinPass(LazyCompute program) : IDisposable
{
    /// <summary>The sets binned: lights, decals and probes, by their slots.</summary>
    public const int Sets = SpatialIndex.FirstHostSet;

    public const uint TileSize = 32;
    public const uint ZBins = 128;
    public const uint Header = 16;

    /// <summary>The words of one layer for so many tiles and binned items of each set.</summary>
    public static uint LayerWords(uint tiles, ReadOnlySpan<int> counts)
    {
        uint words = Header + Sets * ZBins;
        foreach (int count in counts)
            words += tiles * (((uint)count + 31) / 32);
        return words;
    }

    /// <summary>The words of a view's bins, the layer size first.</summary>
    public static uint Words(int layers, uint tiles, ReadOnlySpan<int> counts) => 1 + (uint)layers * LayerWords(tiles, counts);

    private static readonly PropertyID s_bins = "Graphite_BinsWrite", s_spheres = "Graphite_BinSpheres", s_counts = "_BinCounts", s_tiles = "_BinTiles";
    private readonly PropertySet _properties = new();

    /// <summary>Adds the pass binning a view's items, before its first feature.</summary>
    public void Add(ViewGraph view)
    {
        CameraFrame camera = view.Camera;
        if (camera.SpatialBins is not { } bins)
            return;
        FramePipeline frame = view.Frame;
        FrameGraph graph = view.Graph;
        int layers = Math.Max(camera.LayerCount, 1);
        uint tiles = camera.BinTilesX * camera.BinTilesY;
        uint layerWords = LayerWords(tiles, camera.BinCounts);
        Int4 counts = new(camera.BinCounts[0], camera.BinCounts[1], camera.BinCounts[2], (int)layerWords);
        Int4 tileCounts = new((int)camera.BinTilesX, (int)camera.BinTilesY, 0, 0);

        // A thread per set and depth bin, then per set, tile and mask word
        uint threads = Sets * ZBins;
        foreach (int count in camera.BinCounts)
            threads += tiles * (((uint)count + 31) / 32);
        uint groups = (threads + 63) / 64;
        uint groupsX = Math.Min(groups, 65535u), groupsY = (groups + groupsX - 1) / groupsX;

        graph.AddPass("Graphite.SpatialBins", builder =>
        {
            frame.Buffers.ReadWorld(graph, builder, camera.World);
            foreach (DeviceBuffer buffer in camera.Buffers.Items)
            {
                if (buffer != bins)
                    builder.Read(graph.Import(buffer), BufferAccess.AllReads);
            }
            builder.Write(graph.Import(bins), BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
        }, context =>
        {
            CommandBuffer cmd = context.Command;
            view.BeginRecording(context);
            cmd.SetComputeShader(program.Program);
            _properties.SetBuffer(s_bins, bins);
            _properties.SetBuffer(s_spheres, camera.BinSpheres.Buffer);
            _properties.SetInt4(s_counts, counts);
            _properties.SetInt4(s_tiles, tileCounts);
            for (uint layer = 0; layer < (uint)layers; layer++)
            {
                camera.Drawer.BindTables(cmd, layer);
                cmd.SetProperties(_properties);
                cmd.Dispatch(groupsX, groupsY, 1);
            }
        });
    }

    public void Dispose() => program.Dispose();
}
