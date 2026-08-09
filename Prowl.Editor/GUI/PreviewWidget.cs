// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime;

namespace Prowl.Editor.GUI;

/// <summary>Manages a lazy PreviewRenderer and invalidates it when the displayed subject changes.</summary>
public sealed class PreviewWidget : IDisposable
{
    // ============================================================
    // Per-asset lookup
    // ============================================================

    /// <summary>Renderers held at once. Each owns a render target, so this is a real budget rather
    /// than an arbitrary cap - the least recently asked for is disposed past it.</summary>
    private const int MaxLive = 8;

    private static readonly Dictionary<Guid, PreviewWidget> s_byAsset = new();
    private static readonly List<Guid> s_recent = new(); // least recent first

    /// <summary>
    /// The preview for one asset.
    /// </summary>
    /// <remarks>
    /// Editors are shared between inspector panels - <c>EditorRegistries</c> caches a single instance per
    /// asset type - so a widget held as an editor field is reconfigured by every panel every frame, and
    /// they all end up drawing whichever asset set it up last. Keying on the asset gives each its own.
    /// </remarks>
    public static PreviewWidget For(Guid asset, int width = 256, int height = 256, bool showGrid = false)
    {
        if (s_byAsset.TryGetValue(asset, out PreviewWidget? existing))
        {
            s_recent.Remove(asset);
            s_recent.Add(asset);
            return existing;
        }

        var created = new PreviewWidget(width, height, showGrid);
        s_byAsset[asset] = created;
        s_recent.Add(asset);

        while (s_recent.Count > MaxLive)
        {
            Guid oldest = s_recent[0];
            s_recent.RemoveAt(0);
            if (s_byAsset.Remove(oldest, out PreviewWidget? evicted))
                evicted.Dispose();
        }

        return created;
    }

    /// <summary>Drops the preview for an asset, e.g. once it no longer exists.</summary>
    public static void Discard(Guid asset)
    {
        s_recent.Remove(asset);
        if (s_byAsset.Remove(asset, out PreviewWidget? widget))
            widget.Dispose();
    }

    private PreviewRenderer? _renderer;
    private EngineObject? _last;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _showGrid;

    public PreviewWidget(int width = 256, int height = 256, bool showGrid = false)
    {
        _width = width;
        _height = height;
        _showGrid = showGrid;
    }

    public PreviewRenderer Get(EngineObject subject, Action<PreviewRenderer> setup)
    {
        if (_renderer == null)
        {
            _renderer = new PreviewRenderer(_width, _height);
            _renderer.ShowGrid = _showGrid;
        }
        if (_last != subject)
        {
            _last = subject;
            setup(_renderer);
        }
        return _renderer;
    }

    public void Invalidate() => _last = null;

    public void Dispose()
    {
        _renderer?.Dispose();
        _renderer = null;
        _last = null;
    }
}
