// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector.Geometry;

namespace Prowl.Vector.Spatial
{
    /// <summary>
    /// A dynamic bounding-volume hierarchy for sets that change every frame (moving physics bodies, etc.).
    /// Leaves are inserted, removed, and moved individually with incremental tree rotations instead of a
    /// full rebuild.
    ///
    /// Each leaf is stored with a fattened AABB (its tight bounds enlarged by <see cref="Margin"/>). While
    /// an object moves inside its fat box nothing in the tree changes, so small per-frame motion is nearly
    /// free; a re-insertion only happens once the object leaves its margin. Optionally the fat box can be
    /// extended along the object's motion to anticipate travel.
    ///
    /// Items are referenced by a stable integer proxy id returned from <see cref="Add"/>. Overlap and ray
    /// queries are conservative (they test the fat boxes), so callers should do an exact narrow-phase test
    /// on the returned items.
    /// </summary>
    /// <typeparam name="T">The item type stored at each leaf.</typeparam>
    public sealed class DynamicBvh<T>
    {
        private const int Null = -1;

        private sealed class Node
        {
            public AABB Box;        // fattened bounds
            public T Item = default!;
            public int Parent;      // doubles as the free-list link when this node is free
            public int Child1;
            public int Child2;
            public int Height;      // -1 = free, 0 = leaf
            public bool IsLeaf => Child1 == Null;
        }

        private Node[] _nodes;
        private int _root = Null;
        private int _freeList;
        private int _nodeCount;
        private int _leafCount;

        /// <summary>Amount each leaf's bounds are enlarged on every side. Movement within this margin is free.</summary>
        public float Margin { get; set; }

        /// <summary>When updating with a displacement, how far ahead along the motion the fat box is extended.</summary>
        public float PredictionMultiplier { get; set; } = 2.0f;

        /// <summary>Number of items (leaves) in the tree.</summary>
        public int Count => _leafCount;

        /// <summary>Bounds of the whole tree (empty if it has no items).</summary>
        public AABB Bounds => _root == Null ? new AABB() : _nodes[_root].Box;

        public DynamicBvh(float margin = 0.0f, int initialCapacity = 16)
        {
            Margin = margin;
            int cap = Math.Max(1, initialCapacity);
            _nodes = new Node[cap];
            for (int i = 0; i < cap; i++)
                _nodes[i] = new Node { Parent = i + 1, Height = -1 };
            _nodes[cap - 1].Parent = Null;
            _freeList = 0;
        }

        #region Public API

        /// <summary>Inserts an item with the given tight bounds and returns its proxy id.</summary>
        public int Add(T item, AABB bounds)
        {
            int id = AllocateNode();
            var node = _nodes[id];
            node.Box = Fatten(bounds);
            node.Item = item;
            node.Height = 0;
            node.Child1 = node.Child2 = Null;
            InsertLeaf(id);
            _leafCount++;
            return id;
        }

        /// <summary>Removes an item by proxy id.</summary>
        public void Remove(int proxyId)
        {
            RemoveLeaf(proxyId);
            FreeNode(proxyId);
            _leafCount--;
        }

        /// <summary>
        /// Updates an item's bounds. If the new tight bounds still fit inside the leaf's fat box, the tree
        /// is left untouched and this returns false. Otherwise the leaf is re-inserted and this returns true.
        /// </summary>
        public bool Update(int proxyId, AABB bounds)
        {
            if (_nodes[proxyId].Box.Contains(bounds))
                return false;

            RemoveLeaf(proxyId);
            _nodes[proxyId].Box = Fatten(bounds);
            InsertLeaf(proxyId);
            return true;
        }

        /// <summary>
        /// Updates an item's bounds, extending the fat box along <paramref name="displacement"/> to
        /// anticipate motion. Returns false (no work) while the object stays inside its current fat box.
        /// </summary>
        public bool Update(int proxyId, AABB bounds, Float3 displacement)
        {
            if (_nodes[proxyId].Box.Contains(bounds))
                return false;

            AABB fat = Fatten(bounds);
            Float3 d = displacement * PredictionMultiplier;
            Float3 min = fat.Min, max = fat.Max;
            if (d.X < 0) min.X += d.X; else max.X += d.X;
            if (d.Y < 0) min.Y += d.Y; else max.Y += d.Y;
            if (d.Z < 0) min.Z += d.Z; else max.Z += d.Z;

            RemoveLeaf(proxyId);
            _nodes[proxyId].Box = new AABB(min, max);
            InsertLeaf(proxyId);
            return true;
        }

        /// <summary>Removes everything.</summary>
        public void Clear()
        {
            int cap = _nodes.Length;
            for (int i = 0; i < cap; i++)
            {
                _nodes[i].Parent = i + 1;
                _nodes[i].Height = -1;
                _nodes[i].Item = default!;
            }
            _nodes[cap - 1].Parent = Null;
            _freeList = 0;
            _root = Null;
            _nodeCount = 0;
            _leafCount = 0;
        }

        /// <summary>The item stored at a proxy id.</summary>
        public T GetItem(int proxyId) => _nodes[proxyId].Item;

