using Prowl.Vector;

namespace Prowl.Unwrapper.Tests;

/// <summary>Hand-built procedural meshes used by the test suite.</summary>
internal static class Meshes
{
    public static (Double3[] Verts, int[] Tris) Quad()
    {
        var verts = new Double3[]
        {
            new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
        };
        var tris = new[] { 0, 1, 2, 0, 2, 3 };
        return (verts, tris);
    }

    public static (Double3[] Verts, int[] Tris) Cube()
    {
        var verts = new Double3[]
        {
            new(-1, -1, -1), new( 1, -1, -1), new( 1,  1, -1), new(-1,  1, -1),
            new(-1, -1,  1), new( 1, -1,  1), new( 1,  1,  1), new(-1,  1,  1),
        };
        var tris = new[]
        {
            0, 2, 1,  0, 3, 2,    // -Z
            4, 5, 6,  4, 6, 7,    // +Z
            0, 7, 3,  0, 4, 7,    // -X
            1, 2, 6,  1, 6, 5,    // +X
            0, 1, 5,  0, 5, 4,    // -Y
            3, 7, 6,  3, 6, 2,    // +Y
        };
        return (verts, tris);
    }

    public static (Double3[] Verts, int[] Tris) Octahedron()
    {
        var verts = new Double3[]
        {
            new( 1,  0,  0), new(-1,  0,  0),
            new( 0,  1,  0), new( 0, -1,  0),
            new( 0,  0,  1), new( 0,  0, -1),
        };
        var tris = new[]
        {
            0, 2, 4,  2, 1, 4,  1, 3, 4,  3, 0, 4,
            2, 0, 5,  1, 2, 5,  3, 1, 5,  0, 3, 5,
        };
        return (verts, tris);
    }

    /// <summary>Six quad faces each subdivided into NxN triangles.</summary>
    public static (Double3[] Verts, int[] Tris) SubdivCube(int n)
    {
        var verts = new List<Double3>();
        var tris = new List<int>();
        void Face(Double3 origin, Double3 u, Double3 v)
        {
            int baseI = verts.Count;
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    verts.Add(origin + (i / (double)n) * u + (j / (double)n) * v);
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i)
                {
                    int a = baseI + j * (n + 1) + i;
                    int b = a + 1, c = a + (n + 1), d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
        }
        Face(new(-1, -1, -1), new(2, 0, 0), new(0, 2, 0));
        Face(new(-1, -1,  1), new(2, 0, 0), new(0, 2, 0));
        Face(new(-1, -1, -1), new(0, 0, 2), new(0, 2, 0));
        Face(new( 1, -1, -1), new(0, 0, 2), new(0, 2, 0));
        Face(new(-1, -1, -1), new(2, 0, 0), new(0, 0, 2));
        Face(new(-1,  1, -1), new(2, 0, 0), new(0, 0, 2));
        return (verts.ToArray(), tris.ToArray());
    }

    /// <summary>Stacks/slices UV sphere — has zero-area pole triangles by construction.</summary>
    public static (Double3[] Verts, int[] Tris) UvSphere(int stacks, int slices)
    {
        var verts = new List<Double3>();
        var tris = new List<int>();
        for (int j = 0; j <= stacks; ++j)
        {
            double phi = System.Math.PI * j / stacks;
            for (int i = 0; i < slices; ++i)
            {
                double theta = 2 * System.Math.PI * i / slices;
                verts.Add(new Double3(
                    System.Math.Sin(phi) * System.Math.Cos(theta),
                    System.Math.Cos(phi),
                    System.Math.Sin(phi) * System.Math.Sin(theta)));
            }
        }
        int Idx(int j, int i) => j * slices + (i % slices);
        for (int j = 0; j < stacks; ++j)
            for (int i = 0; i < slices; ++i)
            {
                int a = Idx(j, i), b = Idx(j, i + 1);
                int c = Idx(j + 1, i), d = Idx(j + 1, i + 1);
                tris.Add(a); tris.Add(b); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(c);
            }
        return (verts.ToArray(), tris.ToArray());
    }
}
