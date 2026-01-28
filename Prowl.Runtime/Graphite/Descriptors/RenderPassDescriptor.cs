// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Describes a color attachment for a render pass.
/// </summary>
public struct RenderPassColorAttachment
{
    /// <summary>Texture to render to.</summary>
    public Texture Texture;

    /// <summary>Mip level to render to.</summary>
    public uint MipLevel;

    /// <summary>Array layer to render to.</summary>
    public uint ArrayLayer;

    /// <summary>Optional resolve target for MSAA.</summary>
    public Texture? ResolveTarget;

    /// <summary>What to do with existing contents.</summary>
    public LoadOp LoadOp;

    /// <summary>What to do with rendered contents.</summary>
    public StoreOp StoreOp;

    /// <summary>Clear color if LoadOp is Clear.</summary>
    public Float4 ClearColor;

    public RenderPassColorAttachment()
    {
        Texture = null!;
        MipLevel = 0;
        ArrayLayer = 0;
        ResolveTarget = null;
        LoadOp = LoadOp.Clear;
        StoreOp = StoreOp.Store;
        ClearColor = Float4.Zero;
    }

    /// <summary>
    /// Creates a color attachment that clears to a color.
    /// </summary>
    public static RenderPassColorAttachment Clear(Texture texture, Float4 clearColor) => new()
    {
        Texture = texture,
        LoadOp = LoadOp.Clear,
        StoreOp = StoreOp.Store,
        ClearColor = clearColor,
    };

    /// <summary>
    /// Creates a color attachment that clears to black.
    /// </summary>
    public static RenderPassColorAttachment ClearBlack(Texture texture) => Clear(texture, new Float4(0, 0, 0, 1));

    /// <summary>
    /// Creates a color attachment that loads existing contents.
    /// </summary>
    public static RenderPassColorAttachment Load(Texture texture) => new()
    {
        Texture = texture,
        LoadOp = LoadOp.Load,
        StoreOp = StoreOp.Store,
    };

    /// <summary>
    /// Creates a color attachment with MSAA resolve.
    /// </summary>
    public static RenderPassColorAttachment ClearAndResolve(Texture msaaTexture, Texture resolveTarget, Float4 clearColor) => new()
    {
        Texture = msaaTexture,
        LoadOp = LoadOp.Clear,
        StoreOp = StoreOp.Store,
        ClearColor = clearColor,
        ResolveTarget = resolveTarget,
    };
}

/// <summary>
/// Describes a depth/stencil attachment for a render pass.
/// </summary>
public struct RenderPassDepthStencilAttachment
{
    /// <summary>Texture to use for depth/stencil.</summary>
    public Texture Texture;

    /// <summary>Mip level to render to.</summary>
    public uint MipLevel;

    /// <summary>Array layer to render to.</summary>
    public uint ArrayLayer;

    /// <summary>What to do with existing depth contents.</summary>
    public LoadOp DepthLoadOp;

    /// <summary>What to do with rendered depth contents.</summary>
    public StoreOp DepthStoreOp;

    /// <summary>Clear depth value if DepthLoadOp is Clear.</summary>
    public float DepthClearValue;

    /// <summary>Whether depth is read-only (no writes).</summary>
    public bool DepthReadOnly;

    /// <summary>What to do with existing stencil contents.</summary>
    public LoadOp StencilLoadOp;

    /// <summary>What to do with rendered stencil contents.</summary>
    public StoreOp StencilStoreOp;

    /// <summary>Clear stencil value if StencilLoadOp is Clear.</summary>
    public byte StencilClearValue;

    /// <summary>Whether stencil is read-only (no writes).</summary>
    public bool StencilReadOnly;

    public RenderPassDepthStencilAttachment()
    {
        Texture = null!;
        MipLevel = 0;
        ArrayLayer = 0;
        DepthLoadOp = LoadOp.Clear;
        DepthStoreOp = StoreOp.Store;
        DepthClearValue = 1.0f;
        DepthReadOnly = false;
        StencilLoadOp = LoadOp.Clear;
        StencilStoreOp = StoreOp.DontCare;
        StencilClearValue = 0;
        StencilReadOnly = false;
    }

    /// <summary>
    /// Creates a depth attachment that clears to 1.0 (far plane).
    /// </summary>
    public static RenderPassDepthStencilAttachment Clear(Texture texture, float clearValue = 1.0f) => new()
    {
        Texture = texture,
        DepthLoadOp = LoadOp.Clear,
        DepthStoreOp = StoreOp.Store,
        DepthClearValue = clearValue,
    };

    /// <summary>
    /// Creates a depth attachment that loads existing contents.
    /// </summary>
    public static RenderPassDepthStencilAttachment Load(Texture texture) => new()
    {
        Texture = texture,
        DepthLoadOp = LoadOp.Load,
        DepthStoreOp = StoreOp.Store,
    };

    /// <summary>
    /// Creates a read-only depth attachment for depth testing without writing.
    /// </summary>
    public static RenderPassDepthStencilAttachment ReadOnly(Texture texture) => new()
    {
        Texture = texture,
        DepthLoadOp = LoadOp.Load,
        DepthStoreOp = StoreOp.Store,
        DepthReadOnly = true,
        StencilReadOnly = true,
    };

    /// <summary>
    /// Creates a transient depth attachment that discards contents after the pass.
    /// </summary>
    public static RenderPassDepthStencilAttachment Transient(Texture texture) => new()
    {
        Texture = texture,
        DepthLoadOp = LoadOp.Clear,
        DepthStoreOp = StoreOp.DontCare,
        DepthClearValue = 1.0f,
    };
}

/// <summary>
/// Describes how to begin a render pass.
/// </summary>
public struct RenderPassDescriptor
{
    /// <summary>Color attachments (render targets).</summary>
    public RenderPassColorAttachment[]? ColorAttachments;

    /// <summary>Optional depth/stencil attachment.</summary>
    public RenderPassDepthStencilAttachment? DepthStencilAttachment;

    /// <summary>Optional debug name for graphics debuggers.</summary>
    public string? DebugName;

    public RenderPassDescriptor()
    {
        ColorAttachments = null;
        DepthStencilAttachment = null;
        DebugName = null;
    }

    /// <summary>
    /// Creates a render pass with a single color attachment.
    /// </summary>
    public static RenderPassDescriptor SingleColor(RenderPassColorAttachment colorAttachment, RenderPassDepthStencilAttachment? depthStencil = null) => new()
    {
        ColorAttachments = [colorAttachment],
        DepthStencilAttachment = depthStencil,
    };

    /// <summary>
    /// Creates a depth-only render pass (e.g., shadow mapping).
    /// </summary>
    public static RenderPassDescriptor DepthOnly(RenderPassDepthStencilAttachment depthStencil) => new()
    {
        ColorAttachments = null,
        DepthStencilAttachment = depthStencil,
    };
}
