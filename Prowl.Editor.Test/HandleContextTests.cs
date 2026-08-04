// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Guards the arbitration rules in <see cref="HandleContext"/> that decide which of the viewport's
/// handles owns the cursor. Every case here is one that produced a real, user-visible failure:
/// picking silently dying, or a handle behind another one stealing the click.
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

    /// <summary>
    /// A handle's grab radius is a region, not a bias. Handles report unconditionally every frame,
    /// so one the cursor is nowhere near must not be a candidate at all - otherwise every registered
    /// handle outranks the viewport's default control from anywhere on screen and object picking
    /// stops working entirely whenever anything with handles is selected.
    /// </summary>
    [Fact]
    public void HandleOutsideItsGrabRadius_DoesNotStarveObjectPicking()
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID handle = ctx.GetControlID("handle");
        ControlID pick = ctx.GetControlID("pick");

        // Cursor at the origin, handle 500px away with an 8px grab radius.
        ctx.AddControl(handle, ctx.DistanceToScreenPoint(new Float2(400, 300), 8f));
        ctx.AddDefaultControl(pick);

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(pick));

        // ...and it still wins when the cursor is genuinely on it.
        ctx.AddControl(handle, ctx.DistanceToScreenPoint(new Float2(3, 0), 8f));
        ctx.AddDefaultControl(pick);
        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(handle));
    }

    /// <summary>
    /// The same starvation via the other door: a wrapper that reports "not hovered" as
    /// <see cref="float.MaxValue"/> must be rejected, since the accumulator itself starts at
    /// MaxValue and would otherwise accept it.
    /// </summary>
    [Theory]
    [InlineData(float.MaxValue)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NaN)]
    public void NonCandidateDistance_DoesNotStarveObjectPicking(float distance)
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID absent = ctx.GetControlID("absent");
        ControlID pick = ctx.GetControlID("pick");
        ctx.AddControl(absent, distance);
        ctx.AddDefaultControl(pick);

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        Assert.True(ctx.IsNearest(pick));
    }

    /// <summary>
    /// Two handles stacked at the same screen point resolve to the one in front, whichever order
    /// they registered in. Screen distance alone orders them by sub-pixel projection noise, which is
    /// what let a handle behind another take the click about half the time.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverlappingHandles_ResolveToTheOneInFront(bool nearRegistersFirst)
    {
        var (ctx, cam) = MakeContext();

        ctx.BeginFrame(cam, Viewport, Float2.Zero, true);
        ControlID near = ctx.GetControlID("near");
        ControlID far = ctx.GetControlID("far");

        // Overlapping, and the far one is even marginally closer on screen.
        if (nearRegistersFirst)
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
    }

    /// <summary>
    /// Depth only settles overlaps. A handle clearly nearer the cursor still wins however far behind
    /// it sits, or the frontmost handle in the scene would capture the whole viewport.
    /// </summary>
    [Fact]
    public void DepthDoesNotOverrideAClearScreenDistanceWin()
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
}
