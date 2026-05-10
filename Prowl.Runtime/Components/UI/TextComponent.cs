// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime;

/// <summary>
/// </summary>
[AddComponentMenu("UI/Text")]
[ComponentIcon("\u0054")] // Text
public class TextComponent : UIBehaviour
{
    public FontAsset Font;
    public Color TextColor;

    public string Text;

    public Prowl.PaperUI.TextAlignment Alignment;

    public int Size = 20;

    public override bool RequiresPerElementMaterial => true;

    public override void GenerateMesh(UIMeshBuilder builder, in UIContext context)
    {
        // Phase 4.A — placeholder. Until Scribe atlas integration lands, draw a
        // tinted rect so layout/dirty paths can be exercised against TextComponent.
        if (string.IsNullOrEmpty(Text)) return;
        var rt = GameObject.RectTransform;
        if (rt is null) return;
        Rect r = rt.ComputedRect;
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;

        // Element-local pivot-centered rect (see GameCanvas.BuildItemModel for the convention).
        float w = r.Size.X;
        float h = r.Size.Y;
        Float2 pivot = rt.Pivot;
        Rect local = new Rect(
            -pivot.X * w,
            -pivot.Y * h,
            (1f - pivot.X) * w,
            (1f - pivot.Y) * h);

        Color tinted = TextColor * new Color(1, 1, 1, context.Alpha);
        builder.AddQuad(local, tinted, Float2.Zero, Float2.One);
    }

    public override void PopulateProperties(PropertyState p, in UIContext _)
        => p.SetTexture("_MainTex", Texture2D.LoadDefault(DefaultTexture.White));
}
