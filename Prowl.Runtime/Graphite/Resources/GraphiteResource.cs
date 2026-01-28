// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Base class for all GPU resources in Graphite.
/// Resources are immutable after creation and must be explicitly disposed.
/// </summary>
public abstract class GraphiteResource : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Optional debug name for graphics debuggers.
    /// </summary>
    public string? DebugName { get; protected set; }

    /// <summary>
    /// Whether this resource has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Disposes the resource, releasing GPU memory.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            DisposeResources();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Override to release backend-specific resources.
    /// </summary>
    protected abstract void DisposeResources();

    /// <summary>
    /// Throws if the resource has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    ~GraphiteResource()
    {
        if (!_disposed)
        {
            Debug.LogWarning($"GraphiteResource '{DebugName ?? GetType().Name}' was not disposed before finalization.");
            // Don't dispose here - GPU resources must be released on the main thread
        }
    }
}
