// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Guards <see cref="HandleContext"/>'s arbitration: which control the cursor is nearest to, and
/// when a registration counts at all. Drag capture is not covered here because it needs real mouse
/// state, which is not available headlessly.
/// </summary>
public class HandleContextTests
{
    private static readonly Rect Viewport = new(0, 0, 800, 600);

    private static (HandleContext ctx, Camera cam) MakeContext()
    {
        var go = new GameObject("HandleContextTestCamera");
        var cam = go.AddComponent<Camera>();
        return (new HandleContext(), cam);
    }

    /// <summary>Registrations resolve at the next BeginFrame, so a handle acts on the previous
    /// pass's winner. Two frames of the same registrations settle on the nearest.</summary>
    [Fact]
    public void Nearest_IsTheSmallestRegisteredDistance()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID far = ctx.GetControlID("far");
        ControlID near = ctx.GetControlID("near");
        ctx.AddControl(far, 40f);
        ctx.AddControl(near, 3f);

        // Nothing resolved yet on the pass that registered.
        Assert.False(ctx.IsNearest(near));

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(near));
        Assert.False(ctx.IsNearest(far));
    }

    /// <summary>Ties go to the later registration, so a caller can register a broad body first and
    /// specific handles after it.</summary>
    [Fact]
    public void ExactTie_GoesToTheLaterRegistration()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID first = ctx.GetControlID("first");
        ControlID second = ctx.GetControlID("second");
        ctx.AddControl(first, 5f);
        ctx.AddControl(second, 5f);

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(second));
    }

    /// <summary>MaxValue means "not under the cursor", so a handle can register unconditionally.
    /// Without this the wrappers, which report MaxValue when not hovered, would win every frame and
    /// permanently starve the default control.</summary>
    [Fact]
    public void MaxValueDistance_IsNotACandidate()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID absent = ctx.GetControlID("absent");
        ControlID fallback = ctx.GetControlID("fallback");
        ctx.AddControl(absent, float.MaxValue);
        ctx.AddDefaultControl(fallback);

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.False(ctx.IsNearest(absent));
        Assert.True(ctx.IsNearest(fallback));
    }

    [Fact]
    public void NonFiniteDistance_IsNotACandidate()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID infinite = ctx.GetControlID("infinite");
        ControlID nan = ctx.GetControlID("nan");
        ControlID fallback = ctx.GetControlID("fallback");
        ctx.AddControl(infinite, float.PositiveInfinity);
        ctx.AddControl(nan, float.NaN);
        ctx.AddDefaultControl(fallback);

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(fallback));
    }

    /// <summary>The default control is the viewport's object picking: it must lose to any real
    /// handle, however far away that handle reported itself.</summary>
    [Fact]
    public void DefaultControl_LosesToAnyRegisteredControl()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID body = ctx.GetControlID("body");
        ControlID fallback = ctx.GetControlID("fallback");
        ctx.AddDefaultControl(fallback);
        ctx.AddControl(body, HandleContext.BodyDistance);

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(body));
        Assert.False(ctx.IsNearest(fallback));
    }

    [Fact]
    public void NoRegistrations_LeavesNothingNearest()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);

        Assert.False(ctx.Nearest.IsValid);
        Assert.False(ctx.Hot.IsValid);
    }

    /// <summary>The indexed overload is what makes per-item handle loops practical, so it must not
    /// collide across indices or across names.</summary>
    [Fact]
    public void IndexedControlIDs_AreDistinct()
    {
        var (ctx, _) = MakeContext();

        var seen = new HashSet<int>();
        for (int i = 0; i < 256; i++)
            Assert.True(seen.Add(ctx.GetControlID("probe", i).Value));

        Assert.NotEqual(ctx.GetControlID("probe", 7), ctx.GetControlID("vertex", 7));
        Assert.Equal(ctx.GetControlID("probe", 7), ctx.GetControlID("probe", 7));
    }

    /// <summary>
    /// A handle's grab radius is a region, not a bias. A handle the cursor is nowhere near must not
    /// be a candidate at all - otherwise every registered handle outranks the viewport's default
    /// control from anywhere on screen and object picking silently stops working.
    /// </summary>
    [Fact]
    public void PointOutsideGrabRadius_DoesNotBeatTheDefaultControl()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID handle = ctx.GetControlID("handle");
        ControlID pick = ctx.GetControlID("pick");

        // Cursor at the viewport origin, handle 400px away with an 8px grab radius.
        ctx.AddControl(handle, ctx.DistanceToScreenPoint(new Float2(400, 300), 8f));
        ctx.AddDefaultControl(pick);

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.False(ctx.IsNearest(handle));
        Assert.True(ctx.IsNearest(pick));
    }

    [Fact]
    public void PointInsideGrabRadius_BeatsTheDefaultControl()
    {
        var (ctx, cam) = MakeContext();

        Float2 mouse = new(400, 300);
        ctx.BeginFrame(cam, Viewport, mouse, true);
        ControlID handle = ctx.GetControlID("handle");
        ControlID pick = ctx.GetControlID("pick");

        ctx.AddControl(handle, ctx.DistanceToScreenPoint(mouse + new Float2(3, 0), 8f));
        ctx.AddDefaultControl(pick);

        ctx.BeginFrame(cam, Viewport, mouse, true);
        Assert.True(ctx.IsNearest(handle));
    }

    /// <summary>
    /// The context's viewport must round-trip whatever the host laid out. <c>Rect</c> is
    /// min/max-constructed, not x/y/width/height, so building it from a size shifts the projection
    /// centre and draws every handle offset from where it picks.
    /// </summary>
    [Fact]
    public void Viewport_PreservesOriginAndSize()
    {
        var (ctx, cam) = MakeContext();

        Rect offsetViewport = new(new Float2(320, 48), new Float2(320 + 800, 48 + 600));
        ctx.BeginFrame(cam, offsetViewport, Float2.Zero, true);

        Assert.Equal(320f, (float)ctx.Viewport.Min.X);
        Assert.Equal(48f, (float)ctx.Viewport.Min.Y);
        Assert.Equal(800f, (float)ctx.ViewportSize.X);
        Assert.Equal(600f, (float)ctx.ViewportSize.Y);
    }

    /// <summary>Handle distances are measured in the same absolute space the overlay canvas draws
    /// in, so a viewport that is not at the window origin still measures correctly.</summary>
    [Fact]
    public void MousePosition_IsAbsoluteWhenViewportIsOffset()
    {
        var (ctx, cam) = MakeContext();

        Rect offsetViewport = new(new Float2(320, 48), new Float2(320 + 800, 48 + 600));
        ctx.BeginFrame(cam, offsetViewport, new Float2(10, 20), true);

        Assert.Equal(new Float2(10, 20), ctx.MouseLocal);
        Assert.Equal(new Float2(330, 68), ctx.MousePosition);
    }

    /// <summary>
    /// Two handles stacked at the same screen point must resolve to the one in front, whichever
    /// order they registered in. Screen distance alone would order them by sub-pixel projection
    /// noise, letting a handle behind another steal the cursor.
    /// </summary>
    [Theory]
    [InlineData(true)]   // near registers first
    [InlineData(false)]  // near registers last
    public void OverlappingControls_ResolveByDepth(bool nearFirst)
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID near = ctx.GetControlID("near");
        ControlID far = ctx.GetControlID("far");

        // Both within the tie band of each other; the far one is even marginally closer on screen.
        if (nearFirst)
        {
            ctx.AddControl(near, 2f, depth: 5f);
            ctx.AddControl(far, 1f, depth: 50f);
        }
        else
        {
            ctx.AddControl(far, 1f, depth: 50f);
            ctx.AddControl(near, 2f, depth: 5f);
        }

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(near));
        Assert.False(ctx.IsNearest(far));
    }

    /// <summary>Depth only settles overlaps. A handle clearly nearer the cursor still wins, however
    /// far behind it sits - otherwise the frontmost handle in the scene would capture everything.</summary>
    [Fact]
    public void ClearlyNearerOnScreen_BeatsNearerDepth()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID onCursor = ctx.GetControlID("on_cursor");
        ControlID inFront = ctx.GetControlID("in_front");

        ctx.AddControl(inFront, 30f, depth: 1f);
        ctx.AddControl(onCursor, 1f, depth: 900f);

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(onCursor));
    }

    /// <summary>A control that reports no depth loses overlap ties to one that does, so forgetting
    /// the depth cannot let a handle silently outrank everything.</summary>
    [Fact]
    public void UnknownDepth_LosesOverlapToKnownDepth()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID known = ctx.GetControlID("known");
        ControlID unknown = ctx.GetControlID("unknown");

        ctx.AddControl(known, 2f, depth: 12f);
        ctx.AddControl(unknown, 2f);

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(known));
    }

    /// <summary>A screen-space overlay registers depth 0, so it stays on top of scene handles that
    /// happen to project underneath it.</summary>
    [Fact]
    public void ScreenOverlayDepth_WinsOverSceneHandles()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID overlay = ctx.GetControlID("overlay");
        ControlID sceneHandle = ctx.GetControlID("scene");

        ctx.AddControl(overlay, 2f, depth: 0f);
        ctx.AddControl(sceneHandle, 0f, depth: 4f);

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(overlay));
    }

    [Fact]
    public void ControlIDs_AreStableAcrossFrames()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID a = ctx.GetControlID("handle");
        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID b = ctx.GetControlID("handle");

        Assert.Equal(a, b);
    }
}
