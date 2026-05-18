using System;

using Silk.NET.Core.Contexts;

namespace Prowl.Veldrid.OpenGL;

/// <summary>
/// Encapsulates various pieces of OpenGL context, necessary for creating a <see cref="GraphicsDevice"/> using the OpenGL
/// API.
/// </summary>
public class OpenGLPlatformInfo
{
    /// <summary>
    /// A delegate which can be used to retrieve OpenGL function pointers by name.
    /// </summary>
    public IGLContext GLContext { get; }

    /// <summary>
    /// A delegate which can be used to set the synchronization behavior of the OpenGL context.
    /// </summary>
    public Action<bool> SetSyncToVerticalBlank { get; }

    /// <summary>
    /// A delegate which can be used to set the framebuffer used to render to the application Swapchain.
    /// If this is null, the default FBO (0) will be bound.
    /// </summary>
    public Action SetSwapchainFramebuffer { get; }

    /// <summary>
    /// A delegate which is invoked when the main Swapchain is resized. This may be null, in which case
    /// no special action is taken when the Swapchain is resized.
    /// </summary>
    public Action<uint, uint> ResizeSwapchain { get; }

    /// <summary>
    /// Constructs a new OpenGLPlatformInfo.
    /// </summary>
    /// <param name="glContext">The OpenGL context handle.</param>
    /// <param name="setSyncToVerticalBlank">A delegate which can be used to set the synchronization behavior of the OpenGL
    /// context.</param>
    public OpenGLPlatformInfo(
        IGLContext glContext,
        Action<bool> setSyncToVerticalBlank)
    {
        GLContext = glContext;
        SetSyncToVerticalBlank = setSyncToVerticalBlank;
    }

    /// <summary>
    /// Constructs a new OpenGLPlatformInfo.
    /// </summary>
    /// <param name="glContext">The OpenGL context handle.</param>
    /// <param name="setSyncToVerticalBlank">A delegate which can be used to set the synchronization behavior of the OpenGL
    /// context.</param>
    /// <param name="setSwapchainFramebuffer">A delegate which can be used to set the framebuffer used to render to the
    /// application Swapchain.</param>
    /// <param name="resizeSwapchain">A delegate which is invoked when the main Swapchain is resized. This may be null,
    /// in which case no special action is taken when the Swapchain is resized.</param>
    public OpenGLPlatformInfo(
        IGLContext glContext,
        Action<bool> setSyncToVerticalBlank,
        Action setSwapchainFramebuffer,
        Action<uint, uint> resizeSwapchain)
    {
        GLContext = glContext;
        SetSyncToVerticalBlank = setSyncToVerticalBlank;
        SetSwapchainFramebuffer = setSwapchainFramebuffer;
        ResizeSwapchain = resizeSwapchain;
    }
}
