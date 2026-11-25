// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

/// <summary>
/// How a frame waits for the display before it is shown. Where the display lacks a mode, Mailbox and Off stand in for each other and Adaptive for On, which every display offers.
/// </summary>
public enum VSyncMode
{
    // The same members with the same values as Prowl.Graphite.VSyncMode, so Graphics casts between the two
    /// <summary>
    /// Renders uncapped and shows the newest finished frame at each refresh, without tearing. It can be held to the refresh once a window covers the whole screen (seen on a hybrid-GPU laptop), where <see cref="Off"/> is not.
    /// </summary>
    Mailbox,
    /// <summary>
    /// Waits for each refresh. Never tears; a late frame waits for the next refresh.
    /// </summary>
    On,
    /// <summary>
    /// Waits for each refresh, but a late frame is shown at once and may tear.
    /// </summary>
    Adaptive,
    /// <summary>
    /// Shows each frame as soon as it is done. Lowest latency, and it can tear.
    /// </summary>
    Off,
}
