using System;

namespace Prowl.Graphite;

internal sealed class SetBindingMetadata
{
    /// <summary>UBO element indices sorted by binding; needed for Vulkan dynamic offsets.</summary>
    public readonly int[] SortedUboElementIndices;

    /// <summary>True if texture shares name in set; optimizes sampler lookup.</summary>
    public readonly bool[] HasSameNamedTexture;

    /// <summary>Packed byte size of each element's loose uniform block, 0 if it declares none.</summary>
    public readonly uint[] UniformBlockSizes;

    /// <summary>Every property name the set reads: element names plus loose uniform field names.</summary>
    public readonly System.Collections.Generic.HashSet<PropertyID> Names;

    /// <summary>True if any element is a read-write texture, which can need a per-dispatch layout move.</summary>
    public readonly bool HasStorageTexture;

    private SetBindingMetadata(
        int[] sortedUboElementIndices, bool[] hasSameNamedTexture, uint[] uniformBlockSizes,
        System.Collections.Generic.HashSet<PropertyID> names, bool hasStorageTexture)
    {
        SortedUboElementIndices = sortedUboElementIndices;
        HasSameNamedTexture = hasSameNamedTexture;
        UniformBlockSizes = uniformBlockSizes;
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

            int uboCount = 0;
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].Kind == ResourceKind.UniformBuffer)
                    uboCount++;
            }

            int[] sortedUbo = new int[uboCount];
            int w = 0;
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].Kind == ResourceKind.UniformBuffer)
                    sortedUbo[w++] = i;
            }

            Array.Sort(sortedUbo, (a, b) => elements[a].BindingIndex.CompareTo(elements[b].BindingIndex));

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

            uint[] blockSizes = new uint[elements.Length];
            for (int i = 0; i < elements.Length; i++)
            {
                UniformBlockField[] fields = elements[i].UniformFields;
                if (elements[i].Kind != ResourceKind.UniformBuffer || fields == null || fields.Length == 0)
                    continue;

                blockSizes[i] = UniformBlockSize(fields);
            }

            System.Collections.Generic.HashSet<PropertyID> names = new();
            bool hasStorageTexture = false;
            foreach (ResourceLayoutElementDescription element in elements)
            {
                names.Add(element.Name);
                if (element.Kind == ResourceKind.TextureReadWrite)
                    hasStorageTexture = true;
                if (element.UniformFields != null)
                {
                    foreach (UniformBlockField field in element.UniformFields)
                        names.Add(field.Name);
                }
            }

            result[s] = new SetBindingMetadata(sortedUbo, hasSameNamedTexture, blockSizes, names, hasStorageTexture);
        }

        return result;
    }

    private static uint UniformBlockSize(UniformBlockField[] fields)
    {
        uint size = 0;
        foreach (UniformBlockField field in fields)
            size = System.Math.Max(size, field.Offset + field.Size);
        return size == 0 ? 16 : size;
    }
}