        /// <summary>The fattened bounds stored at a proxy id.</summary>
        public AABB GetFatBounds(int proxyId) => _nodes[proxyId].Box;

        #endregion

        #region Queries

        /// <summary>Appends every item whose fat bounds overlap <paramref name="box"/> to <paramref name="results"/>.</summary>
        public void QueryAABB(AABB box, List<T> results) => QueryAABB(box, results.Add);

        /// <summary>Invokes <paramref name="visit"/> for every item whose fat bounds overlap <paramref name="box"/>.</summary>
        public void QueryAABB(AABB box, Action<T> visit)
        {
            if (_root == Null) return;

            Span<int> stack = stackalloc int[64];
            int sp = 0;
            stack[sp++] = _root;
            while (sp > 0)
            {
                int index = stack[--sp];
                Node node = _nodes[index];
                if (!node.Box.Intersects(box)) continue;

                if (node.IsLeaf)
                {
                    visit(node.Item);
                }
                else if (sp + 2 <= stack.Length)
                {
                    stack[sp++] = node.Child1;
                    stack[sp++] = node.Child2;
                }
            }
        }

        /// <summary>Appends every item whose fat bounds contain <paramref name="point"/> to <paramref name="results"/>.</summary>
        public void QueryPoint(Float3 point, List<T> results) => QueryAABB(new AABB(point, point), results);

        /// <summary>
        /// Finds the closest item hit by a ray. The tree narrows the search; <paramref name="intersect"/>
        /// performs the exact item test and returns the hit distance (negative for a miss).
        /// </summary>
        public bool Raycast(Ray ray, Func<T, Ray, float> intersect, out T hit, out float distance, float maxDistance = float.PositiveInfinity)
        {
            hit = default!;
            distance = maxDistance;
            bool found = false;
            if (_root == Null) return false;

            Span<int> stack = stackalloc int[64];
            int sp = 0;
            stack[sp++] = _root;
            while (sp > 0)
            {
                int index = stack[--sp];
                Node node = _nodes[index];
                if (!Intersection.RayAABB(ray.Origin, ray.Direction, node.Box.Min, node.Box.Max, out float tMin, out float tMax))
                    continue;
                if (tMax < 0 || tMin > distance)
                    continue;

                if (node.IsLeaf)
                {
                    float d = intersect(node.Item, ray);
                    if (d >= 0 && d < distance) { distance = d; hit = node.Item; found = true; }
                }
                else if (sp + 2 <= stack.Length)
                {
                    stack[sp++] = node.Child1;
                    stack[sp++] = node.Child2;
                }
            }

            if (!found) distance = 0;
            return found;
        }

        #endregion

        #region Node pool

        private AABB Fatten(AABB bounds)
        {
            Float3 m = new Float3(Margin);
            return new AABB(bounds.Min - m, bounds.Max + m);
        }

        private int AllocateNode()
        {
            if (_freeList == Null)
            {
                int oldCap = _nodes.Length;
                int newCap = oldCap * 2;
                Array.Resize(ref _nodes, newCap);
                for (int i = oldCap; i < newCap; i++)
                    _nodes[i] = new Node { Parent = i + 1, Height = -1 };
                _nodes[newCap - 1].Parent = Null;
                _freeList = oldCap;
            }

            int id = _freeList;
            _freeList = _nodes[id].Parent;
            var node = _nodes[id];
            node.Parent = Null;
            node.Child1 = Null;
            node.Child2 = Null;
            node.Height = 0;
            node.Item = default!;
            _nodeCount++;
            return id;
        }

        private void FreeNode(int id)
        {
            _nodes[id].Parent = _freeList;
            _nodes[id].Height = -1;
            _nodes[id].Item = default!;
            _freeList = id;
            _nodeCount--;
        }

        #endregion

        #region Tree maintenance

        private void InsertLeaf(int leaf)
        {
            if (_root == Null)
            {
                _root = leaf;
                _nodes[leaf].Parent = Null;
                return;
            }

            AABB leafBox = _nodes[leaf].Box;

            // Descend to the best sibling using a surface-area cost.
            int index = _root;
            while (!_nodes[index].IsLeaf)
            {
                var n = _nodes[index];
                float area = n.Box.SurfaceArea;
                AABB combined = n.Box.Encapsulating(leafBox);
                float combinedArea = combined.SurfaceArea;

                float cost = 2.0f * combinedArea;
                float inheritanceCost = 2.0f * (combinedArea - area);

                float cost1 = DescendCost(n.Child1, leafBox, inheritanceCost);
                float cost2 = DescendCost(n.Child2, leafBox, inheritanceCost);

                if (cost < cost1 && cost < cost2)
                    break;

                index = cost1 < cost2 ? n.Child1 : n.Child2;
            }

            int sibling = index;
            int oldParent = _nodes[sibling].Parent;
            int newParent = AllocateNode();

            var np = _nodes[newParent];
            np.Parent = oldParent;
            np.Box = leafBox.Encapsulating(_nodes[sibling].Box);
            np.Height = _nodes[sibling].Height + 1;
            np.Child1 = sibling;
            np.Child2 = leaf;
            _nodes[sibling].Parent = newParent;
            _nodes[leaf].Parent = newParent;

            if (oldParent != Null)
            {
                if (_nodes[oldParent].Child1 == sibling) _nodes[oldParent].Child1 = newParent;
                else _nodes[oldParent].Child2 = newParent;
            }
            else
            {
                _root = newParent;
            }

            RefitAndBalance(_nodes[leaf].Parent);
        }

