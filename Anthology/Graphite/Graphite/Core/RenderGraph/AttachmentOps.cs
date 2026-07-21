namespace Prowl.Graphite.RenderGraph;

/// <summary>What to do with an attachment's contents when a pass starts rendering to it.</summary>
public enum LoadAction
{
    /// <summary>Clear to a value given at record time.</summary>
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

    /// <summary>Load/store pair.</summary>
    public AttachmentOps(LoadAction load, StoreAction store)
    {
        Load = load;
        Store = store;
    }

    /// <summary>Clear then store. Usual transient target.</summary>
    public static AttachmentOps Cleared => new(LoadAction.Clear, StoreAction.Store);

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

    /// <summary>Default by lifetime: transient clears, persistent loads.</summary>
    public static TargetLoadStoreOps ForLifetime(bool persistent)
        => persistent
            ? new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded)
            : new TargetLoadStoreOps(AttachmentOps.Cleared, AttachmentOps.Cleared);
}
