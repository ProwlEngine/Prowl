using System;

namespace Prowl.Graphite;

internal readonly record struct UniformFieldTarget(int Block, uint Offset, uint Size);

internal sealed class UniformBindingPlan
{
    public readonly uint[] BlockSizes;
    public readonly int[][] ElementBlock;
    public readonly int[][] SetBlocks;
    public readonly int[][] DynamicOffsetOrder;

    private readonly int[] _keys;
    private readonly int[] _first;
    private readonly int[] _count;
    private readonly int _mask;
    private readonly UniformFieldTarget[] _targets;

    private UniformBindingPlan(
        uint[] blockSizes, int[][] elementBlock, int[][] setBlocks, int[][] dynamicOffsetOrder,
        int[] keys, int[] first, int[] count, UniformFieldTarget[] targets)
    {
        BlockSizes = blockSizes;
        ElementBlock = elementBlock;
        SetBlocks = setBlocks;
        DynamicOffsetOrder = dynamicOffsetOrder;
        _keys = keys;
        _first = first;
        _count = count;
        _mask = keys.Length - 1;
        _targets = targets;
    }

    public int BlockCount => BlockSizes.Length;

    public bool TryFind(PropertyID name, out ReadOnlySpan<UniformFieldTarget> targets)
    {
        int slot = Hash(name.Value) & _mask;
        while (true)
        {
            int key = _keys[slot];
            if (key == name.Value)
            {
                targets = new ReadOnlySpan<UniformFieldTarget>(_targets, _first[slot], _count[slot]);
                return true;
            }

            if (key == 0)
            {
                targets = default;
                return false;
            }

            slot = (slot + 1) & _mask;
        }
    }

    public static UniformBindingPlan Build(ResourceLayoutDescription[] layouts)
    {
        int[][] elementBlock = new int[layouts.Length][];
        int[][] order = new int[layouts.Length][];
        int[][] setBlocks = new int[layouts.Length][];
        System.Collections.Generic.List<uint> sizes = new();
        System.Collections.Generic.List<(PropertyID Name, UniformFieldTarget Target)> fields = new();

        for (int s = 0; s < layouts.Length; s++)
        {
            ResourceLayoutElementDescription[] elements = layouts[s].Elements ?? Array.Empty<ResourceLayoutElementDescription>();
            elementBlock[s] = new int[elements.Length];

            int uboCount = 0;
            for (int i = 0; i < elements.Length; i++)
            {
                elementBlock[s][i] = -1;
                if (elements[i].Kind != ResourceKind.UniformBuffer)
                    continue;

                uboCount++;
                UniformBlockField[] blockFields = elements[i].UniformFields;
                if (layouts[s].Table || blockFields == null || blockFields.Length == 0)
                    continue;

                int block = sizes.Count;
                elementBlock[s][i] = block;
                sizes.Add(BlockSize(blockFields));
                foreach (UniformBlockField field in blockFields)
                {
                    if (!field.Name.IsValid)
                        continue;

                    fields.Add((field.Name, new UniformFieldTarget(block, field.Offset, field.Size)));
                }
            }

            setBlocks[s] = Array.FindAll(elementBlock[s], b => b >= 0);
            order[s] = new int[uboCount];
            int w = 0;
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].Kind == ResourceKind.UniformBuffer)
                    order[s][w++] = i;
            }

            Array.Sort(order[s], (a, b) => elements[a].BindingIndex.CompareTo(elements[b].BindingIndex));
        }

        fields.Sort((a, b) => a.Name.Value.CompareTo(b.Name.Value));

        int capacity = 4;
        while (capacity < fields.Count * 2)
            capacity <<= 1;

        int[] keys = new int[capacity];
        int[] first = new int[capacity];
        int[] count = new int[capacity];
        UniformFieldTarget[] targets = new UniformFieldTarget[fields.Count];
        int mask = capacity - 1;

        for (int i = 0; i < fields.Count; i++)
        {
            targets[i] = fields[i].Target;
            int id = fields[i].Name.Value;
            int slot = Hash(id) & mask;
            while (keys[slot] != 0 && keys[slot] != id)
                slot = (slot + 1) & mask;

            if (keys[slot] == 0)
            {
                keys[slot] = id;
                first[slot] = i;
            }

            count[slot]++;
        }

        return new UniformBindingPlan(sizes.ToArray(), elementBlock, setBlocks, order, keys, first, count, targets);
    }

    private static int Hash(int value) => (int)((uint)value * 2654435761u >> 8);

    private static uint BlockSize(UniformBlockField[] fields)
    {
        uint size = 0;
        foreach (UniformBlockField field in fields)
            size = Math.Max(size, field.Offset + field.Size);
        return size == 0 ? 16 : size;
    }
}
