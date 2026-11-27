// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public sealed class SpriteTests
{
    private const int Size = 32;

    private static byte[] Disc(float radius, float hole = 0)
    {
        byte[] alpha = new byte[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = x + 0.5f - Size / 2f, dy = y + 0.5f - Size / 2f;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                alpha[y * Size + x] = d <= radius && d >= hole ? (byte)255 : (byte)0;
            }
        return alpha;
    }

    private static bool Covered(SpriteMeshTracer.TracedMesh mesh, Float2 p)
    {
        for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            Float2 a = mesh.Vertices[mesh.Indices[i]], b = mesh.Vertices[mesh.Indices[i + 1]], c = mesh.Vertices[mesh.Indices[i + 2]];
            float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
            bool negative = d1 < -1e-4f || d2 < -1e-4f || d3 < -1e-4f;
            bool positive = d1 > 1e-4f || d2 > 1e-4f || d3 > 1e-4f;
            if (!(negative && positive)) return true;
        }
        return false;
    }

    private static float Cross(Float2 a, Float2 b, Float2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void TightMesh_CoversEveryOpaquePixel_EvenWithACoarseOutline(float extrude)
    {
        byte[] alpha = Disc(12);
        SpriteMeshTracer.TracedMesh mesh = SpriteMeshTracer.Generate(alpha, Size, Size, 1, simplifyTolerance: 3f, extrude: extrude);

        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (alpha[y * Size + x] != 0)
                    foreach (Float2 corner in (Float2[])[new(x, y), new(x + 1, y), new(x, y + 1), new(x + 1, y + 1)])
                        Assert.True(Covered(mesh, corner), $"Pixel ({x}, {y}) corner {corner} is outside the mesh");
    }

    [Fact]
    public void TightMesh_StaysInsideTheSpriteRect()
    {
        byte[] alpha = new byte[Size * Size];
        Array.Fill(alpha, (byte)255);

        SpriteMeshTracer.TracedMesh mesh = SpriteMeshTracer.Generate(alpha, Size, Size, 1, simplifyTolerance: 1.5f, extrude: 4f);

        Assert.All(mesh.Vertices, v => Assert.True(v.X >= 0 && v.Y >= 0 && v.X <= Size && v.Y <= Size, $"{v} is outside the rect"));
    }

    [Fact]
    public void TightMesh_KeepsHolesInTheOutline_WithoutTriangulatingThemTwice()
    {
        byte[] alpha = Disc(14, hole: 6);
        SpriteMeshTracer.TracedMesh mesh = SpriteMeshTracer.Generate(alpha, Size, Size);

        Assert.Equal(2, mesh.Contours.Count);
        int covering = 0;
        Float2 centre = new(Size / 2f, Size / 2f);
        for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            Float2 a = mesh.Vertices[mesh.Indices[i]], b = mesh.Vertices[mesh.Indices[i + 1]], c = mesh.Vertices[mesh.Indices[i + 2]];
            float d1 = Cross(a, b, centre), d2 = Cross(b, c, centre), d3 = Cross(c, a, centre);
            if ((d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0)) covering++;
        }
        Assert.True(covering <= 2, $"The hole's centre is drawn by {covering} triangles");
    }
}
