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
        var m = new Mesh { Vertices = new[] { new Float3(200000, 200000, 200000), new Float3(200001, 200002, 200003) } };
        m.RecalculateBounds();

        Assert.Equal(200000f, m.bounds.Min.X, 1);
        Assert.Equal(200001f, m.bounds.Max.X, 1);
        Assert.Equal(200003f, m.bounds.Max.Z, 1);
    }

    // Serializing a vertex-less mesh must not throw.
    [Fact]
    public void Serialize_EmptyMesh_DoesNotThrow()
    {
        var ex = Record.Exception(() => Serializer.Serialize(new Mesh()));
        Assert.Null(ex);
    }

    // Every attribute lands in the blob interleaved per vertex, in the order GetVertexLayout declares.
    [Fact]
    public void VertexBlob_InterleavesEveryAttributeInLayoutOrder()
    {
        var m = new Mesh { Vertices = [new Float3(1, 2, 3), new Float3(4, 5, 6)] };
        m.UV = [new Float2(0.1f, 0.2f), new Float2(0.3f, 0.4f)];
        m.UV2 = [new Float2(0.5f, 0.6f), new Float2(0.7f, 0.8f)];
        m.Normals = [new Float3(0, 1, 0), new Float3(1, 0, 0)];
        m.Colors32 = [new Color32(255, 0, 0, 255), new Color32(0, 128, 255, 64)];
        m.Tangents = [new Float4(1, 0, 0, 1), new Float4(0, 0, 1, -1)];
        m.BoneIndices = [new Float4(0, 1, 2, 3), new Float4(4, 5, 6, 7)];
        m.BoneWeights = [new Float4(0.4f, 0.3f, 0.2f, 0.1f), new Float4(1, 0, 0, 0)];

        var expected = new List<float>();
        for (int i = 0; i < 2; i++)
        {
            Color c = (Color)m.Colors32[i];
            expected.AddRange([m.Vertices[i].X, m.Vertices[i].Y, m.Vertices[i].Z]);
            expected.AddRange([m.UV[i].X, m.UV[i].Y, m.UV2[i].X, m.UV2[i].Y]);
            expected.AddRange([m.Normals[i].X, m.Normals[i].Y, m.Normals[i].Z]);
            expected.AddRange([c.R, c.G, c.B, c.A]);
            expected.AddRange([m.Tangents[i].X, m.Tangents[i].Y, m.Tangents[i].Z, m.Tangents[i].W]);
            expected.AddRange([m.BoneIndices[i].X, m.BoneIndices[i].Y, m.BoneIndices[i].Z, m.BoneIndices[i].W]);
            expected.AddRange([m.BoneWeights[i].X, m.BoneWeights[i].Y, m.BoneWeights[i].Z, m.BoneWeights[i].W]);
        }

        byte[] blob = m.MakeVertexDataBlob(Mesh.GetVertexLayout(m), out int length);
        float[] actual = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(blob.AsSpan(0, length)).ToArray();
        System.Buffers.ArrayPool<byte>.Shared.Return(blob);

        Assert.Equal(expected, actual);
    }

    // Gizmos are redrawn every frame; identical ones must reuse the mesh untouched so nothing is uploaded again.
    [Fact]
    public void GizmoMesh_OnlyRebuildsWhenTheGizmosChange()
    {
        var gizmos = new GizmoBuilder();
        gizmos.DrawLine(Float3.Zero, Float3.UnitX, Color.Red);
        var (first, _) = gizmos.UpdateMesh();
        Float3[] firstVertices = first!.Vertices;

        gizmos.Clear();
        gizmos.DrawLine(Float3.Zero, Float3.UnitX, Color.Red);
        var (same, _) = gizmos.UpdateMesh();
        Assert.Same(first, same);
        Assert.Same(firstVertices, same!.Vertices);

        gizmos.Clear();
        gizmos.DrawLine(Float3.Zero, Float3.UnitY, Color.Red);
        var (changed, _) = gizmos.UpdateMesh();
        Assert.NotSame(firstVertices, changed!.Vertices);
        Assert.Equal(Float3.UnitY, changed.Vertices[1]);
    }
}
