using System;

namespace Prowl.Graphite;

/// <summary>
/// Base GPU resource; backends implement DisposeCore only.
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
    /// True after Dispose called; native object may outlive flag for in-flight work.
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

    private protected virtual void NameChanged(string name) { }

    private protected virtual void OnDisposing() { }

    private protected abstract void DisposeCore();
}
