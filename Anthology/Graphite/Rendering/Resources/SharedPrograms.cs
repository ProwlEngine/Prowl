using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The programs features build from shader library files, shared by every feature of a renderer so a new instance of
/// a feature compiles nothing already built. The renderer owns them and frees them when it is disposed. See
/// <see cref="Renderer.SharedPrograms"/>.
/// </summary>
public sealed class SharedPrograms : IDisposable
{
    private readonly Renderer _renderer;
    private readonly Dictionary<string, FullscreenProgram> _programs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LazyCompute> _computes = new(StringComparer.Ordinal);

    internal SharedPrograms(Renderer renderer) => _renderer = renderer;

    public void Dispose()
    {
        foreach (FullscreenProgram program in _programs.Values)
            program.Dispose();
        _programs.Clear();
        foreach (LazyCompute compute in _computes.Values)
            compute.Dispose();
        _computes.Clear();
    }

    /// <summary>
    /// The compute program of a library file, named past the library's prefix, made the first time. A lit one binds
    /// the renderer's frame and view tables whole, as <see cref="Renderer.CreateComputeProgram"/> tells. Host thread.
    /// </summary>
    public LazyCompute Compute(string file, bool lit = false)
    {
        string key = file + (lit ? "\n1" : "\n0");
        lock (_computes)
        {
            if (!_computes.TryGetValue(key, out LazyCompute? compute))
                _computes[key] = compute = _renderer.CreateLazyCompute(file, lit);
            return compute;
        }
    }

    /// <summary>
    /// The fullscreen program of a library file, by the name its includes use, under a set of defines, each line
    /// ending in a newline, made the first time, which the feature drawing with it waits on. Host thread.
    /// </summary>
    public FullscreenProgram Fullscreen(RenderFeature user, string include, string defines = "", bool source = true, BlendStateDescription? blend = null, bool lit = false)
    {
        string key = include + "\n" + defines + (source ? "\n1" : "\n0") + (lit ? "\n1" : "\n0");
        lock (_programs)
        {
            if (!_programs.TryGetValue(key, out FullscreenProgram? program))
            {
                program = _renderer.CreateFullscreenProgram($"#include \"{include}\"\n", include, source, blend, lit, defines);
                _programs[key] = program;
            }
            user.Uses(program);
            return program;
        }
    }
}
