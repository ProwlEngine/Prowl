namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    private TransientTexturePool _transientTexturePool;
    private readonly object _transientTexturePoolLock = new();

    private TransientTexturePool TransientTexturePool
    {
        get
        {
            if (_transientTexturePool == null)
            {
                lock (_transientTexturePoolLock)
                {
                    _transientTexturePool ??= new TransientTexturePool(this);
                }
            }
            return _transientTexturePool;
        }
    }

    /// <summary>
    /// Rents a transient render-texture bundle, returns its first color attachment.
    /// <para>
    /// Backed by a device free-list keyed by desc. Bundle returns to the pool once the
    /// rent-time execution finishes on GPU; never handed out while still in flight.
    /// </para>
    /// <para>
    /// Texture has RenderTarget | Sampled usage. Need the whole bundle? Use RentTransientFramebuffer.
    /// </para>
    /// </summary>
    /// <param name="task">Execution renting the bundle. Returns to pool once it finishes on GPU.</param>
    /// <param name="desc">Bundle desc. Needs at least one color format.</param>
    /// <returns>First color attachment.</returns>
    /// <exception cref="RenderException">No color attachments in desc.</exception>
    public Texture RentTransientTexture(ExecutionTask task, in RenderTextureDescription desc)
    {
        if (desc.ColorFormats.Length == 0)
            throw new RenderException($"{nameof(RentTransientTexture)} requires a description with at least one color format. Use {nameof(RentTransientFramebuffer)} for a depth-only bundle.");

        return RentTransientBundle(task, desc).Texture.ColorTextures[0];
    }

    /// <summary>
    /// Rents a transient render-texture bundle, returns its framebuffer.
    /// <para>
    /// Same free-list as RentTransientTexture. Attachments are Framebuffer.ColorTargets / DepthTarget.
    /// </para>
    /// </summary>
    /// <param name="task">Execution renting the bundle. Returns to pool once it finishes on GPU.</param>
    /// <param name="desc">Bundle desc.</param>
    /// <returns>Framebuffer of the rented bundle.</returns>
    /// <exception cref="RenderException">No attachments in desc.</exception>
    public Framebuffer RentTransientFramebuffer(ExecutionTask task, in RenderTextureDescription desc)
    {
        return RentTransientBundle(task, desc).Texture.Framebuffer;
    }

    /// <summary>
    /// Rents a transient render-texture bundle.
    /// <para>
    /// Same free-list as RentTransientTexture/RentTransientFramebuffer. Bind Framebuffer to render
    /// into it, or bind ColorTextures/DepthTexture to sample from it.
    /// </para>
    /// </summary>
    /// <param name="task">Execution renting the bundle. Returns to pool once it finishes on GPU.</param>
    /// <param name="desc">Bundle desc.</param>
    /// <returns>The rented RenderTexture.</returns>
    public RenderTexture RentTransientRenderTexture(ExecutionTask task, in RenderTextureDescription desc)
    {
        return RentTransientBundle(task, desc).Texture;
    }

    private TransientTexturePool.PooledBundle RentTransientBundle(ExecutionTask task, in RenderTextureDescription desc)
    {
        ValidationHelpers.RequireNotNull(task, nameof(task), nameof(RentTransientFramebuffer));
        return TransientTexturePool.Rent(desc, task.Id);
    }
}
