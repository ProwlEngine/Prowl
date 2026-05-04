// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

namespace Prowl.Runtime;

/// <summary>
/// Scoped guard around a block of offscreen rendering — captures Prowl's tracked GL
/// state on entry, restores it on disposal so surrounding rendering is undisturbed.
/// </summary>
/// <remarks>
/// Captured state covers everything <see cref="Graphics"/> caches:
/// <list type="bullet">
///   <item>Bound framebuffer (read/draw/both)</item>
///   <item>Viewport (queried directly via glGet — Prowl doesn't cache it)</item>
///   <item>Rasterizer state (depth test/write/func, blend on/off + factors + equation, cull, winding)</item>
///   <item>Currently bound shader program</item>
/// </list>
/// Vertex array objects are not cached — each draw call binds its own — so they don't
/// need to be saved here.
///
/// Example:
/// <code>
/// using (OffscreenRenderScope.Begin())
/// {
///     // any number of Graphics.BindFramebuffer / RenderPipeline.Blit / glReadPixels calls
/// }
/// // all snapshotted state restored
/// </code>
/// </remarks>
public readonly struct OffscreenRenderScope : IDisposable
{
    private readonly GraphicsFrameBuffer? _previousFramebuffer;
    private readonly int _vpX, _vpY, _vpW, _vpH;
    private readonly RasterizerState _previousRasterizer;
    private readonly GraphicsProgram? _previousProgram;
    private readonly int _previousActiveTexture;
    private readonly int _previousVAO;

    private OffscreenRenderScope(
        GraphicsFrameBuffer? prevFB,
        int x, int y, int w, int h,
        RasterizerState prevRaster,
        GraphicsProgram? prevProgram,
        int prevActiveTex,
        int prevVAO)
    {
        _previousFramebuffer = prevFB;
        _vpX = x; _vpY = y; _vpW = w; _vpH = h;
        _previousRasterizer = prevRaster;
        _previousProgram = prevProgram;
        _previousActiveTexture = prevActiveTex;
        _previousVAO = prevVAO;
    }

    /// <summary>
    /// Snapshot framebuffer + viewport + rasterizer state + bound program + active
    /// texture unit + bound VAO. Pair with <c>using</c> so disposal always restores them.
    /// </summary>
    public static OffscreenRenderScope Begin()
    {
        var prevFB = Graphics.GetCurrentFramebuffer();

        Span<int> vp = stackalloc int[4];
        unsafe
        {
            fixed (int* p = vp)
                Graphics.GL.GetInteger(GLEnum.Viewport, p);
        }

        var prevRaster = Graphics.GetState();
        var prevProgram = Graphics.CurrentProgram;

        // Active texture unit and bound VAO are raw GL state — not cached by Prowl but
        // still global state that a tool's blits will trample.
        Graphics.GL.GetInteger(GLEnum.ActiveTexture, out int prevActiveTex);
        Graphics.GL.GetInteger(GLEnum.VertexArrayBinding, out int prevVAO);

        return new OffscreenRenderScope(prevFB, vp[0], vp[1], vp[2], vp[3],
            prevRaster, prevProgram, prevActiveTex, prevVAO);
    }

    public void Dispose()
    {
        // Framebuffer first — BindFramebuffer overrides the viewport, so viewport restore
        // must come after.
        if (_previousFramebuffer != null)
            Graphics.BindFramebuffer(_previousFramebuffer);
        else
            Graphics.UnbindFramebuffer();

        Graphics.Viewport(_vpX, _vpY, (uint)Math.Max(0, _vpW), (uint)Math.Max(0, _vpH));

        // force=true: Prowl's rasterizer cache may still match the modified state from
        // inside the scope, so a normal SetState would no-op. Force re-issues the GL calls.
        Graphics.SetState(_previousRasterizer, force: true);

        if (_previousProgram != null)
            Graphics.BindProgram(_previousProgram);

        Graphics.GL.ActiveTexture((TextureUnit)_previousActiveTexture);
        Graphics.GL.BindVertexArray((uint)_previousVAO);
    }
}
