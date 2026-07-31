using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// Hands out per assembly synthetic indexes with this reload's metadata reader attached. The indexes
/// themselves are cached for the life of the assembly; only the reader is per reload, because it depends on
/// the caller's byte resolver.
/// </summary>
internal sealed class SyntheticIndexes
{
    private readonly MetadataCache _metadata;

    public SyntheticIndexes(MetadataCache metadata) => _metadata = metadata;

    public SyntheticIndex For(Assembly assembly)
    {
        var index = SyntheticIndex.For(assembly);
        index.UseMetadata(_metadata.For(assembly));
        return index;
    }
}
