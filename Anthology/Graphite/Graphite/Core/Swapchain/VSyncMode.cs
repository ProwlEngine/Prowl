namespace Prowl.Graphite;

/// <summary>
/// How a swapchain paces its presents against the display's refresh. Where the surface lacks a mode, Mailbox and Off stand in for each other and Adaptive for On, which every surface offers.
/// </summary>
public enum VSyncMode
{
    // Mailbox is first so a default SwapchainDescription presents uncapped, and tear-free where the surface offers Mailbox
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
