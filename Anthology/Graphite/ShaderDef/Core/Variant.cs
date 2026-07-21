using System;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// One fixed keyword combination plus whatever backend shader descriptions it's compiled for. Pure
/// data, no compiler reference, so it can be serialized and bound standalone.
/// </summary>
public sealed class Variant
{
    /// <summary>
    /// This variant's fixed keyword combination.
    /// </summary>
    public Keyword[] Keywords;

    /// <summary>
    /// Compiled description per backend. May be empty, partial, or full.
    /// </summary>
    public (GraphicsBackend Backend, ShaderDescription Description)[] Compiled;


    /// <summary>
    /// Makes an empty, uncompiled variant.
    /// </summary>
    public Variant()
    {
        Keywords = [];
        Compiled = [];
    }


    /// <summary>
    /// Makes a variant with fixed keywords and already-compiled backends.
    /// </summary>
    public Variant(Keyword[] keywords, (GraphicsBackend Backend, ShaderDescription Description)[] compiled)
    {
        Keywords = keywords;
        Compiled = compiled;
    }


    /// <summary>
    /// True if compiled for the given backend.
    /// </summary>
    public bool IsCompiledFor(GraphicsBackend backend)
    {
        for (int i = 0; i < Compiled.Length; i++)
        {
            if (Compiled[i].Backend == backend)
                return true;
        }

        return false;
    }


    /// <summary>
    /// Gets the compiled description for the backend, if any.
    /// </summary>
    public bool TryGetDescription(GraphicsBackend backend, out ShaderDescription description)
    {
        for (int i = 0; i < Compiled.Length; i++)
        {
            if (Compiled[i].Backend == backend)
            {
                description = Compiled[i].Description;
                return true;
            }
        }

        description = default;
        return false;
    }


    internal void Store(GraphicsBackend backend, ShaderDescription description)
    {
        for (int i = 0; i < Compiled.Length; i++)
        {
            if (Compiled[i].Backend == backend)
            {
                Compiled[i] = (backend, description);
                return;
            }
        }

        (GraphicsBackend, ShaderDescription)[] next = new (GraphicsBackend, ShaderDescription)[Compiled.Length + 1];
        Array.Copy(Compiled, next, Compiled.Length);
        next[Compiled.Length] = (backend, description);
        Compiled = next;
    }
}
