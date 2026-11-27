// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>What to do with the canvas region a frame occupied before the next frame is drawn.</summary>
public enum FrameDisposal
{
    /// <summary>Leave the region as it is, so the next frame composites over it.</summary>
    None = 0,

    /// <summary>Clear the region to fully transparent black.</summary>
    RestoreBackground,

    /// <summary>Restore the region to how it looked before this frame was drawn.</summary>
    RestorePrevious,
}

/// <summary>How a frame's pixels combine with the canvas underneath.</summary>
public enum FrameBlend
{
    /// <summary>Overwrite the destination region, alpha included.</summary>
    Source = 0,

    /// <summary>Composite over the destination using the frame's alpha.</summary>
    Over,
}
