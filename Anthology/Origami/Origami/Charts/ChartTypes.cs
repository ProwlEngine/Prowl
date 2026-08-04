// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.OrigamiUI;

namespace Prowl.OrigamiUI.Charts;

/// <summary>How a chart's value axis maps data values to pixel positions.</summary>
public enum AxisScale
{
    /// <summary>Even spacing per unit value.</summary>
    Linear,

    /// <summary>Even spacing per order of magnitude.</summary>
    Log,
}

/// <summary>Point marker glyph used by Scatter and Bubble charts.</summary>
public enum MarkerShape
{
    Circle,
    Square,
    Triangle,
    Diamond,
    Cross,
}

/// <summary>How a Line/Area chart interpolates between points.</summary>
public enum CartesianInterpolation
{
    /// <summary>Straight segments between consecutive points.</summary>
    Linear,

    /// <summary>Curved segments through consecutive points.</summary>
    Smooth,
}
