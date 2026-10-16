using System;

namespace Prowl.Graphite;

internal sealed class SetBindingMetadata
{
    /// <summary>True if texture shares name in set; optimizes sampler lookup.</summary>
    public readonly bool[] HasSameNamedTexture;

    /// <summary>Every resource name the set reads.</summary>
    public readonly System.Collections.Generic.HashSet<PropertyID> Names;

    /// <summary>True if any element is a read-write texture, which can need a per-dispatch layout move.</summary>
    public readonly bool HasStorageTexture;

    private SetBindingMetadata(
        bool[] hasSameNamedTexture,
        System.Collections.Generic.HashSet<PropertyID> names, bool hasStorageTexture)
    {
        HasSameNamedTexture = hasSameNamedTexture;
        Names = names;
        HasStorageTexture = hasStorageTexture;
    }

    /// <summary>True if any of the keys is a name this set reads.</summary>
    public bool ReadsAny(System.Collections.Generic.List<PropertyID> keys)
    {
        foreach (PropertyID key in keys)
        {
            if (Names.Contains(key))
                return true;
        }
        return false;
    }

    /// <summary>Build metadata one per set, parallel to layouts.</summary>
    public static SetBindingMetadata[] Build(ResourceLayoutDescription[] layouts)
    {
        SetBindingMetadata[] result = new SetBindingMetadata[layouts.Length];

        for (int s = 0; s < layouts.Length; s++)
        {
            ResourceLayoutElementDescription[] elements = layouts[s].Elements ?? System.Array.Empty<ResourceLayoutElementDescription>();

            bool[] hasSameNamedTexture = new bool[elements.Length];
            for (int i = 0; i < elements.Length; i++)
            {
                PropertyID name = elements[i].Name;
                for (int j = 0; j < elements.Length; j++)
                {
                    if ((elements[j].Kind == ResourceKind.TextureReadOnly || elements[j].Kind == ResourceKind.TextureReadWrite)
                        && elements[j].Name == name)
                    {
                        hasSameNamedTexture[i] = true;
                        break;
                    }
                }
            }

            System.Collections.Generic.HashSet<PropertyID> names = new();
            bool hasStorageTexture = false;
            foreach (ResourceLayoutElementDescription element in elements)
            {
                names.Add(element.Name);
                if (element.Kind == ResourceKind.TextureReadWrite)
                    hasStorageTexture = true;
            }

            result[s] = new SetBindingMetadata(hasSameNamedTexture, names, hasStorageTexture);
        }

        return result;
    }
}
