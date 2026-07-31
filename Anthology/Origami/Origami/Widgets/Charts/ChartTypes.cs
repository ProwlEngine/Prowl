// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.OrigamiUI;

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

/// <summary>Where a Step chart's vertical riser sits relative to a pair of points.</summary>
public enum StepAlign
{
    /// <summary>Rise before the point (step ends at the point's x).</summary>
    Before,

    /// <summary>Rise after the point (step begins at the point's x).</summary>
    After,

    /// <summary>Rise halfway between the two points.</summary>
    Middle,
}
