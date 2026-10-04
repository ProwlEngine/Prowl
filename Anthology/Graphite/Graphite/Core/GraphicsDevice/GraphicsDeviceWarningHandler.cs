namespace Prowl.Graphite;

/// <summary>
/// Fires on a non-fatal device warning, e.g. implicit buffer reallocation.
/// </summary>
/// <param name="message">What happened.</param>
public delegate void GraphicsDeviceWarningHandler(string message);
