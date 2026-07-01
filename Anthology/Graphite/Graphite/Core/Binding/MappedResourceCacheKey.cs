using System;

namespace Prowl.Graphite;

internal readonly struct MappedResourceCacheKey : IEquatable<MappedResourceCacheKey>
{
    public readonly MappableResource Resource;
    public readonly uint Subresource;

    public MappedResourceCacheKey(MappableResource resource, uint subresource)
    {
        Resource = resource;
        Subresource = subresource;
    }


    public readonly bool Equals(MappedResourceCacheKey other)
        => Resource.Equals(other.Resource) && Subresource.Equals(other.Subresource);

    public override readonly int GetHashCode()
        => HashCode.Combine(Resource.GetHashCode(), Subresource.GetHashCode());

    public override readonly bool Equals(object? obj)
        => obj is MappedResourceCacheKey key && Equals(key);
}
