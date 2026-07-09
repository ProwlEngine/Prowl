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
}
