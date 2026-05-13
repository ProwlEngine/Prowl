// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Bounding-volume hierarchy of localized lights (point + spot) for forward rendering.
///
/// One instance is owned by each <see cref="Resources.Scene"/>: a "static" tree for lights flagged
/// non-moving and a "dynamic" tree for everything else. Per fragment the shader walks both trees
/// stacklessly via rope links to find lights whose tight AABB contains the fragment position,
/// then accumulates their contributions.
///
/// Light slots are stable identifiers in [0, capacity). They survive topology changes so the
/// per-light data texture rarely needs to be re-uploaded. Topology changes (add / remove /
/// "moved past loose buffer") rebuild the tree top-down with a median split. Lights whose new
/// tight AABB still fits inside the previously-stored loose AABB only refit a single leaf node
/// no parent updates needed because internal node bounds were derived from the loose bounds.
/// </summary>
public sealed class LightBVH
{
    /// <summary>How much each light's tight AABB is inflated to produce its loose AABB.
    /// 0.25 means the light can drift 25 % of its range past its tight bounds before reinsertion.</summary>
    public const float DefaultLooseFactor = 0.25f;

    /// <summary>One float4 of light data uploaded per slot. Must match <c>LightBVH.glsl</c>.</summary>
    public const int TexelsPerLight = 5;

    /// <summary>Two float4 per node (min/hit, max/miss). Must match <c>LightBVH.glsl</c>.</summary>
    public const int TexelsPerNode = 2;

    /// <summary>Per-slot light parameters. Updated by <see cref="Update"/> / <see cref="Add"/>.</summary>
    public struct SlotInfo
    {
        public bool Active;
        public LightType Type;
        public Float3 Position;
        public Float3 Direction;
        public Float3 Color;
        public float Intensity;
        public float Range;
        public float SpotAngle;
        public float InnerSpotAngle;
        public bool ShadowEnabled;
        public float ShadowBias;
        public float ShadowNormalBias;
        public float ShadowStrength;
        public float ShadowQuality;
        public int ShadowSlot; // -1 = no atlas slot this frame
        public AABB Tight;     // light center +/- range, axis aligned
        public AABB Loose;     // tight expanded by Range * LooseFactor in each direction
    }

    /// <summary>
    /// Stackless rope BVH node. Layout matches the GPU exactly (2 float4):
    ///   <c>(Min.xyz, Hit) / (Max.xyz, Miss)</c>.
    /// <para>
    /// Internal: <c>Hit</c> = first child index, <c>Miss</c> = escape index.
    /// Leaf:     <c>Hit</c> = encoded slot ( <c>-(slot+1)</c> ), <c>Miss</c> = escape index.
    /// Escape index of -1 = "traversal complete".
    /// </para>
    /// </summary>
    public struct Node
    {
        public Float3 Min;
        public int Hit;
        public Float3 Max;
        public int Miss;

        public bool IsLeaf => Hit < 0;
        public int LeafSlot => -Hit - 1;
    }

    /// <summary>Encode a leaf hit pointer for slot index <paramref name="slot"/>.</summary>
    public static int EncodeLeafHit(int slot) => -(slot + 1);

    private readonly Dictionary<IRenderableLight, int> _lightToSlot = new();
    private readonly Stack<int> _freeSlots = new();

    private SlotInfo[] _slots;
    private int _slotHighWater;       // highest slot index ever allocated (texture must cover this)
    private int _activeLightCount;    // current registered lights

    private Node[] _nodes = Array.Empty<Node>();
    private int _nodeCount;

    // slot -> leaf node index, so a refit walk can start at the leaf and bubble up.
    // We don't need this for full rebuilds because we recompute everything; it's purely the
    // optimisation for the "leaf moved within its loose AABB" case.
    private int[] _slotToLeafNode;

    // Dirty tracking (linear texel ranges into the upload textures).
    private bool _topologyDirty;
    private int _slotDataMinDirty = int.MaxValue;
    private int _slotDataMaxDirty = -1;
    private int _nodeMinDirty = int.MaxValue;
    private int _nodeMaxDirty = -1;

    private readonly float _looseFactor;

    public int ActiveLightCount => _activeLightCount;
    public int NodeCount => _nodeCount;
    public int SlotHighWater => _slotHighWater;
    public int SlotCapacity => _slots.Length;

