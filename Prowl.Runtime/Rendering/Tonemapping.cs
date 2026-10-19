// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering;

/// <summary>How an HDR camera maps its colour to the display.</summary>
public enum TonemappingMode
{
    ACES,

    /// <summary>AgX with no look, flat and desaturated.</summary>
    AgXNeutral,

    /// <summary>AgX with the reference Punchy look: more contrast and saturation.</summary>
    AgXPunchy,

    /// <summary>Extended Reinhard on luminance, white at 2.</summary>
    Reinhard,
    RomBinDaHouse,
    Uncharted2,
}

/// <summary>A camera's tonemapper, made when first used.</summary>
internal sealed class TonemappingFeature
{
    private Renderer? _renderer;
    private Tonemapper? _tonemapper;

    public RenderFeature Get(Renderer renderer, TonemappingMode mode)
    {
        if (_renderer != renderer)
        {
            _renderer = renderer;
            _tonemapper = new Tonemapper(renderer);
        }

        _tonemapper!.Operator = (TonemapOperator)mode;
        return _tonemapper;
    }
}
