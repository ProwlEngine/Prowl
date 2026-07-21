using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Handed to a pass's setup so it can declare the resources it reads and writes. The graph uses these to
/// allocate resources and order passes: readers run after their writers. Inputs are declared by ID only -
/// the pass that outputs a resource (or a central pipeline declaration) owns its description, so a reader
/// never restates it.
/// </summary>
public sealed class RenderContextBuilder
{
    internal readonly List<RenderResourceID> Inputs = new();
    internal readonly List<GraphResource> Outputs = new();

    internal void Reset()
    {
        Inputs.Clear();
        Outputs.Clear();
    }

    /// <summary>Declares a texture this pass samples. The producing pass or a central declaration owns its description.</summary>
    public TextureHandle GetInputTexture(RenderResourceID id)
    {
        Inputs.Add(id);
        return new TextureHandle(id);
    }

    /// <summary>
    /// Declares a texture this pass renders into. Creates the resource if not already declared. A non-zero
    /// history depth makes it a persistent versioned resource: the graph keeps a ring of history+1 physical
    /// copies and rotates the current one each execution, so reads can resolve prior executions by age.
    /// </summary>
    public TextureHandle GetOutputTexture(RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)
    {
        Outputs.Add(new GraphTextureResource(id, desc, history, ops));
        return new TextureHandle(id);
    }

    /// <summary>
    /// Imports an externally-owned render target into the graph under the given ID so passes can read it and
    /// order around it. The caller keeps ownership; the graph never disposes it.
    /// </summary>
    public TextureHandle ImportTexture(RenderResourceID id, RenderTexture existing)
    {
        Outputs.Add(new GraphImportedTextureResource(id, existing));
        return new TextureHandle(id);
    }

    /// <summary>Declares a buffer this pass reads. The producing pass or a central declaration owns its description.</summary>
    public BufferHandle GetInputBuffer(RenderResourceID id)
    {
        Inputs.Add(id);
        return new BufferHandle(id);
    }

    /// <summary>
    /// Declares a buffer this pass writes. Creates the resource if not already declared. A non-zero history
    /// depth makes it a persistent versioned resource: the graph keeps a ring of history+1 physical copies
    /// and rotates the current one each execution, so reads can resolve prior executions by age.
    /// </summary>
    public BufferHandle GetOutputBuffer(RenderResourceID id, GraphBufferDesc desc, int history = 0)
    {
        Outputs.Add(new GraphBufferResource(id, desc, history));
        return new BufferHandle(id);
    }
}
