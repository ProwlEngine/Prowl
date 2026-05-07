// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// Displays a colored rectangle or a <see cref="Texture2D"/> sprite in the UI.
/// Analogous to Unity's <c>Image</c> component.
/// </summary>
/// <remarks>
/// Expects the parent GameObject to have a <see cref="RectTransform"/>.
/// The image fills the rect computed by the <see cref="RectTransform"/>.
/// Alpha from the parent <see cref="CanvasGroup"/> is multiplied into <see cref="Color"/>.
/// </remarks>
public class UIImage : UIBehaviour
{
    /// <summary>
    /// The tint color of the image. Alpha is modulated by CanvasGroup.
    /// </summary>
    public Color Color = Color.White;

    /// <summary>
    /// Optional texture to display. When null, a solid color rectangle is drawn.
    /// </summary>
    public Texture2D? Sprite;

    /// <summary>
    /// Whether the image should preserve the source texture's aspect ratio.
    /// </summary>
    public bool PreserveAspect;

    /// <summary>
    /// Corner radius for rounded rectangles (in pixels). 0 = sharp corners.
    /// </summary>
    public float CornerRadius;

    /// <summary>
    /// Whether this element should block raycasts (pointer hit-testing).
    /// </summary>
    public bool RaycastTarget = true;

    public override void BuildUI(Paper paper, UIContext context)
    {
        RectTransform? rt = GameObject.RectTransform;
        if (rt == null) return;

        Rect rect = rt.ComputedRect;
        float w = rect.Size.X;
        float h = rect.Size.Y;
        if (w <= 0 || h <= 0) return;

        Color tinted = new(
            Color.R,
            Color.G,
            Color.B,
            Color.A * context.Alpha
        );

        var box = paper.Box($"img_{InstanceID}")
            .PositionType(PositionType.SelfDirected)
            .Left(rect.Min.X)
            .Top(rect.Min.Y)
            .Width(w)
            .Height(h)
            .BackgroundColor(tinted);

        if (CornerRadius > 0)
            box = box.Rounded(CornerRadius);

        using (box.Enter())
        {
            // Box content — texture support will use Paper's image API
            // when the backend supports it.
        }
    }

    public override void OnGui(Paper paper)
    {
        RectTransform? rt = GameObject.RectTransform;
        if (rt == null) return;

        Rect rect = rt.ComputedRect;
        float w = rect.Size.X;
        float h = rect.Size.Y;
        if (w <= 0 || h <= 0) return;

        Color tinted = new(
            Color.R,
            Color.G,
            Color.B,
            Color.A
        );

        var box = paper.Box($"img_{InstanceID}")
            .PositionType(PositionType.SelfDirected)
            .Left(rect.Min.X)
            .Top(rect.Min.Y)
            .Width(w)
            .Height(h)
            .Rounded(CornerRadius)
            .BackgroundColor(tinted);

        if (CornerRadius > 0)
            box = box.Rounded(CornerRadius);

        using (box.Enter())
        {
            // Box content — texture support will use Paper's image API
            // when the backend supports it.
        }
    }
}
