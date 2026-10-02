using Prowl.Vector;

namespace Prowl.Graphite.RenderGraph;

/// <summary>What to do with an attachment's contents when a pass starts rendering to it.</summary>
public enum LoadAction
{
    /// <summary>Clear to the value carried by the declaration.</summary>
    Clear,

    /// <summary>Keep existing contents.</summary>
    Load,

    /// <summary>Undefined; pass overwrites everything.</summary>
    DontCare
}

/// <summary>What to do with an attachment's contents when a pass finishes rendering to it.</summary>
public enum StoreAction
{
    /// <summary>Keep rendered contents.</summary>
    Store,

    /// <summary>Discard; nobody reads it later.</summary>
    DontCare
}

/// <summary>Load/store ops for one attachment.</summary>
public struct AttachmentOps
{
    /// <summary>Load behavior.</summary>
    public LoadAction Load;

    /// <summary>Store behavior.</summary>
    public StoreAction Store;

    /// <summary>Color clear value, used when Load is Clear.</summary>
    public Color ClearColor;

    /// <summary>Depth clear value, used when Load is Clear.</summary>
    public float ClearDepth;

    /// <summary>Stencil clear value, used when Load is Clear.</summary>
    public byte ClearStencil;

    /// <summary>Load/store pair. Clear values default to black, depth 1, stencil 0.</summary>
    public AttachmentOps(LoadAction load, StoreAction store)
    {
        Load = load;
        Store = store;
        ClearColor = default;
        ClearDepth = 1f;
        ClearStencil = 0;
    }

    /// <summary>Clear to black or depth 1, then store. Usual transient target.</summary>
    public static AttachmentOps Cleared => new(LoadAction.Clear, StoreAction.Store);

    /// <summary>Clear to a color, then store.</summary>
    public static AttachmentOps Clear(Color color)
    {
        AttachmentOps ops = Cleared;
        ops.ClearColor = color;
        return ops;
    }

    /// <summary>Clear depth and stencil, then store.</summary>
    public static AttachmentOps Clear(float depth, byte stencil = 0)
    {
        AttachmentOps ops = Cleared;
        ops.ClearDepth = depth;
        ops.ClearStencil = stencil;
        return ops;
    }

    /// <summary>Load then store. Usual persistent/history/imported target.</summary>
    public static AttachmentOps Loaded => new(LoadAction.Load, StoreAction.Store);

    /// <summary>Discard both ends.</summary>
    public static AttachmentOps Discard => new(LoadAction.DontCare, StoreAction.DontCare);
}

/// <summary>Load/store ops for a target's color and depth attachments.</summary>
public struct TargetLoadStoreOps
{
    /// <summary>Ops for every color attachment.</summary>
    public AttachmentOps Color;

    /// <summary>Ops for depth attachment, if any.</summary>
    public AttachmentOps Depth;

    /// <summary>Load/store pair for color and depth.</summary>
    public TargetLoadStoreOps(AttachmentOps color, AttachmentOps depth)
    {
        Color = color;
        Depth = depth;
    }

    /// <summary>Clear color to a value and depth to a value, both stored.</summary>
    public static TargetLoadStoreOps Clear(Color color, float depth = 1f, byte stencil = 0)
        => new(AttachmentOps.Clear(color), AttachmentOps.Clear(depth, stencil));

    /// <summary>Default by lifetime: transient clears, persistent loads.</summary>
    public static TargetLoadStoreOps ForLifetime(bool persistent)
        => persistent
            ? new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded)
            : new TargetLoadStoreOps(AttachmentOps.Cleared, AttachmentOps.Cleared);
}
