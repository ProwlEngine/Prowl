using System;

using Xunit;

namespace Prowl.Veldrid.Tests;

// CPU-side coverage of the profiling value types (the counter-API surface). The actual
// per-frame recording lives on GraphicsDevice and is exercised by the GPU profiling tests;
// here we verify the read structures: counter storage, enum-indexed bin lookup, the
// zeroed-when-disabled contract, and ProfileSnapshot wiring. These rely on InternalsVisibleTo
// to reach the internal constructors.
public class ProfilingTests
{
    [Fact]
    public void ProfileCounter_StoresCountAndBytes()
    {
        ProfileCounter c = new(5, 4096);

        Assert.Equal(5, c.Count);
        Assert.Equal(4096, c.Bytes);
    }

    [Fact]
    public void ProfileCounter_Default_IsZero()
    {
        ProfileCounter c = default;

        Assert.Equal(0, c.Count);
        Assert.Equal(0, c.Bytes);
    }

    [Fact]
    public void ProfileBinGroup_IndexesByEnumValue()
    {
        ProfileCounter[] counters = new ProfileCounter[Enum.GetValues<AllocBin>().Length];
        counters[(int)AllocBin.Texture] = new ProfileCounter(3, 1024);
        counters[(int)AllocBin.Shader] = new ProfileCounter(7, 0);

        ProfileBinGroup<AllocBin> group = new(counters);

        Assert.Equal(3, group[AllocBin.Texture].Count);
        Assert.Equal(1024, group[AllocBin.Texture].Bytes);
        Assert.Equal(7, group[AllocBin.Shader].Count);
        Assert.Equal(0, group[AllocBin.DeviceBuffer].Count);
    }

    [Fact]
    public void ProfileBinGroup_Default_ReturnsZeroCounterForAnyBin()
    {
        // A snapshot taken while profiling is disabled carries no backing storage.
        ProfileBinGroup<BufferRoleBin> group = default;

        Assert.Equal(0, group[BufferRoleBin.Vertex].Count);
        Assert.Equal(0, group[BufferRoleBin.Dynamic].Bytes);
    }

    [Fact]
    public void ProfileSnapshot_RoutesEachAccessorToItsGroup()
    {
        ProfileCounter[] live = new ProfileCounter[Enum.GetValues<AllocBin>().Length];
        live[(int)AllocBin.DeviceBuffer] = new ProfileCounter(2, 512);

        ProfileCounter[] bufferMem = new ProfileCounter[Enum.GetValues<BufferRoleBin>().Length];
        bufferMem[(int)BufferRoleBin.Uniform] = new ProfileCounter(1, 256);

        ProfileSnapshot snapshot = new(
            allocated: default,
            freed: default,
            bufferOps: default,
            swaps: default,
            live: new ProfileBinGroup<AllocBin>(live),
            bufferMem: new ProfileBinGroup<BufferRoleBin>(bufferMem));

        Assert.Equal(2, snapshot.Live[AllocBin.DeviceBuffer].Count);
        Assert.Equal(512, snapshot.Live[AllocBin.DeviceBuffer].Bytes);
        Assert.Equal(256, snapshot.BufferMem[BufferRoleBin.Uniform].Bytes);

        // Untouched flow accessors stay zeroed.
        Assert.Equal(0, snapshot.Allocated[AllocBin.Texture].Count);
        Assert.Equal(0, snapshot.Swaps[SwapBin.Present].Count);
    }

    [Theory]
    [InlineData(AllocBin.DeviceBuffer, 0)]
    [InlineData(AllocBin.Texture, 1)]
    [InlineData(AllocBin.PropertySet, 8)]
    public void AllocBin_HasStableOrdinals(AllocBin bin, int expectedOrdinal)
    {
        // The bin->array-index mapping relies on these ordinals staying fixed.
        Assert.Equal(expectedOrdinal, (int)bin);
    }
}
