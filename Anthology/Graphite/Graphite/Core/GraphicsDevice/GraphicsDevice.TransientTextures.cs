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
    /// Rents a render-texture bundle from the transient pool and returns its primary color attachment.
    /// <para>
    /// Backing textures come from a device-level free-list keyed by desc, and survive across executions:
    /// once the rent-time execution finishes on GPU, the bundle goes back to the free-list and a later
    /// rent with an equal desc reuses it. A bundle still in flight is never handed to another caller.
    /// </para>
    /// <para>
    /// Returned texture has RenderTarget | Sampled usage. To render the whole bundle (all color
    /// plus depth), rent the framebuffer instead via RentTransientFramebuffer.
    /// </para>
    /// </summary>
    /// <param name="task">Execution renting the bundle; it returns to the free-list once this completes on GPU.</param>
    /// <param name="desc">Bundle to rent. Needs at least one color format.</param>
    /// <returns>Primary (first) color attachment of the rented bundle.</returns>
    /// <exception cref="RenderException">Thrown if desc has no color attachments.</exception>
    public Texture RentTransientTexture(ExecutionTask task, in RenderTextureDescription desc)
    {
        if (desc.ColorFormats.Length == 0)
            throw new RenderException($"{nameof(RentTransientTexture)} requires a description with at least one color format. Use {nameof(RentTransientFramebuffer)} for a depth-only bundle.");

        return RentTransientBundle(task, desc).Texture.ColorTextures[0];
    }

    /// <summary>
    /// Rents a render-texture bundle from the transient pool and returns its framebuffer.
    /// <para>
    /// Shares the same free-list as RentTransientTexture. Attachments are reachable through
    /// Framebuffer.ColorTargets and Framebuffer.DepthTarget.
    /// </para>
    /// </summary>
    /// <param name="task">Execution renting the bundle; it returns to the free-list once this completes on GPU.</param>
    /// <param name="desc">Bundle to rent.</param>
    /// <returns>Framebuffer of the rented bundle.</returns>
    /// <exception cref="RenderException">Thrown if desc has no attachments at all.</exception>
    public Framebuffer RentTransientFramebuffer(ExecutionTask task, in RenderTextureDescription desc)
    {
        return RentTransientBundle(task, desc).Texture.Framebuffer;
    }

    /// <summary>
    /// Rents a render-texture bundle from the transient pool.
    /// <para>
    /// Shares the same free-list as RentTransientTexture and RentTransientFramebuffer. Bind Framebuffer
    /// to render into the bundle, or bind ColorTextures/DepthTexture directly to sample from it.
    /// </para>
    /// </summary>
    /// <param name="task">Execution renting the bundle; it returns to the free-list once this completes on GPU.</param>
    /// <param name="desc">Bundle to rent.</param>
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
