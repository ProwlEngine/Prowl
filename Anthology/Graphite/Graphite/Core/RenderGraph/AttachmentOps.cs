namespace Prowl.Graphite.RenderGraph;

/// <summary>What happens to an attachment's existing contents when a pass begins rendering to it.</summary>
public enum LoadAction
{
    /// <summary>Clear to a value supplied at record time.</summary>
    Clear,

    /// <summary>Preserve the attachment's existing contents.</summary>
    Load,

    /// <summary>Contents are undefined; the pass fully overwrites them.</summary>
    DontCare
}

/// <summary>What happens to an attachment's contents when a pass finishes rendering to it.</summary>
public enum StoreAction
{
    /// <summary>Keep the rendered contents.</summary>
    Store,

    /// <summary>Discard the contents; nothing later reads them.</summary>
    DontCare
}

/// <summary>Load and store operations for one attachment.</summary>
public struct AttachmentOps
{
    /// <summary>How existing contents are treated when rendering begins.</summary>
    public LoadAction Load;

    /// <summary>How rendered contents are treated when rendering ends.</summary>
    public StoreAction Store;

    /// <summary>Load/store pair.</summary>
    public AttachmentOps(LoadAction load, StoreAction store)
    {
        Load = load;
        Store = store;
    }

    /// <summary>Clear on load, store on end - the usual transient target.</summary>
    public static AttachmentOps Cleared => new(LoadAction.Clear, StoreAction.Store);

    /// <summary>Load on begin, store on end - the usual persistent/history/imported target.</summary>
    public static AttachmentOps Loaded => new(LoadAction.Load, StoreAction.Store);

    /// <summary>Discard on both ends.</summary>
    public static AttachmentOps Discard => new(LoadAction.DontCare, StoreAction.DontCare);
}

/// <summary>Load/store operations for a render target's color attachments and its depth attachment.</summary>
public struct TargetLoadStoreOps
{
    /// <summary>Operations applied to every color attachment.</summary>
    public AttachmentOps Color;

    /// <summary>Operations applied to the depth attachment, if any.</summary>
    public AttachmentOps Depth;

    /// <summary>Load/store pair for color and depth.</summary>
    public TargetLoadStoreOps(AttachmentOps color, AttachmentOps depth)
    {
        Color = color;
        Depth = depth;
    }

    /// <summary>The lifetime-driven default: transient targets clear, persistent targets load.</summary>
    public static TargetLoadStoreOps ForLifetime(bool persistent)
        => persistent
            ? new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded)
            : new TargetLoadStoreOps(AttachmentOps.Cleared, AttachmentOps.Cleared);
}
