namespace Prowl.Graphite;

/// <summary>
/// Precomputed, immutable per-set binding metadata derived once at program build time from a
/// <see cref="ResourceLayoutDescription"/>. Lets the per-draw binder avoid work that only depends on
/// the static layout: gathering dynamic UBO offsets in binding order, and pairing samplers with
/// their same-named texture element.
/// </summary>
internal sealed class SetBindingMetadata
{
    /// <summary>
    /// Element indices (into the set's <see cref="ResourceLayoutDescription.Elements"/>) of every
    /// <see cref="ResourceKind.UniformBuffer"/> element, sorted ascending by
    /// <see cref="ResourceLayoutElementDescription.BindingIndex"/>. Vulkan requires dynamic offsets
    /// in binding-number order; this is that order, computed once.
    /// </summary>
    public readonly int[] SortedUboElementIndices;

    /// <summary>
    /// Per element index: true if a texture element (read-only or read-write) in the same set shares
    /// this element's <see cref="ResourceLayoutElementDescription.Name"/>. Lets sampler resolution
    /// (a standalone sampler paired with a texture, or a combined image-sampler element) source its
    /// sampler from the <c>SetTexture(name, _, sampler)</c> entry without scanning the set each draw.
    /// </summary>
    public readonly bool[] HasSameNamedTexture;

    private SetBindingMetadata(int[] sortedUboElementIndices, bool[] hasSameNamedTexture)
    {
        SortedUboElementIndices = sortedUboElementIndices;
        HasSameNamedTexture = hasSameNamedTexture;
    }

    /// <summary>Builds the metadata array, one entry per set, indexed parallel to <paramref name="layouts"/>.</summary>
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
