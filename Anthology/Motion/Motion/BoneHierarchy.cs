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
