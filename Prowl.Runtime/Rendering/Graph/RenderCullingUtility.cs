// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Reusable frustum/layer culling and distance sorting for a set of <see cref="IRenderable"/>s.
/// An <see cref="IRenderCuller{TDrawCommand}"/> holds one of these and feeds its index outputs into
/// whatever draw commands the pipeline emits. Buffers are reused across frames; a single instance is
/// not re-entrant (encode is sequential).
/// </summary>
public sealed class RenderCullingUtility
{
    private AABB[] _worldBounds = Array.Empty<AABB>();
    private bool[] _boundsRenderable = Array.Empty<bool>();
    private IReadOnlyList<IRenderable>? _boundsFrameList;
    private int _boundsCount;

    private static readonly Comparison<(int index, float distSq)> s_frontToBack = (a, b) => a.distSq.CompareTo(b.distSq);
    private static readonly Comparison<(int index, float distSq)> s_backToFront = (a, b) => b.distSq.CompareTo(a.distSq);

    private readonly List<(int index, float distSq)> _sortPairs = new();
    private readonly List<int> _sortResult = new();

    /// <summary>
    /// Computes (or returns the cached) per-renderable world-space bounds for this frame's list. Keyed
    /// on list identity + count so the main cull and every shadow cull transform each bound once.
    /// </summary>
    public void EnsureWorldBounds(IReadOnlyList<IRenderable> renderables)
    {
        int count = renderables.Count;
        if (ReferenceEquals(_boundsFrameList, renderables) && _boundsCount == count)
            return;

        if (_worldBounds.Length < count)
        {
            _worldBounds = new AABB[count];
            _boundsRenderable = new bool[count];
        }

        for (int i = 0; i < count; i++)
        {
            renderables[i].GetCullingData(out bool isRenderable, out AABB bounds);
            _boundsRenderable[i] = isRenderable;
            _worldBounds[i] = bounds;
        }

        _boundsFrameList = renderables;
        _boundsCount = count;
    }

    /// <summary>
    /// Returns a per-index "culled" mask (true == skip) aligned to <paramref name="renderables"/>.
    /// A renderable is culled when it's outside <paramref name="worldFrustum"/> (if provided) or not
    /// on <paramref name="cullingMask"/>.
    /// </summary>
    public bool[] ComputeCullMask(IReadOnlyList<IRenderable> renderables, Frustum? worldFrustum, LayerMask cullingMask)
    {
        EnsureWorldBounds(renderables);

        var culled = new bool[renderables.Count];
        for (int i = 0; i < renderables.Count; i++)
        {
            bool frustumCull = worldFrustum != null
                && (!_boundsRenderable[i] || !worldFrustum.Value.Intersects(_worldBounds[i]));

            if (frustumCull || !cullingMask.HasLayer(renderables[i].GetLayer()))
                culled[i] = true;
        }

        return culled;
    }

    /// <summary>
    /// Returns the indices of the non-culled renderables ordered by distance from
    /// <paramref name="cameraPosition"/>. The returned list is reused across calls.
    /// </summary>
    public List<int> SortIndices(IReadOnlyList<IRenderable> renderables, bool[]? culledMask, Float3 cameraPosition, SortMode mode)
    {
        _sortResult.Clear();
        int count = renderables?.Count ?? 0;
        if (count == 0)
            return _sortResult;

        _sortPairs.Clear();
        for (int i = 0; i < count; i++)
        {
            if (culledMask != null && culledMask[i])
                continue;

            float distSq = Float3.DistanceSquared(renderables[i].GetPosition(), cameraPosition);
            _sortPairs.Add((i, distSq));
        }

        if (mode != SortMode.None)
            _sortPairs.Sort(mode == SortMode.BackToFront ? s_backToFront : s_frontToBack);

        for (int i = 0; i < _sortPairs.Count; i++)
            _sortResult.Add(_sortPairs[i].index);

        return _sortResult;
    }
}
