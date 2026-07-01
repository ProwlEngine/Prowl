namespace Prowl.Graphite;

/// <summary>
/// Callback invoked when this <see cref="GraphicsDevice"/> wants to surface a non-fatal warning, such as an
/// implicit <see cref="DeviceBuffer"/> reallocation or a transient buffer soft cap being exceeded.
/// </summary>
/// <param name="message">A human-readable description of the condition being warned about.</param>
public delegate void GraphicsDeviceWarningHandler(string message);