    /// <summary>True when the per-light data texture has rows that the GPU hasn't seen yet.</summary>
    public bool HasSlotDirty => _slotDataMinDirty <= _slotDataMaxDirty;

    /// <summary>True when the BVH node texture has rows that the GPU hasn't seen yet.</summary>
    public bool HasNodeDirty => _nodeMinDirty <= _nodeMaxDirty;

    /// <summary>Read-only view of all slots (active or freed). Index by slot id.</summary>
    public ReadOnlySpan<SlotInfo> Slots => _slots.AsSpan(0, _slotHighWater);

    /// <summary>Read-only view of the live BVH nodes.</summary>
    public ReadOnlySpan<Node> Nodes => _nodes.AsSpan(0, _nodeCount);

    public LightBVH(int initialCapacity = 32, float looseFactor = DefaultLooseFactor)
    {
        if (initialCapacity < 1) initialCapacity = 1;
        if (looseFactor < 0f) looseFactor = 0f;
        _looseFactor = looseFactor;
        _slots = new SlotInfo[initialCapacity];
        _slotToLeafNode = new int[initialCapacity];
        Array.Fill(_slotToLeafNode, -1);
    }

    /// <summary>Returns the slot index for <paramref name="light"/>, or -1 if not registered.</summary>
    public int GetSlot(IRenderableLight light)
        => _lightToSlot.TryGetValue(light, out int slot) ? slot : -1;

    /// <summary>
    /// Add a light to the tree. Slot is stable until <see cref="Remove"/> is called for the same
    /// light. Topology is marked dirty so the next <see cref="Sync"/> will rebuild.
    /// </summary>
    public int Add(IRenderableLight light, in ForwardLightData data)
    {
        if (light == null) throw new ArgumentNullException(nameof(light));
        if (_lightToSlot.TryGetValue(light, out int existing))
        {
            // Idempotent re-add behaves as an update: keep the slot, refresh data.
            UpdateInternal(existing, in data);
            return existing;
        }

        int slot = AllocateSlot();
        _lightToSlot[light] = slot;
        _activeLightCount++;
        WriteSlot(slot, in data);
        _topologyDirty = true;
        return slot;
    }

    /// <summary>
    /// Remove a registered light. Frees its slot for reuse. Topology marked dirty.
    /// Returns true if the light was registered.
    /// </summary>
    public bool Remove(IRenderableLight light)
    {
        if (light == null) return false;
        if (!_lightToSlot.Remove(light, out int slot)) return false;

        _slots[slot].Active = false;
        _slotToLeafNode[slot] = -1;
        _freeSlots.Push(slot);
        _activeLightCount--;

        // Don't touch the data texture row; it's orphaned and never referenced because the BVH
        // won't emit a leaf for an inactive slot. Saves an upload.
        _topologyDirty = true;
        return true;
    }

    /// <summary>
    /// Update a registered light's parameters. Cheap when the new tight AABB still sits inside
    /// the previously-stored loose AABB (just refits one leaf node). Otherwise marks topology
    /// dirty so the tree will be rebuilt with a fresh loose envelope.
    /// </summary>
    public void Update(IRenderableLight light, in ForwardLightData data)
    {
        if (light == null) throw new ArgumentNullException(nameof(light));
        if (!_lightToSlot.TryGetValue(light, out int slot))
        {
            Add(light, in data);
            return;
        }
        UpdateInternal(slot, in data);
    }

