namespace Prowl.Graphite;

/// <summary>Per-set binding metadata, precomputed once at program build so the per-draw binder skips redundant layout work.</summary>
internal sealed class SetBindingMetadata
{
    /// <summary>UBO element indices, sorted by binding index. Vulkan needs dynamic offsets in this order.</summary>
    public readonly int[] SortedUboElementIndices;

    /// <summary>Per element: true if some texture element in the set shares its name. Speeds up sampler lookup.</summary>
    public readonly bool[] HasSameNamedTexture;

    private SetBindingMetadata(int[] sortedUboElementIndices, bool[] hasSameNamedTexture)
    {
        SortedUboElementIndices = sortedUboElementIndices;
        HasSameNamedTexture = hasSameNamedTexture;
    }

    /// <summary>Builds metadata, one entry per set, parallel to layouts.</summary>
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

            // Insertion sort by binding index; UBO counts per set are tiny.
            for (int i = 1; i < sortedUbo.Length; i++)
            {
                int key = sortedUbo[i];
                int keyBinding = elements[key].BindingIndex;
                int j = i - 1;
                while (j >= 0 && elements[sortedUbo[j]].BindingIndex > keyBinding)
                {
                    sortedUbo[j + 1] = sortedUbo[j];
                    j--;
                }
                sortedUbo[j + 1] = key;
            }

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

            result[s] = new SetBindingMetadata(sortedUbo, hasSameNamedTexture);
        }

        return result;
    }
}
