using System;

namespace Prowl.Graphite;

/// <summary>
/// Base for every GraphicsDevice-owned resource. Owns the debug name and the disposal contract, so a
/// backend implements <see cref="DisposeCore"/> and nothing else.
/// </summary>
public abstract class GraphicsResource : IDisposable
{
    private string _name = string.Empty;

    /// <summary>
    /// Debug name, shows up in graphics debuggers.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            _name = value ?? string.Empty;
            NameChanged(_name);
        }
    }

    /// <summary>
    /// True once <see cref="Dispose"/> has been called. The native object may outlive this flag if the
    /// backend keeps it alive for in-flight work.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Frees device resources. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        OnDisposing();
        DisposeCore();
    }

    /// <summary>Backend hook, runs after <see cref="Name"/> changes.</summary>
    private protected virtual void NameChanged(string name) { }

    /// <summary>Hook for base classes that own managed state, runs before <see cref="DisposeCore"/>.</summary>
    private protected virtual void OnDisposing() { }

    /// <summary>Backend disposal. Runs at most once.</summary>
    private protected abstract void DisposeCore();
}
