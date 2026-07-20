using System;
using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Handed to a pass's setup so it can declare textures it reads and writes. The graph uses these to
/// allocate resources and order passes: readers run after their writers. A pass can nominate one output
/// as its main output; the graph picks which pass's main output becomes its result.
/// </summary>
public sealed class RenderContextBuilder
{
    internal readonly struct ResourceDecl(RenderResourceID id, GraphTextureDesc desc)
    {
        public readonly RenderResourceID Id = id;
        public readonly GraphTextureDesc Desc = desc;
    }

    internal readonly List<ResourceDecl> Inputs = new();
    internal readonly List<ResourceDecl> Outputs = new();
    internal RenderResourceID MainOutput;
    internal bool HasMainOutput;

    internal void Reset()
    {
        Inputs.Clear();
        Outputs.Clear();
        MainOutput = default;
        HasMainOutput = false;
    }

    /// <summary>Declares a texture this pass samples. Creates the resource if not already declared.</summary>
    public TextureHandle GetInputTexture(RenderResourceID id, GraphTextureDesc desc)
    {
        Inputs.Add(new ResourceDecl(id, desc));
        return new TextureHandle(id);
    }

    /// <summary>Declares a texture this pass renders into. Creates the resource if not already declared.</summary>
    public TextureHandle GetOutputTexture(RenderResourceID id, GraphTextureDesc desc)
    {
        Outputs.Add(new ResourceDecl(id, desc));
        return new TextureHandle(id);
    }

    /// <summary>
    /// Nominates one output as this pass's main result. If the graph picks this pass as presentation
    /// source, this is what gets surfaced, so passes never need to know the real target.
    /// </summary>
    public void SetMainOutput(TextureHandle handle)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Main output must be a valid output texture handle.", nameof(handle));

        MainOutput = handle.Id;
        HasMainOutput = true;
    }
}
