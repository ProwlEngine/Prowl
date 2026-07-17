// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime;

namespace Prowl.Editor.GUI;

/// <summary>Manages a lazy PreviewRenderer and invalidates it when the displayed subject changes.</summary>
public sealed class PreviewWidget
{
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
}
