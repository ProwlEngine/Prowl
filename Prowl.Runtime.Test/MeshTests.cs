// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Tests for <see cref="Mesh"/> CPU-side data (bounds, serialization).</summary>
public class MeshTests
{
    // Bounds must be correct for vertices far outside the old +/-99999 seed range.
    [Fact]
    public void RecalculateBounds_HandlesVerticesBeyond99999()
    {
        var m = new Mesh { Vertices = [new Float3(200000, 200000, 200000), new Float3(200001, 200002, 200003)] };
        m.RecalculateBounds();

        Assert.Equal(200000f, m.bounds.Min.X, 1);
        Assert.Equal(200001f, m.bounds.Max.X, 1);
        Assert.Equal(200003f, m.bounds.Max.Z, 1);
    }

    // Serializing a vertex-less mesh must not throw.
    [Fact]
    public void Serialize_EmptyMesh_DoesNotThrow()
    {
        Exception ex = Record.Exception(() => Serializer.Serialize(new Mesh()));
        Assert.Null(ex);
    }

    // The seam's duplicated column shares one normal, so lighting has no hard edge there.
    [Fact]
    public void CreateSphere_SeamVerticesShareNormals()
    {
        const int rings = 12, slices = 16;
        Mesh sphere = Mesh.CreateSphere(2f, rings, slices);
        for (int ring = 1; ring < rings; ring++)
        {
            int first = ring * (slices + 1), last = first + slices;
            Assert.True(Float3.Distance(sphere.Normals[first], sphere.Normals[last]) < 1e-5f, $"ring {ring}");
            Assert.True(Float4.Distance(sphere.Tangents[first], sphere.Tangents[last]) < 1e-5f, $"ring {ring}");
        }
    }

    // Away from the seam the written tangents match the ones worked out from the UVs, handedness included.
    [Fact]
    public void CreateSphere_TangentsMatchUVs()
    {
        const int rings = 12, slices = 16;
        Mesh sphere = Mesh.CreateSphere(1f, rings, slices);
        Float4[] written = sphere.Tangents;
        sphere.RecalculateTangents();
        int vertex = 6 * (slices + 1) + 5;
        Assert.True(Float3.Dot(written[vertex].XYZ, sphere.Tangents[vertex].XYZ) > 0.99f);
        Assert.Equal(sphere.Tangents[vertex].W, written[vertex].W);
    }
}
