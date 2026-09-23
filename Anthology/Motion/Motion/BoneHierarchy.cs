using System.Collections.Generic;

namespace Prowl.Motion;

/// <summary>
/// A parents first evaluation order for a bone hierarchy. Parent links that are out of range or that
/// close a cycle are cut, so the bone becomes a root and model space evaluation always terminates.
/// </summary>
internal sealed class BoneHierarchy
{
    public BoneHierarchy(IReadOnlyList<int> parents)
    {
        int count = parents.Count;
        Parents = new int[count];
        Order = new int[count];
        var state = new byte[count]; // 0 unvisited, 1 on the current walk, 2 emitted
        var chain = new int[count];
        int written = 0;

        for (int i = 0; i < count; i++)
        {
            int p = parents[i];
            Parents[i] = p >= 0 && p < count && p != i ? p : -1;
        }

        for (int start = 0; start < count; start++)
        {
            if (state[start] == 2)
                continue;

            int depth = 0;
            int bone = start;
            while (bone >= 0 && state[bone] == 0)
            {
                state[bone] = 1;
                chain[depth++] = bone;
                bone = Parents[bone];
            }

            if (bone >= 0 && state[bone] == 1)
            {
                HasCycle = true;
                Parents[chain[depth - 1]] = -1;
            }

            for (int i = depth - 1; i >= 0; i--)
            {
                Order[written++] = chain[i];
                state[chain[i]] = 2;
            }
        }

        BuildSubtreeOrder(count);
    }

    /// <summary>
    /// Every bone depth first, so each bone is followed directly by all of its descendants. A bone's
    /// descendants are the positions from <see cref="SubtreeStart"/> up to <see cref="SubtreeEnd"/>.
    /// </summary>
    public int[] SubtreeOrder { get; private set; } = null!;

    /// <summary>A bone's position in <see cref="SubtreeOrder"/>.</summary>
    public int[] SubtreeStart { get; private set; } = null!;

    /// <summary>The position just past a bone's last descendant in <see cref="SubtreeOrder"/>.</summary>
    public int[] SubtreeEnd { get; private set; } = null!;

    private void BuildSubtreeOrder(int count)
    {
        var childCount = new int[count + 1];
        for (int i = 0; i < count; i++)
            if (Parents[i] >= 0)
                childCount[Parents[i] + 1]++;
        for (int i = 0; i < count; i++)
            childCount[i + 1] += childCount[i];

        var children = new int[count];
        var fill = (int[])childCount.Clone();
        for (int i = 0; i < count; i++)
            if (Parents[i] >= 0)
                children[fill[Parents[i]]++] = i;

        SubtreeOrder = new int[count];
        SubtreeStart = new int[count];
        SubtreeEnd = new int[count];
        var stack = new int[count];
        int written = 0;
        for (int root = 0; root < count; root++)
        {
            if (Parents[root] >= 0)
                continue;

            int depth = 0;
            stack[depth++] = root;
            while (depth > 0)
            {
                int bone = stack[--depth];
                SubtreeStart[bone] = written;
                SubtreeOrder[written++] = bone;
                for (int c = childCount[bone + 1] - 1; c >= childCount[bone]; c--)
                    stack[depth++] = children[c];
            }
        }

        for (int k = count - 1; k >= 0; k--)
        {
            int bone = SubtreeOrder[k];
            if (SubtreeEnd[bone] == 0)
                SubtreeEnd[bone] = k + 1;
            int parent = Parents[bone];
            if (parent >= 0 && SubtreeEnd[parent] < SubtreeEnd[bone])
                SubtreeEnd[parent] = SubtreeEnd[bone];
        }
    }

    /// <summary>Every bone, each parent before its children.</summary>
    public int[] Order { get; }

    /// <summary>Parent indices with invalid and cycle closing links replaced by -1.</summary>
    public int[] Parents { get; }

    /// <summary>True if the source parent links contained a cycle.</summary>
    public bool HasCycle { get; }

    /// <summary>The evaluation order restricted to the first <paramref name="count"/> bones and their ancestors.</summary>
    public int[] SubsetOrder(int count)
    {
        var needed = new bool[Parents.Length];
        int neededCount = 0;
        for (int i = 0; i < Math.Min(count, Parents.Length); i++)
        {
            for (int bone = i; bone >= 0 && !needed[bone]; bone = Parents[bone])
            {
                needed[bone] = true;
                neededCount++;
            }
        }

        var subset = new int[neededCount];
        int written = 0;
        foreach (int bone in Order)
            if (needed[bone])
                subset[written++] = bone;
        return subset;
    }
}