    private void UpdateInternal(int slot, in ForwardLightData data)
    {
        ref SlotInfo s = ref _slots[slot];
        AABB oldLoose = s.Loose;
        bool wasActive = s.Active;

        // Skip the entire path when nothing visible to either the GPU or the BVH topology
        // changed. The reconcile pass calls Update for every dynamic light every frame; without
        // this fast path we'd re-pack and re-upload identical bytes constantly.
        if (wasActive && SlotMatches(in s, in data))
            return;

        WriteSlot(slot, in data);

        // If we don't yet have a tree (or this is a brand-new leaf since the last sync), the
        // upcoming rebuild will pick up the new loose AABB; nothing else to do.
        if (!wasActive || _topologyDirty || _slotToLeafNode[slot] < 0)
        {
            _topologyDirty = true;
            return;
        }

        // Range / type changes regenerate the tight + loose AABBs. If the new tight escapes the
        // old loose envelope, the parent nodes that were sized around the old loose are no
        // longer correct: rebuild. Otherwise we can refit just this leaf.
        if (!oldLoose.Contains(s.Tight))
        {
            _topologyDirty = true;
            return;
        }

        // Keep the existing loose so the invariant ("leaf tight subset of leaf loose,
        // internal bounds = union of children's loose") holds without touching parents.
        s.Loose = oldLoose;

        // Update just this leaf node's stored tight bounds in the GPU node table.
        int leafNode = _slotToLeafNode[slot];
        _nodes[leafNode].Min = s.Tight.Min;
        _nodes[leafNode].Max = s.Tight.Max;
        MarkNodeDirty(leafNode);
    }

    /// <summary>
    /// Bring the tree and dirty ranges up to date. Call once per frame before uploading textures.
    /// </summary>
    public void Sync()
    {
        if (_topologyDirty)
        {
            Rebuild();
            _topologyDirty = false;
        }
    }

    /// <summary>Mark every dirty range as uploaded. Call after the texture upload has consumed it.</summary>
    public void ClearDirtyRanges()
    {
        _slotDataMinDirty = int.MaxValue;
        _slotDataMaxDirty = -1;
        _nodeMinDirty = int.MaxValue;
        _nodeMaxDirty = -1;
    }

    public bool TryGetSlotDirtyRange(out int minSlot, out int maxSlot)
    {
        if (_slotDataMinDirty > _slotDataMaxDirty) { minSlot = -1; maxSlot = -1; return false; }
        minSlot = _slotDataMinDirty;
        maxSlot = _slotDataMaxDirty;
        return true;
    }

    public bool TryGetNodeDirtyRange(out int minNode, out int maxNode)
    {
        if (_nodeMinDirty > _nodeMaxDirty) { minNode = -1; maxNode = -1; return false; }
        minNode = _nodeMinDirty;
        maxNode = _nodeMaxDirty;
        return true;
    }

    /// <summary>
    /// Mark the entire current state as dirty. Used after the textures resize and the GPU view
    /// of the data is invalidated.
    /// </summary>
    public void MarkAllDirty()
    {
        if (_slotHighWater > 0)
        {
            _slotDataMinDirty = 0;
            _slotDataMaxDirty = _slotHighWater - 1;
        }
        if (_nodeCount > 0)
        {
            _nodeMinDirty = 0;
            _nodeMaxDirty = _nodeCount - 1;
        }
    }

    // ─────────────────────── internals ───────────────────────

    private int AllocateSlot()
    {
        if (_freeSlots.Count > 0)
        {
            int slot = _freeSlots.Pop();
            return slot;
        }

        int newSlot = _slotHighWater++;
        if (newSlot >= _slots.Length)
        {
            int newCap = _slots.Length * 2;
            Array.Resize(ref _slots, newCap);
            int oldLen = _slotToLeafNode.Length;
            Array.Resize(ref _slotToLeafNode, newCap);
            for (int i = oldLen; i < newCap; i++) _slotToLeafNode[i] = -1;
        }
        return newSlot;
    }

    private void WriteSlot(int slot, in ForwardLightData data)
    {
        ref SlotInfo s = ref _slots[slot];
        // First-time activation defaults shadow slot to "no atlas slot". Subsequent updates
        // preserve whatever the host system last wrote via SetShadowSlot.
        if (!s.Active) s.ShadowSlot = -1;
        s.Active = true;
        s.Type = data.Type;
        s.Position = data.Position;
        s.Direction = data.Direction;
        s.Color = data.Color;
        s.Intensity = data.Intensity;
        s.Range = data.Range;
        s.SpotAngle = data.SpotAngle;
        s.InnerSpotAngle = data.InnerSpotAngle;
        s.ShadowEnabled = data.ShadowEnabled;
        s.ShadowBias = data.ShadowBias;
        s.ShadowNormalBias = data.ShadowNormalBias;
        s.ShadowStrength = data.ShadowStrength;
        s.ShadowQuality = data.ShadowQuality;
        // Shadow slot is assigned by the scene-level system after the closest-N selection.
        // Preserve any prior assignment until that pass runs again.

        s.Tight = ComputeTightBounds(in s);
        s.Loose = ExpandByLoose(s.Tight, s.Range);

        MarkSlotDirty(slot);
    }

