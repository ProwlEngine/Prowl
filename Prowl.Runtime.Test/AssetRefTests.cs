// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Tests for <see cref="AssetRef{T}"/> identity semantics.</summary>
public class AssetRefTests
{
    // Equality/hash must use the resolved AssetID, so a ref built from an instance whose AssetID is
    // assigned after construction still equals a ref built from that GUID.
    [Fact]
    public void Equality_ConsistentAfterInstanceAssetIdAssigned()
    {
        var mesh = new Mesh();
        var fromInstance = new AssetRef<Mesh>(mesh);

        var id = Guid.NewGuid();
        mesh.AssetID = id; // assigned after the ref was created

        var fromGuid = new AssetRef<Mesh>(id);

        Assert.True(fromInstance == fromGuid, "Refs denoting the same asset must be equal.");
        Assert.Equal(fromInstance.GetHashCode(), fromGuid.GetHashCode());
    }
}
