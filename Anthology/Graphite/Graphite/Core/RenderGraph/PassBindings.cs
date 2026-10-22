using System.Collections.Generic;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite;

internal sealed class PassBindings
{
    private readonly Dictionary<Framebuffer, TargetLoadStoreOps> _ops = new();

    public HashSet<ResourceId> Known { get; } = new();

    public HashSet<ResourceId> Declared { get; } = new();

    public string PassName { get; set; } = "";

    public void Reset(string passName)
    {
        PassName = passName;
        Declared.Clear();
        _ops.Clear();
    }

    public void Declare(RenderTexture texture)
    {
        foreach (Texture color in texture.ColorTextures)
            Declared.Add(color.ResourceId);
        if (texture.DepthTexture != null)
            Declared.Add(texture.DepthTexture.ResourceId);
    }

    public void Know(RenderTexture texture)
    {
        foreach (Texture color in texture.ColorTextures)
            Known.Add(color.ResourceId);
        if (texture.DepthTexture != null)
            Known.Add(texture.DepthTexture.ResourceId);
    }

    public void SetOps(Framebuffer framebuffer, in TargetLoadStoreOps ops) => _ops.TryAdd(framebuffer, ops);

    public TargetLoadStoreOps? TakeOps(Framebuffer framebuffer)
        => _ops.Remove(framebuffer, out TargetLoadStoreOps ops) ? ops : null;

    public void Check(ResourceId id, string name)
    {
        if (Known.Contains(id) && !Declared.Contains(id))
            throw new RenderGraphException($"Pass '{PassName}' binds '{name}', which it did not declare.", PassName);
    }
}