    /// <summary>Update only the shadow-slot field on a registered light. Cheap, marks one row dirty.</summary>
    public void SetShadowSlot(IRenderableLight light, int shadowSlot)
    {
        if (!_lightToSlot.TryGetValue(light, out int slot)) return;
        if (_slots[slot].ShadowSlot == shadowSlot) return;
        _slots[slot].ShadowSlot = shadowSlot;
        MarkSlotDirty(slot);
    }

    private static bool SlotMatches(in SlotInfo s, in ForwardLightData data)
    {
        return s.Type == data.Type
            && s.Position == data.Position
            && s.Direction == data.Direction
            && s.Color == data.Color
            && s.Intensity == data.Intensity
            && s.Range == data.Range
            && s.SpotAngle == data.SpotAngle
            && s.InnerSpotAngle == data.InnerSpotAngle
            && s.ShadowEnabled == data.ShadowEnabled
            && s.ShadowBias == data.ShadowBias
            && s.ShadowNormalBias == data.ShadowNormalBias
            && s.ShadowStrength == data.ShadowStrength
            && s.ShadowQuality == data.ShadowQuality;
    }

    private static AABB ComputeTightBounds(in SlotInfo s)
    {
        // For point: sphere of radius Range. For spot: same conservative sphere AABB
        // tighter spot AABBs aren't worth the complexity at this scale, the BVH absorbs it.
        float r = MathF.Max(0.0001f, s.Range);
        Float3 ext = new(r, r, r);
        return new AABB(s.Position - ext, s.Position + ext);
    }

    private AABB ExpandByLoose(AABB tight, float range)
    {
        float pad = MathF.Max(0f, range) * _looseFactor;
        if (pad <= 0f) return tight;
        Float3 e = new(pad, pad, pad);
        return new AABB(tight.Min - e, tight.Max + e);
    }

    private void MarkSlotDirty(int slot)
    {
        if (slot < _slotDataMinDirty) _slotDataMinDirty = slot;
        if (slot > _slotDataMaxDirty) _slotDataMaxDirty = slot;
    }

    private void MarkNodeDirty(int node)
    {
        if (node < _nodeMinDirty) _nodeMinDirty = node;
        if (node > _nodeMaxDirty) _nodeMaxDirty = node;
    }

    // ─────────────────────── full rebuild ───────────────────────

    /// <summary>Top-down median split build. O(N log N). Tree is laid out in DFS order so the
    /// hit-link of an internal node is just (currentIndex + 1).</summary>
    private void Rebuild()
    {
        // Collect active slots into a working array.
        int active = _activeLightCount;
        if (active == 0)
        {
            _nodeCount = 0;
            return;
        }

        var work = new int[active];
        int idx = 0;
        for (int s = 0; s < _slotHighWater; s++)
            if (_slots[s].Active) work[idx++] = s;

        // Worst-case node count: 2N - 1 (binary tree with N leaves). Reuse buffer when possible.
        int maxNodes = 2 * active - 1;
        if (_nodes.Length < maxNodes)
            _nodes = new Node[Math.Max(maxNodes, 4)];

        Array.Fill(_slotToLeafNode, -1);

        _nodeCount = 0;
        Build(work, 0, active);
        WireRopes(0, -1);

        // Whole tree changed: every node row must be re-uploaded.
        _nodeMinDirty = 0;
        _nodeMaxDirty = _nodeCount - 1;
    }

