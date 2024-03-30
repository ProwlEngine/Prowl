/*
 * Copyright (c) Thorben Linneweber and others
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Jitter2.DataStructures;
using Jitter2.LinearMath;
using Jitter2.Parallelization;

namespace Jitter2.Collision;

/// <summary>
/// Represents a dynamic Axis Aligned Bounding Box (AABB) tree. A hashset (refer to <see cref="PairHashSet"/>)
/// maintains a record of potential overlapping pairs.
/// </summary>
/// <typeparam name="T">The type of elements stored in the dynamic tree.</typeparam>
public class DynamicTree<T> where T : class, IDynamicTreeProxy, IListIndex
{
    private SlimBag<T>[] lists = Array.Empty<SlimBag<T>>();

    private readonly ActiveList<T> activeList;

    /// <summary>
    /// Gets the PairHashSet that contains pairs representing potential collisions. This should not be modified directly.
    /// </summary>
    public readonly PairHashSet PotentialPairs = new();

    public const int NullNode = -1;
    public const int InitialSize = 1024;

    /// <summary>
    /// Specifies the factor by which the bounding box in the dynamic tree structure is expanded. The expansion is calculated as
    /// <see cref="IDynamicTreeProxy.Velocity"/> * ExpandFactor * alpha, where alpha is a pseudo-random number in the range [1,2].
    /// </summary>
    public const float ExpandFactor = 0.1f;

    /// <summary>
    /// Specifies a small additional expansion of the bounding box in the AABB tree structure to prevent
    /// the creation of bounding boxes with zero volume.
    /// </summary>
    public const float ExpandEps = 0.01f;

    /// <summary>
    /// Represents a node in the AABB tree.
    /// </summary>
    public struct Node
    {
        public int Left, Right;
        public int Parent;
        public int Height;

        public JBBox ExpandedBox;
        public T Proxy;

        public bool ForceUpdate;

        public bool IsLeaf
        {
            readonly get => Left == NullNode;
            set => Left = value ? NullNode : Left;
        }
    }

    public Node[] Nodes = new Node[InitialSize];
    private readonly Stack<int> freeNodes = new();
    private int nodePointer = -1;
    private int root = NullNode;

    /// <summary>
    /// Gets the root of the dynamic tree.
    /// </summary>
    public int Root => root;

    private readonly Action<Parallel.Batch> scanOverlapsPre;
    private readonly Action<Parallel.Batch> scanOverlapsPost;

    public Func<T, T, bool> Filter { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicTree{T}"/> class.
    /// </summary>
    /// <param name="activeList">Active entities that are considered for updates during <see cref="Update(bool)"/>.</param>
    /// <param name="filter">A collision filter function, used in Jitter to exclude collisions between Shapes belonging to the same body. The collision is filtered out if the function returns false.</param>
    public DynamicTree(ActiveList<T> activeList, Func<T, T, bool> filter)
    {
        this.activeList = activeList;

        scanOverlapsPre = batch =>
        {
            ScanForMovedProxies(batch);
            ScanForOverlaps(batch.BatchIndex, false);
        };

        scanOverlapsPost = batch => { ScanForOverlaps(batch.BatchIndex, true); };

        this.Filter = filter;
    }

    public enum Timings
    {
        ScanOverlapsPre,
        UpdateProxies,
        ScanOverlapsPost,
        Last
    }

    public readonly double[] DebugTimings = new double[(int)Timings.Last];

    private int updatedProxies;

    /// <summary>
    /// Updates the state of the specified entity within the dynamic tree structure.
    /// </summary>
    /// <param name="shape">The entity to update.</param>
    public void Update(T shape)
    {
        OverlapCheck(shape, false);
        InternalRemoveProxy(shape);
        InternalAddProxy(shape);
        OverlapCheck(shape, true);
    }

    /// <summary>
    /// Gets the number of updated proxies.
    /// </summary>
    public int UpdatedProxies => updatedProxies;

    /// <summary>
    /// Updates all entities that are marked as active in the active list.
    /// </summary>
    /// <param name="multiThread">A boolean indicating whether to perform a multi-threaded update.</param>
    public void Update(bool multiThread)
    {
        long time = Stopwatch.GetTimestamp();
        double invFrequency = 1.0d / Stopwatch.Frequency;

        CheckBagCount();

        void SetTime(Timings type)
        {
            long ctime = Stopwatch.GetTimestamp();
            double delta = (ctime - time) * 1000.0d;
            DebugTimings[(int)type] = delta * invFrequency;
            time = ctime;
        }

        SetTime(Timings.ScanOverlapsPre);

        if (multiThread)
        {
            const int TaskThreshold = 24;
            int numTasks = Math.Clamp(activeList.Active / TaskThreshold, 1, ThreadPool.Instance.ThreadCount);
            Parallel.ForBatch(0, activeList.Active, numTasks, scanOverlapsPre);

            SetTime(Timings.ScanOverlapsPre);

            updatedProxies = 0;

            for (int ntask = 0; ntask < numTasks; ntask++)
            {
                var sl = lists[ntask];
                updatedProxies += sl.Count;

                for (int i = 0; i < sl.Count; i++)
                {
                    T proxy = sl[i];
                    InternalRemoveProxy(proxy);
                    InternalAddProxy(proxy);
                }
            }

            SetTime(Timings.UpdateProxies);

            Parallel.ForBatch(0, activeList.Active, numTasks, scanOverlapsPost);

            SetTime(Timings.ScanOverlapsPost);
        }
        else
        {
            scanOverlapsPre(new Parallel.Batch(0, activeList.Active));
            SetTime(Timings.ScanOverlapsPre);

            var sl = lists[0];
            for (int i = 0; i < sl.Count; i++)
            {
                T proxy = sl[i];
                InternalRemoveProxy(proxy);
                InternalAddProxy(proxy);
            }

            SetTime(Timings.UpdateProxies);

            scanOverlapsPost(new Parallel.Batch(0, activeList.Active));
            SetTime(Timings.ScanOverlapsPost);
        }
    }

    /// <summary>
    /// Add an entity to the tree.
    /// </summary>
    public void AddProxy(T proxy)
    {
        InternalAddProxy(proxy);
        OverlapCheck(root, proxy.NodePtr, true);
    }

    /// <summary>
    /// Removes an entity from the tree.
    /// </summary>
    public void RemoveProxy(T proxy)
    {
        OverlapCheck(root, proxy.NodePtr, false);
        InternalRemoveProxy(proxy);
        proxy.NodePtr = NullNode;
    }

    /// <summary>
    /// Clears all entities from the tree.
    /// </summary>
    public void Clear()
    {
        nodePointer = -1;
        root = NullNode;
        activeList.Clear();
        PotentialPairs.Clear();
    }

    /// <summary>
    /// Calculates the cost function of the tree.
    /// </summary>
    /// <returns>The calculated cost.</returns>
    public double CalculateCost()
    {
        return Cost(ref Nodes[root]);
    }

    /// <summary>
    /// Calculates the height of the tree.
    /// </summary>
    /// <returns>The calculated height.</returns>
    public double CalculateHeight()
    {
        int calcHeight = Height(ref Nodes[root]);
        Debug.Assert(calcHeight == Nodes[root].Height);
        return calcHeight;
    }

    /// <summary>
    /// Enumerates all axis-aligned bounding boxes in the tree.
    /// </summary>
    /// <param name="action">The action to perform on each bounding box and node height in the tree.</param>
    public void EnumerateAll(Action<JBBox, int> action)
    {
        if (root == -1) return;
        EnumerateAll(ref Nodes[root], action);
    }

    /// <summary>
    /// Forces an update for the specified proxy during the next tree update. This update process identifies all overlaps with the current extended box and removes all detections from the <see cref="PairHashSet"/>, followed by an update of the extended box and overlap detection, adding new overlaps back to the <see cref="PairHashSet"/>. Essentially, this resets the state of the entity within the <see cref="PairHashSet"/>. Jitter utilizes this method to reintegrate entities into the hashset that were previously pruned due to inactivity.
    /// </summary>
    /// <param name="proxy">The proxy to update.</param>
    public void ForceUpdate(T proxy)
    {
        Nodes[proxy.NodePtr].ForceUpdate = true;
    }

    [ThreadStatic] private static Stack<int>? stack;

    /// <summary>
    /// Queries the tree to find entities within the specified axis-aligned bounding box.
    /// </summary>
    /// <param name="hits">A list to store the entities found within the bounding box.</param>
    /// <param name="aabb">The axis-aligned bounding box used for the query.</param>
    public void Query(List<T> hits, in JBBox aabb)
    {
        stack ??= new Stack<int>(256);

        stack.Push(root);

        while (stack.Count > 0)
        {
            int index = stack.Pop();

            Node node = Nodes[index];

            if (node.IsLeaf)
            {
                hits.Add(node.Proxy);
            }
            else
            {
                int child1 = Nodes[index].Left;
                int child2 = Nodes[index].Right;

                if (Nodes[child1].ExpandedBox.NotDisjoint(aabb))
                    stack.Push(child1);

                if (Nodes[child2].ExpandedBox.NotDisjoint(aabb))
                    stack.Push(child2);
            }
        }

        stack.Clear();
    }


    /// <summary>
    /// Randomly removes and adds entities to the tree to facilitate optimization.
    /// </summary>
    /// <param name="sweeps">The number of optimization iterations to perform. The default value is 100.</param>
    public void Optimize(int sweeps = 100)
    {
        Random random = new(0);
        Stack<T> temp = new();
        for (int e = 0; e < sweeps; e++)
        {
            for (int i = 0; i < activeList.Count; i++)
            {
                T proxy = activeList[i];

                if (random.NextDouble() > 0.05d) continue;

                temp.Push(proxy);
                InternalRemoveProxy(proxy);
            }

            while (temp.Count > 0)
            {
                T proxy = temp.Pop();
                InternalAddProxy(proxy);
            }
        }
    }

    private int AllocateNode()
    {
        if (freeNodes.Count > 0)
        {
            return freeNodes.Pop();
        }

        nodePointer += 1;
        if (nodePointer == Nodes.Length)
        {
            Array.Resize(ref Nodes, Nodes.Length * 2);
            Trace.WriteLine($"Resized array of AABBTree to {Nodes.Length} elements.");
        }

        return nodePointer;
    }

    private void FreeNode(int node)
    {
        freeNodes.Push(node);
    }

    private void CheckBagCount()
    {
        int numThreads = ThreadPool.Instance.ThreadCount;
        if (lists.Length != numThreads)
        {
            lists = new SlimBag<T>[numThreads];
            for (int i = 0; i < numThreads; i++)
            {
                lists[i] = new SlimBag<T>();
            }
        }
    }

    private double Cost(ref Node node)
    {
        if (node.IsLeaf)
        {
            Debug.Assert(node.ExpandedBox.Perimeter < 1e8);
            return node.ExpandedBox.Perimeter;
        }

        return node.ExpandedBox.Perimeter + Cost(ref Nodes[node.Left]) + Cost(ref Nodes[node.Right]);
    }

    private int Height(ref Node node)
    {
        if (node.IsLeaf) return 0;
        return 1 + Math.Max(Height(ref Nodes[node.Left]), Height(ref Nodes[node.Right]));
    }

    private void OverlapCheck(T shape, bool add)
    {
        OverlapCheck(root, shape.NodePtr, add);
    }

    private void OverlapCheck(int index, int node, bool add)
    {
        if (Nodes[index].IsLeaf)
        {
            if (node == index) return;

            if (!Filter(Nodes[node].Proxy, Nodes[index].Proxy)) return;

            lock (PotentialPairs)
            {
                if (add) PotentialPairs.Add(new PairHashSet.Pair(index, node));
                else PotentialPairs.Remove(new PairHashSet.Pair(index, node));
            }
        }
        else
        {
            int child1 = Nodes[index].Left;
            int child2 = Nodes[index].Right;

            if (Nodes[child1].ExpandedBox.NotDisjoint(Nodes[node].ExpandedBox))
                OverlapCheck(child1, node, add);

            if (Nodes[child2].ExpandedBox.NotDisjoint(Nodes[node].ExpandedBox))
                OverlapCheck(child2, node, add);
        }
    }

    private void EnumerateAll(ref Node node, Action<JBBox, int> action, int depth = 0)
    {
        action(node.ExpandedBox, depth);
        if (node.IsLeaf) return;

        EnumerateAll(ref Nodes[node.Left], action, depth + 1);
        EnumerateAll(ref Nodes[node.Right], action, depth + 1);
    }

    private void ScanForMovedProxies(Parallel.Batch batch)
    {
        var list = lists[batch.BatchIndex];
        list.Clear();

        for (int i = batch.Start; i < batch.End; i++)
        {
            var proxy = activeList[i];

            if (Nodes[proxy.NodePtr].ForceUpdate || Nodes[proxy.NodePtr].ExpandedBox.Contains(proxy.WorldBoundingBox) !=
                JBBox.ContainmentType.Contains)
            {
                Nodes[proxy.NodePtr].ForceUpdate = false;
                list.Add(proxy);
            }

            // else proxy is well contained within the nodes expanded Box:
        }
    }

    private void ScanForOverlaps(int fraction, bool add)
    {
        var sl = lists[fraction];
        for (int i = 0; i < sl.Count; i++)
        {
            OverlapCheck(root, sl[i].NodePtr, add);
        }
    }

    private static void ExpandBoundingBox(ref JBBox box, in JVector direction)
    {
        if (direction.X < 0.0f)
        {
            box.Min.X += direction.X;
        }
        else
        {
            box.Max.X += direction.X;
        }

        if (direction.Y < 0.0f)
        {
            box.Min.Y += direction.Y;
        }
        else
        {
            box.Max.Y += direction.Y;
        }

        if (direction.Z < 0.0f)
        {
            box.Min.Z += direction.Z;
        }
        else
        {
            box.Max.Z += direction.Z;
        }

        box.Min.X -= ExpandEps;
        box.Min.Y -= ExpandEps;
        box.Min.Z -= ExpandEps;

        box.Max.X += ExpandEps;
        box.Max.Y += ExpandEps;
        box.Max.Z += ExpandEps;
    }

    private static float GenerateRandom(ulong seed)
    {
        const uint A = 21_687_443;
        const uint B = 35_253_893;

        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;

        uint randomBits = (uint)seed * A + B;
        return MathF.Abs((float)randomBits / uint.MaxValue);
    }

    private void InternalAddProxy(T proxy)
    {
        JBBox b = proxy.WorldBoundingBox;

        int index = AllocateNode();
        float pseudoRandomExt = GenerateRandom((ulong)index);

        ExpandBoundingBox(ref b, proxy.Velocity * ExpandFactor * (1.0f + pseudoRandomExt));

        Nodes[index].Proxy = proxy;
        Nodes[index].IsLeaf = true;
        Nodes[index].Height = 0;
        proxy.NodePtr = index;

        Nodes[index].ExpandedBox = b;

        AddLeaf(index);
    }

    private void InternalRemoveProxy(T proxy)
    {
        Debug.Assert(Nodes[proxy.NodePtr].IsLeaf);

        RemoveLeaf(proxy.NodePtr);
        FreeNode(proxy.NodePtr);
    }

    private void RemoveLeaf(int node)
    {
        if (node == root)
        {
            root = NullNode;
            return;
        }

        int parent = Nodes[node].Parent;
        int grandParent = Nodes[parent].Parent;
        int sibling;

        if (Nodes[parent].Left == node) sibling = Nodes[parent].Right;
        else sibling = Nodes[parent].Left;

        if (grandParent != NullNode)
        {
            if (Nodes[grandParent].Left == parent) Nodes[grandParent].Left = sibling;
            else Nodes[grandParent].Right = sibling;

            Nodes[sibling].Parent = grandParent;
            FreeNode(parent);

            int index = grandParent;
            while (index != NullNode)
            {
                int left = Nodes[index].Left;
                int rght = Nodes[index].Right;

                JBBox.CreateMerged(Nodes[left].ExpandedBox, Nodes[rght].ExpandedBox, out Nodes[index].ExpandedBox);
                Nodes[index].Height = 1 + Math.Max(Nodes[left].Height, Nodes[rght].Height);
                index = Nodes[index].Parent;
            }
        }
        else
        {
            root = sibling;
            Nodes[sibling].Parent = NullNode;
            FreeNode(parent);
        }
    }

    private static double MergedPerimeter(in JBBox box1, in JBBox box2)
    {
        double a, b;
        double x, y, z;

        a = box1.Min.X < box2.Min.X ? box1.Min.X : box2.Min.X;
        b = box1.Max.X > box2.Max.X ? box1.Max.X : box2.Max.X;

        x = b - a;

        a = box1.Min.Y < box2.Min.Y ? box1.Min.Y : box2.Min.Y;
        b = box1.Max.Y > box2.Max.Y ? box1.Max.Y : box2.Max.Y;

        y = b - a;

        a = box1.Min.Z < box2.Min.Z ? box1.Min.Z : box2.Min.Z;
        b = box1.Max.Z > box2.Max.Z ? box1.Max.Z : box2.Max.Z;

        z = b - a;

        return 2.0d * (x * y + x * z + z * y);
    }

    private void AddLeaf(int node)
    {
        if (root == NullNode)
        {
            root = node;
            Nodes[root].Parent = NullNode;
            return;
        }

        // search for the best sibling
        // int sibling = root;
        JBBox nodeBox = Nodes[node].ExpandedBox;

        int sibling = root;

        while (!Nodes[sibling].IsLeaf)
        {
            int left = Nodes[sibling].Left;
            int rght = Nodes[sibling].Right;

            double area = Nodes[sibling].ExpandedBox.Perimeter;

            double combinedArea = MergedPerimeter(Nodes[sibling].ExpandedBox, nodeBox);

            double cost = 2.0d * combinedArea;
            double inhcost = 2.0d * (combinedArea - area);
            double costl, costr;

            if (Nodes[left].IsLeaf)
            {
                costl = inhcost + MergedPerimeter(Nodes[left].ExpandedBox, nodeBox);
            }
            else
            {
                double oldArea = Nodes[left].ExpandedBox.Perimeter;
                double newArea = MergedPerimeter(Nodes[left].ExpandedBox, nodeBox);
                costl = newArea - oldArea + inhcost;
            }

            if (Nodes[rght].IsLeaf)
            {
                costr = inhcost + MergedPerimeter(Nodes[rght].ExpandedBox, nodeBox);
            }
            else
            {
                double oldArea = Nodes[rght].ExpandedBox.Perimeter;
                double newArea = MergedPerimeter(Nodes[rght].ExpandedBox, nodeBox);
                costr = newArea - oldArea + inhcost;
            }

            // costl /= 2;
            // costr /= 2;

            // if this is true, the choice is actually the best for the current candidate
            if (cost < costl && cost < costr) break;

            sibling = costl < costr ? left : rght;
        }

        // create a new parent
        int oldParent = Nodes[sibling].Parent;
        int newParent = AllocateNode();

        Nodes[newParent].Parent = oldParent;
        Nodes[newParent].Height = Nodes[sibling].Height + 1;

        if (oldParent != NullNode)
        {
            if (Nodes[oldParent].Left == sibling) Nodes[oldParent].Left = newParent;
            else Nodes[oldParent].Right = newParent;

            Nodes[newParent].Left = sibling;
            Nodes[newParent].Right = node;
            Nodes[sibling].Parent = newParent;
            Nodes[node].Parent = newParent;
        }
        else
        {
            Nodes[newParent].Left = sibling;
            Nodes[newParent].Right = node;
            Nodes[sibling].Parent = newParent;
            Nodes[node].Parent = newParent;
            root = newParent;
        }

        int index = Nodes[node].Parent;
        while (index != NullNode)
        {
            int lft = Nodes[index].Left;
            int rgt = Nodes[index].Right;

            JBBox.CreateMerged(Nodes[lft].ExpandedBox, Nodes[rgt].ExpandedBox, out Nodes[index].ExpandedBox);
            Nodes[index].Height = 1 + Math.Max(Nodes[lft].Height, Nodes[rgt].Height);
            index = Nodes[index].Parent;
        }
    }
}