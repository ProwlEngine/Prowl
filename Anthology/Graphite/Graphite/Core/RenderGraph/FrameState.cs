using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

internal sealed class FrameResource
{
    public readonly List<int> Writers = new();
    public RenderResourceID Id;
    public int Scope;
    public bool IsTexture;
    public Lifetime Lifetime;
    public bool Imported;
    public bool IsTarget;
    public GraphTextureDesc Texture;
    public GraphBufferDesc Buffer;
    public RenderTexture? ImportedTexture;
    public DeviceBuffer? ImportedBuffer;
    public Framebuffer? TargetFramebuffer;
    public PixelFormat? TargetDepth;
    public bool Storage;
    public int Version;
    public bool Written;
    public int FirstWriter;
    public int LastAccess;
    public TextureRings? TextureRings;
    public BufferRings? BufferRings;

    public bool IsTransient => !Imported && Lifetime.IsTransient;

    public bool IsRetained => !Imported && Lifetime.IsRetained;

    public GraphResourceKind Kind => IsTexture ? GraphResourceKind.Texture : GraphResourceKind.Buffer;

    public string Name => RenderResourceID.ToString(Id) ?? Id.ToString();

    public void Reset()
    {
        Writers.Clear();
        Id = default;
        Scope = 0;
        IsTexture = false;
        Lifetime = default;
        Imported = false;
        IsTarget = false;
        Texture = default;
        Buffer = default;
        ImportedTexture = null;
        ImportedBuffer = null;
        TargetFramebuffer = null;
        TargetDepth = null;
        Storage = false;
        Version = 0;
        Written = false;
        FirstWriter = -1;
        LastAccess = -1;
        TextureRings = null;
        BufferRings = null;
    }
}

internal abstract class PassRenderer
{
    public abstract void Invoke(PassContext context);
}

internal sealed class PlainRenderer(Action<PassContext> render) : PassRenderer
{
    public override void Invoke(PassContext context) => render(context);
}

internal sealed class DataRenderer<T>(T data, Action<PassContext, T> render) : PassRenderer
{
    public override void Invoke(PassContext context) => render(context, data);
}

internal sealed class FramePass
{
    public readonly List<ResourceAccess> Accesses = new();
    public string Name = "";
    public int Group;
    public PassRenderer Render = null!;

    public bool Declares(int resource)
    {
        foreach (ResourceAccess access in Accesses)
        {
            if (access.Resource == resource)
                return true;
        }
        return false;
    }

    public void Reset(string name, int group)
    {
        Accesses.Clear();
        Name = name;
        Group = group;
        Render = null!;
    }
}
