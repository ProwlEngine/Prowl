// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class CloudTests
{
    [Fact]
    public void Grid_PutsEveryCornerAtItsParticle()
    {
        const int size = 8;
        var mesh = CloudRenderer.BuildGrid(size);
        Float3[] vertices = mesh.Vertices;
        Float2[] uv = mesh.UV;
        uint[] indices = mesh.Indices;
        Assert.Equal(size * size * 4, vertices.Length);
        Assert.Equal(size * size * 6, indices.Length);

        for (int quad = 0; quad < size * size; quad++)
        {
            uint first = indices[quad * 6];
            Float3 centre = vertices[first];
            for (int k = 0; k < 6; k++)
                Assert.Equal(centre, vertices[indices[quad * 6 + k]]);

            // Two triangles over all four corners
            var corners = new bool[4];
            for (int k = 0; k < 6; k++)
            {
                Float2 c = uv[indices[quad * 6 + k]];
                corners[(int)c.X + (int)c.Y * 2] = true;
            }
            Assert.All(corners, Assert.True);
        }
    }

    [Fact]
    public void Lattice_FollowsTheCameraInWholeCells_AndDriftsWithTheWind()
    {
        const float cell = 250f;
        Float2 wind = new(1234.5f, -987.25f);

        foreach (Float2 camera in new[] { new Float2(0f, 0f), new Float2(5123.7f, -812.4f), new Float2(-40000f, 33333f) })
        {
            Float2 origin = CloudRenderer.LatticeOrigin(camera, wind, cell);
            Assert.True(MathF.Abs(origin.X - camera.X) <= cell * 0.5f + 1e-2f);
            Assert.True(MathF.Abs(origin.Y - camera.Y) <= cell * 0.5f + 1e-2f);

            // Lattice points sit a whole number of cells from where the wind has carried the clouds
            float cellsX = (origin.X - wind.X) / cell, cellsZ = (origin.Y - wind.Y) / cell;
            Assert.Equal(MathF.Round(cellsX), cellsX, 3);
            Assert.Equal(MathF.Round(cellsZ), cellsZ, 3);
        }

        // A small gust carries the particles with it rather than snapping them
        Float2 still = CloudRenderer.LatticeOrigin(new Float2(10f, 20f), wind, cell);
        Float2 gust = new(30f, -40f);
        Float2 moved = CloudRenderer.LatticeOrigin(new Float2(10f, 20f), wind + gust, cell);
        Assert.Equal(still.X + gust.X, moved.X, 2);
        Assert.Equal(still.Y + gust.Y, moved.Y, 2);
    }
}
