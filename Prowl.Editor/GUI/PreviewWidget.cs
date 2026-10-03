// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime;

namespace Prowl.Editor.GUI;

/// <summary>Manages a lazy PreviewRenderer and invalidates it when the displayed subject changes.</summary>
public sealed class PreviewWidget : IDisposable
{
    // A preview keeps its subject in a live scene, which keeps everything it shows loaded, so each lives only while drawn.
    private static readonly Dictionary<Guid, PreviewWidget> s_byAsset = new();

    /// <summary> Gets or creates a PreviewWidget for the specified asset, reusing an existing one if its dimensions and grid setting match. Editors are shared between inspector panels - EditorRegistries caches a single instance per asset type - so a widget held as an editor field is reconfigured by every panel every frame, and they all end up drawing whichever asset set it up last. Keying on the asset gives each its own. </summary>
    public static PreviewWidget For(Guid asset, int width = 256, int height = 256, bool showGrid = false)
    {
        if (s_byAsset.TryGetValue(asset, out PreviewWidget? existing))
        {
            if (existing._width == width && existing._height == height && existing._showGrid == showGrid)
                return existing;
            existing.Dispose();
        }

        var created = new PreviewWidget(width, height, showGrid);
        s_byAsset[asset] = created;
        return created;
    }

    /// <summary>Makes an asset's preview set up again on its next draw, if it has one.</summary>
    public static void Invalidate(Guid asset)
    {
        if (s_byAsset.TryGetValue(asset, out PreviewWidget? widget)) widget.Invalidate();
    }

    /// <summary>Disposes every preview nothing drew since the last call. Called once a frame, before drawing.</summary>
    public static void ReleaseUndrawn()
    {
        List<Guid>? undrawn = null;
        foreach (var (asset, widget) in s_byAsset)
        {
            if (!widget._drawn) (undrawn ??= []).Add(asset);
            widget._drawn = false;
        }
        if (undrawn == null) return;
        foreach (Guid asset in undrawn)
            Discard(asset);
    }

    /// <summary>Drops the preview for an asset, e.g. once it no longer exists.</summary>
    public static void Discard(Guid asset)
    {
        if (s_byAsset.Remove(asset, out PreviewWidget? widget))
            widget.Dispose();
    }

    private PreviewRenderer? _renderer;
    private EngineObject? _last;
    private bool _drawn;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _showGrid;

    /// <summary> Creates a new PreviewWidget with the specified dimensions and grid visibility. </summary>
    public PreviewWidget(int width = 256, int height = 256, bool showGrid = false)
    {
        _width = width;
        _height = height;
        _showGrid = showGrid;
    }

    /// <summary> Returns the cached PreviewRenderer for the given subject, calling the setup action if the subject has changed or the renderer was just created. </summary>
    public PreviewRenderer Get(EngineObject subject, Action<PreviewRenderer> setup)
    {
        _drawn = true;
        if (_renderer == null)
        {
            _renderer = new PreviewRenderer(_width, _height);
            _renderer.ShowGrid = _showGrid;
        }
        if (_last != subject)
        {
            setup(_renderer);
            _last = subject;
        }
        return _renderer;
    }

    /// <summary> Forces the next call to Get to re-run the setup action for the current subject. </summary>
    public void Invalidate() => _last = null;

    /// <summary> Disposes the underlying PreviewRenderer and resets the widget state. </summary>
    public void Dispose()
    {
        _renderer?.Dispose();
        _renderer = null;
        _last = null;
    }
}
