using System.Collections.Generic;

namespace Prowl.Graphite;

internal static class GraphSorter
{
    public static int[] Sort(
        IReadOnlyList<IPass> passes,
        ResourceAccess[][] accesses,
        IReadOnlyDictionary<RenderResourceID, GraphResource> resources)
    {
        int count = passes.Count;
        var writers = new Dictionary<RenderResourceID, List<int>>();
        var readers = new Dictionary<RenderResourceID, List<int>>();
        var written = new HashSet<RenderResourceID>();
        var seen = new HashSet<RenderResourceID>();
        for (int i = 0; i < count; i++)
        {
            written.Clear();
            seen.Clear();
            foreach (ResourceAccess access in accesses[i])
            {
                if (access.IsOutput && written.Add(access.Id))
                    Add(writers, access.Id, i);
            }

            foreach (ResourceAccess access in accesses[i])
            {
                if (!access.IsOutput && !written.Contains(access.Id) && seen.Add(access.Id))
                    Add(readers, access.Id, i);
            }
        }

        var next = new List<int>[count];
        var indegree = new int[count];
        var edges = new HashSet<long>();
        for (int i = 0; i < count; i++)
            next[i] = new List<int>();

        void Edge(int from, int to)
        {
            if (from != to && edges.Add(((long)from << 32) | (uint)to))
            {
                next[from].Add(to);
                indegree[to]++;
            }
        }

        foreach ((RenderResourceID id, List<int> chain) in writers)
        {
            for (int k = 0; k + 1 < chain.Count; k++)
                Edge(chain[k], chain[k + 1]);

            if (!readers.TryGetValue(id, out List<int>? reading))
                continue;

            bool transient = chain.Count == 1 && IsTransient(resources[id]);
            bool retained = !IsTransient(resources[id]);
            foreach (int reader in reading)
            {
                if (passes[reader].Kind != passes[chain[0]].Kind)
                {
                    Edge(chain[^1], reader);
                    continue;
                }

                if (transient)
                {
                    Edge(chain[0], reader);
                    continue;
                }

                int before = -1;
                while (before + 1 < chain.Count && chain[before + 1] < reader)
                    before++;

                if (before >= 0)
                {
                    Edge(chain[before], reader);
                    if (before + 1 < chain.Count)
                        Edge(reader, chain[before + 1]);
                }
                else if (retained)
                {
                    Edge(reader, chain[0]);
                }
                else
                {
                    Edge(chain[0], reader);
                    if (chain.Count > 1)
                        Edge(reader, chain[1]);
                }
            }
        }

        var ready = new PriorityQueue<int, int>();
        for (int i = 0; i < count; i++)
        {
            if (indegree[i] == 0)
                ready.Enqueue(i, i);
        }

        var order = new int[count];
        int emitted = 0;
        while (ready.TryDequeue(out int node, out _))
        {
            order[emitted++] = node;
            foreach (int dependent in next[node])
            {
                if (--indegree[dependent] == 0)
                    ready.Enqueue(dependent, dependent);
            }
        }

        if (emitted < count)
        {
            var stuck = new List<string>();
            for (int i = 0; i < count; i++)
            {
                if (indegree[i] > 0)
                    stuck.Add($"'{passes[i].Name}'");
            }
            throw new RenderGraphException($"Render graph has a cyclic resource dependency among passes {string.Join(", ", stuck)} and cannot be ordered.", null);
        }

        return order;
    }

    private static void Add(Dictionary<RenderResourceID, List<int>> map, RenderResourceID id, int pass)
    {
        if (!map.TryGetValue(id, out List<int>? list))
            map[id] = list = new List<int>();
        list.Add(pass);
    }

    internal static bool IsTransient(GraphResource resource)
        => resource is GraphTextureResource { Retained: false } or GraphBufferResource { Retained: false };
}