        private float DescendCost(int child, AABB leafBox, float inheritanceCost)
        {
            AABB combined = leafBox.Encapsulating(_nodes[child].Box);
            if (_nodes[child].IsLeaf)
                return combined.SurfaceArea + inheritanceCost;
            return (combined.SurfaceArea - _nodes[child].Box.SurfaceArea) + inheritanceCost;
        }

        private void RemoveLeaf(int leaf)
        {
            if (leaf == _root)
            {
                _root = Null;
                return;
            }

            int parent = _nodes[leaf].Parent;
            int grandParent = _nodes[parent].Parent;
            int sibling = _nodes[parent].Child1 == leaf ? _nodes[parent].Child2 : _nodes[parent].Child1;

            if (grandParent != Null)
            {
                if (_nodes[grandParent].Child1 == parent) _nodes[grandParent].Child1 = sibling;
                else _nodes[grandParent].Child2 = sibling;
                _nodes[sibling].Parent = grandParent;
                FreeNode(parent);
                RefitAndBalance(grandParent);
            }
            else
            {
                _root = sibling;
                _nodes[sibling].Parent = Null;
                FreeNode(parent);
            }
        }

        private void RefitAndBalance(int index)
        {
            while (index != Null)
            {
                index = Balance(index);

                var n = _nodes[index];
                var c1 = _nodes[n.Child1];
                var c2 = _nodes[n.Child2];
                n.Height = 1 + Math.Max(c1.Height, c2.Height);
                n.Box = c1.Box.Encapsulating(c2.Box);

                index = n.Parent;
            }
        }

        // AVL-style rotation that lifts the taller grandchild. Returns the new root of the subtree.
        private int Balance(int iA)
        {
            var A = _nodes[iA];
            if (A.IsLeaf || A.Height < 2)
                return iA;

            int iB = A.Child1, iC = A.Child2;
            var B = _nodes[iB];
            var C = _nodes[iC];
            int balance = C.Height - B.Height;

            if (balance > 1)
            {
                int iF = C.Child1, iG = C.Child2;
                var F = _nodes[iF];
                var G = _nodes[iG];

                C.Child1 = iA;
                C.Parent = A.Parent;
                A.Parent = iC;

                if (C.Parent != Null)
                {
                    if (_nodes[C.Parent].Child1 == iA) _nodes[C.Parent].Child1 = iC;
                    else _nodes[C.Parent].Child2 = iC;
                }
                else _root = iC;

                if (F.Height > G.Height)
                {
                    C.Child2 = iF;
                    A.Child2 = iG;
                    G.Parent = iA;
                    A.Box = B.Box.Encapsulating(G.Box);
                    C.Box = A.Box.Encapsulating(F.Box);
                    A.Height = 1 + Math.Max(B.Height, G.Height);
                    C.Height = 1 + Math.Max(A.Height, F.Height);
                }
                else
                {
                    C.Child2 = iG;
                    A.Child2 = iF;
                    F.Parent = iA;
                    A.Box = B.Box.Encapsulating(F.Box);
                    C.Box = A.Box.Encapsulating(G.Box);
                    A.Height = 1 + Math.Max(B.Height, F.Height);
                    C.Height = 1 + Math.Max(A.Height, G.Height);
                }
                return iC;
            }

            if (balance < -1)
            {
                int iD = B.Child1, iE = B.Child2;
                var D = _nodes[iD];
                var E = _nodes[iE];

                B.Child1 = iA;
                B.Parent = A.Parent;
                A.Parent = iB;

                if (B.Parent != Null)
                {
                    if (_nodes[B.Parent].Child1 == iA) _nodes[B.Parent].Child1 = iB;
                    else _nodes[B.Parent].Child2 = iB;
                }
                else _root = iB;

                if (D.Height > E.Height)
                {
                    B.Child2 = iD;
                    A.Child1 = iE;
                    E.Parent = iA;
                    A.Box = C.Box.Encapsulating(E.Box);
                    B.Box = A.Box.Encapsulating(D.Box);
                    A.Height = 1 + Math.Max(C.Height, E.Height);
                    B.Height = 1 + Math.Max(A.Height, D.Height);
                }
                else
                {
                    B.Child2 = iE;
                    A.Child1 = iD;
                    D.Parent = iA;
                    A.Box = C.Box.Encapsulating(D.Box);
                    B.Box = A.Box.Encapsulating(E.Box);
                    A.Height = 1 + Math.Max(C.Height, D.Height);
                    B.Height = 1 + Math.Max(A.Height, E.Height);
                }
                return iB;
            }

            return iA;
        }

        #endregion
    }
}
