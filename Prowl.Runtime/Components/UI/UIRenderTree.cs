// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// Per-<see cref="GameCanvas"/> store of <see cref="UIRenderItem"/>s. The canvas writes
/// into the tree from <c>RebuildIfDirty</c>; the pipeline reads from it via
/// <see cref="CollectFor"/>. Items are pooled inside the tree so the steady-state
/// allocation rate is zero.
/// </summary>
internal sealed class UIRenderTree
{
    // -------- Active items, in DFS order --------
    private readonly List<UIRenderItem> _items = new(32);

    // -------- Free list: reusable UIRenderItem instances ----
    private readonly Stack<UIRenderItem> _freeItems = new(32);

    /// <summary>Read-only access for the pipeline / canvas. Safe to enumerate.</summary>
    public IReadOnlyList<UIRenderItem> Items => _items;
    public int Count => _items.Count;

    // -------- Topology mutation --------

    /// <summary>
    /// Returns a fresh-or-recycled <see cref="UIRenderItem"/> *not yet* in the items list.
    /// The caller must invoke <see cref="UIRenderItem.Initialize"/> and then <see cref="Add"/>.
    /// </summary>
    public UIRenderItem RentItem() =>
        _freeItems.Count > 0 ? _freeItems.Pop() : new UIRenderItem();

    public void Add(UIRenderItem item) => _items.Add(item);

    /// <summary>
    /// Releases all current items back to the free list. Called at the start of every
    /// <c>RebuildIfDirty</c>.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            UIRenderItem it = _items[i];
            // Drop external refs so disabled items don't keep meshes/materials alive.
            it.Owner = null!; it.Canvas = null!;
            it.Mesh = null!;  it.Material = null!;
            it.Props.Clear();
            it.HasClip = false; it.ClipSource = null;
            _freeItems.Push(it);
        }
        _items.Clear();
    }

    /// <summary>
    /// Sorts items by <see cref="UIRenderItem.SortKey"/>. Stable across rebuilds because
    /// keys encode <c>SortOrder</c> + DFS index.
    /// </summary>
    public void SortHierarchical() => _items.Sort(static (a, b) => a.SortKey.CompareTo(b.SortKey));

    /// <summary>
    /// Patches model matrices for items whose owning Transform.Version changed since last
    /// rebuild. Avoids a full rebuild when only rotation/translation changed.
    /// </summary>
    public void RefreshTransforms()
    {
        for (int i = 0; i < _items.Count; i++)
            _items[i].RefreshModelIfDirty();
    }

    // ============================================================
    // Static scene query - the pipeline's entry point
    // ============================================================

    /// <summary>
    /// Walks every active <see cref="GameCanvas"/> in <paramref name="scene"/>, asks each
    /// to <c>RebuildIfDirty</c>, and appends every item whose surface matches
    /// <paramref name="surface"/> to <paramref name="dst"/>.
    /// </summary>
    /// <remarks>
    /// We can't cache the canvas list across frames: scenes can hot-add/remove canvases
    /// at runtime. The walk is O(active GameObjects), bounded by the editor's tree size,
    /// and only allocates if a canvas needs a rebuild.
    /// </remarks>
    public static void CollectFor(Scene scene, UISurface surface, List<IRenderable> dst)
    {
        if (scene is null) return;

        foreach (GameObject go in scene.ActiveObjects)
        {
            // GetComponent is O(components-on-go). Cheap.
            GameCanvas? gc = go.GetComponent<GameCanvas>();
            if (gc is null || !gc.EnabledInHierarchy) continue;
            if (ToSurface(gc.RenderMode) != surface) continue;

            gc.RebuildIfDirty();         // no-op if clean
            gc.Tree.RefreshTransforms(); // matrix fast-path

            var items = gc.Tree._items;
            for (int i = 0; i < items.Count; i++)
                dst.Add(items[i]);
        }
    }

    /// <summary>
    /// Maps the user-facing <see cref="RenderMode"/> to the pipeline-side <see cref="UISurface"/>.
    /// Single source of truth - every other site that needs the conversion calls this.
    /// </summary>
    internal static UISurface ToSurface(RenderMode m) => m switch
    {
        RenderMode.WorldSpace => UISurface.World,
        _                     => UISurface.Overlay,
    };
}
