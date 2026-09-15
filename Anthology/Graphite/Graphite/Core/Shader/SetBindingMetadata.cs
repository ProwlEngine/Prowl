using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Prowl.Graphite;

internal sealed class SetBindingMetadata
{
    private static int s_nextUniformBlockSlot;
    private static readonly ConcurrentBag<int> s_freeUniformBlockSlots = [];

    /// <summary>UBO element indices sorted by binding; needed for Vulkan dynamic offsets.</summary>
    public readonly int[] SortedUboElementIndices;

    /// <summary>True if texture shares name in set; optimizes sampler lookup.</summary>
    public readonly bool[] HasSameNamedTexture;

    /// <summary>
    /// Process-unique slot per element declaring loose uniform fields, -1 otherwise. Identifies the block
    /// across programs so a per-execution uniform cache can be a flat array instead of a keyed lookup.
    /// </summary>
    public readonly int[] UniformBlockSlots;

    /// <summary>Packed byte size of each element's loose uniform block, 0 if it declares none.</summary>
    public readonly uint[] UniformBlockSizes;

    private SetBindingMetadata(int[] sortedUboElementIndices, bool[] hasSameNamedTexture, int[] uniformBlockSlots, uint[] uniformBlockSizes)
    {
        SortedUboElementIndices = sortedUboElementIndices;
        HasSameNamedTexture = hasSameNamedTexture;
        UniformBlockSlots = uniformBlockSlots;
        UniformBlockSizes = uniformBlockSizes;
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

            int[] blockSlots = new int[elements.Length];
            uint[] blockSizes = new uint[elements.Length];
            for (int i = 0; i < elements.Length; i++)
            {
                UniformBlockField[] fields = elements[i].UniformFields;
                if (elements[i].Kind != ResourceKind.UniformBuffer || fields == null || fields.Length == 0)
                {
                    blockSlots[i] = -1;
                    continue;
                }

                blockSlots[i] = s_freeUniformBlockSlots.TryTake(out int free)
                    ? free
                    : Interlocked.Increment(ref s_nextUniformBlockSlot) - 1;
                blockSizes[i] = UniformBlockSize(fields);
            }

            result[s] = new SetBindingMetadata(sortedUbo, hasSameNamedTexture, blockSlots, blockSizes);
        }

        return result;
    }

    /// <summary>Returns this set's block slots for reuse. Call once, when the owning program is disposed.</summary>
    public void ReleaseUniformBlockSlots()
    {
        foreach (int slot in UniformBlockSlots)
        {
            if (slot >= 0)
                s_freeUniformBlockSlots.Add(slot);
        }
    }

    private static uint UniformBlockSize(UniformBlockField[] fields)
    {
        uint size = 0;
        foreach (UniformBlockField field in fields)
            size = System.Math.Max(size, field.Offset + field.Size);
        return size == 0 ? 16 : size;
    }
}
