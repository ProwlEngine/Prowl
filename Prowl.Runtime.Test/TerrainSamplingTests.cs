// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Guards the coordinate conventions the terrain maps share. Heights are a vertex grid, splats and
/// details are cell grids, and every consumer (surface shader, grass placement, physics holes) has
/// to agree on where a given index lands in UV or the maps drift apart on screen.
/// </summary>
public class TerrainSamplingTests
{
    private const int kRes = 33;

    /// <summary>Terrain whose normalized height equals U everywhere, so height at UV is analytically known.</summary>
    private static TerrainData RampTerrain()
    {
        var data = new TerrainData { Size = 32f, Height = 100f };
        data.ResizeHeightmap(kRes);
        for (int z = 0; z < kRes; z++)
            for (int x = 0; x < kRes; x++)
                data.SetHeight(x, z, x / (float)(kRes - 1));
        return data;
    }

    [Theory]
    [InlineData(TerrainInterpolation.Bilinear)]
    [InlineData(TerrainInterpolation.Bicubic)]
    public void InterpolatedHeightFollowsTheVertexGrid(TerrainInterpolation mode)
    {
        using var data = RampTerrain();
        data.Interpolation = mode;

        // Both filters reproduce a linear ramp exactly, so any drift here is a convention shift.
        for (float u = 0.1f; u <= 0.9f; u += 0.05f)
            Assert.Equal(u * data.Height, data.GetInterpolatedHeight(u, 0.5f), 1);
    }

    [Fact]
    public void HeightSamplesLandOnTheirOwnUV()
    {
        using var data = RampTerrain();
        for (int x = 1; x < kRes - 1; x++)
        {
            Float2 uv = data.HeightmapToUV(x, 4);
            Assert.Equal(data.GetHeight(x, 4) * data.Height, data.GetInterpolatedHeight(uv.X, uv.Y), 1);
        }
    }

    [Fact]
    public void DetailCellsSitInsideTheSplatTexelTheyOverlap()
    {
        using var data = new TerrainData();
        data.DetailResolution = data.SplatmapResolution * 2;

        // Two detail cells per splat texel, both must resolve to that same texel.
        for (int k = 0; k < data.SplatmapResolution; k += 37)
        {
            for (int sub = 0; sub < 2; sub++)
            {
                Float2 uv = data.DetailToUV(k * 2 + sub, 0);
                Assert.Equal(k, (int)(uv.X * data.SplatmapResolution));
            }
        }
    }

    [Fact]
    public void SplatTexelUVResolvesBackToItself()
    {
        using var data = new TerrainData();
        for (int x = 0; x < data.SplatmapResolution; x += 53)
        {
            Float2 uv = data.SplatmapToUV(x, x);
            Assert.Equal(x, (int)(uv.X * data.SplatmapResolution));
            Assert.Equal(x, (int)(uv.Y * data.SplatmapResolution));
        }
    }

    [Fact]
    public void HoleLookupHitsTheSplatTexelUnderTheHeightCell()
    {
        using var data = new TerrainData();
        int last = data.SplatmapResolution - 1;

        data.SetHole(0, 0, 0);
        data.SetHole(last, last, 0);

        Assert.True(data.IsCellHole(0, 0));
        Assert.True(data.IsCellHole(last, last));
        Assert.False(data.IsCellHole(1, 1));
        Assert.False(data.IsCellHole(last - 1, last - 1));
    }

    [Fact]
    public void DetailEditsBumpTheVersionRenderersWatch()
    {
        using var data = new TerrainData();
        int before = data.DetailsVersion;

        data.SetDetailDensity(0, 4, 4, 1f);

        Assert.NotEqual(before, data.DetailsVersion);
    }

    [Fact]
    public void HeightEditsBumpTheVersionRenderersWatch()
    {
        using var data = new TerrainData();
        int before = data.HeightsVersion;

        data.SetHeight(4, 4, 0.5f);

        Assert.NotEqual(before, data.HeightsVersion);
    }
}