    /// <summary>Build a subtree over <c>work[start..start+count]</c>. Returns the root index.</summary>
    private int Build(int[] work, int start, int count)
    {
        int nodeIdx = _nodeCount++;

        if (count == 1)
        {
            int slot = work[start];
            ref SlotInfo s = ref _slots[slot];
            ref Node n = ref _nodes[nodeIdx];
            n.Min = s.Tight.Min;
            n.Max = s.Tight.Max;
            n.Hit = EncodeLeafHit(slot);
            n.Miss = -1; // patched by WireRopes
            _slotToLeafNode[slot] = nodeIdx;
            return nodeIdx;
        }

        // Compute combined LOOSE bounds across the partition this is what internal nodes carry,
        // so leaves can shift inside their own loose envelope without dirtying parents.
        Float3 mn = _slots[work[start]].Loose.Min;
        Float3 mx = _slots[work[start]].Loose.Max;
        Float3 cMn = _slots[work[start]].Tight.Center;
        Float3 cMx = cMn;

        for (int i = start + 1; i < start + count; i++)
        {
            ref SlotInfo s = ref _slots[work[i]];
            mn = Maths.Min(mn, s.Loose.Min);
            mx = Maths.Max(mx, s.Loose.Max);
            Float3 c = s.Tight.Center;
            cMn = Maths.Min(cMn, c);
            cMx = Maths.Max(cMx, c);
        }

        ref Node node = ref _nodes[nodeIdx];
        node.Min = mn;
        node.Max = mx;

        // Choose split axis = longest centroid extent.
        Float3 cExt = cMx - cMn;
        int axis = cExt.X > cExt.Y ? (cExt.X > cExt.Z ? 0 : 2) : (cExt.Y > cExt.Z ? 1 : 2);

        // Median split (nth-element style). If all centroids coincide on the chosen axis just
        // bisect the input; the bounds are still correct and the tree stays balanced.
        int mid = start + count / 2;
        if (cExt[axis] <= float.Epsilon)
        {
            // degenerate: just split in half by current order
        }
        else
        {
            QuickSelect(work, start, start + count - 1, mid, axis);
        }

        int leftCount = mid - start;
        int rightCount = count - leftCount;

        // Build left first so it occupies the slot directly after this internal node, then right.
        int leftIdx = Build(work, start, leftCount);
        int rightIdx = Build(work, mid, rightCount);

        // Hit = left child. Stash the right child index in Miss temporarily so WireRopes can
        // walk the tree in O(N) without a separate "subtree end" traversal. WireRopes overwrites
        // Miss with the real escape link.
        node.Hit = leftIdx;
        node.Miss = rightIdx;
        return nodeIdx;
    }

    /// <summary>Walks the DFS-ordered tree once and patches every node's <c>Miss</c> link from the
    /// rightIdx hint stashed by <see cref="Build"/>.</summary>
    private void WireRopes(int nodeIdx, int escape)
    {
        ref Node node = ref _nodes[nodeIdx];
        if (node.IsLeaf)
        {
            node.Miss = escape;
            return;
        }

        int leftIdx = node.Hit;
        int rightIdx = node.Miss; // stashed by Build, overwritten below
        node.Miss = escape;

        // From the left subtree, "next" is the right subtree. From the right subtree, "next" is
        // whatever was the parent's escape.
        WireRopes(leftIdx, rightIdx);
        WireRopes(rightIdx, escape);
    }

    /// <summary>
    /// Lomuto-partition quickselect on slot indices ordered by tight-AABB centroid along
    /// <paramref name="axis"/>. Partial sort, O(N) average. Picks the middle element as pivot;
    /// it's good enough for the centroid distributions we see and avoids the boundary-condition
    /// hazards of the Hoare scheme.
    /// </summary>
    private void QuickSelect(int[] work, int lo, int hi, int k, int axis)
    {
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int pivot = Partition(work, lo, hi, mid, axis);
            if (pivot == k) return;
            if (pivot < k) lo = pivot + 1;
            else hi = pivot - 1;
        }
    }

    private int Partition(int[] work, int lo, int hi, int pivotIdx, int axis)
    {
        float pivotVal = Centroid(work[pivotIdx], axis);
        // Move pivot to the end.
        (work[pivotIdx], work[hi]) = (work[hi], work[pivotIdx]);
        int store = lo;
        for (int i = lo; i < hi; i++)
        {
            if (Centroid(work[i], axis) < pivotVal)
            {
                (work[store], work[i]) = (work[i], work[store]);
                store++;
            }
        }
        // Restore pivot to its final spot.
        (work[store], work[hi]) = (work[hi], work[store]);
        return store;
    }

    private float Centroid(int slot, int axis)
    {
        Float3 c = _slots[slot].Tight.Center;
        return axis switch { 0 => c.X, 1 => c.Y, _ => c.Z };
    }
}
