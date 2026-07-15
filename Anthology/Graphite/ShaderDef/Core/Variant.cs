using System;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// A single shader variant: one fixed combination of keyword values, plus whatever backend shader
/// descriptions it has been compiled for. Pure data - it never references the compiler, so a variant
/// can be serialized and later bound with no compiler dependency.
/// </summary>
public sealed class Variant
{
    /// <summary>
    /// The fixed keyword combination this variant represents.
    /// </summary>
    public Keyword[] Keywords;

    /// <summary>
    /// The compiled shader description for each backend this variant has been compiled for. May be
    /// empty (not yet compiled), partial (some backends), or full.
    /// </summary>
    public (GraphicsBackend Backend, ShaderDescription Description)[] Compiled;


    /// <summary>
    /// Creates an empty, uncompiled variant.
    /// </summary>
    public Variant()
    {
        Keywords = [];
        Compiled = [];
    }


    /// <summary>
    /// Creates a variant with a fixed keyword combination and a set of already-compiled backends.
    /// </summary>
    public Variant(Keyword[] keywords, (GraphicsBackend Backend, ShaderDescription Description)[] compiled)
    {
        Keywords = keywords;
        Compiled = compiled;
    }


    /// <summary>
    /// True if this variant holds a compiled description for the given backend.
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
    /// Gets the compiled description for the given backend, if one has been compiled.
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
