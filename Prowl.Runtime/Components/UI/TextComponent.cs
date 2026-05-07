// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.PaperUI;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Scribe;
using Prowl.Vector;

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

    private WorldCanvas _canvas;

    public override void OnEnable()
    {
        base.OnEnable();
        //_canvas ??= GetComponentInParent<WorldCanvas>();
        //_canvas.OnRenderUI += RenderGUI;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        //_canvas ??= GetComponentInParent<WorldCanvas>();
        //_canvas.OnRenderUI -= RenderGUI;
    }

    public override void BuildUI(Paper paper, UIContext ctx){}

    public override void OnGui(Paper paper)
    {
        RenderGUI(paper);
    }

    public void RenderGUI(Paper paper)
    {
        if (paper == null) return;
        if (Font == null) return;

        RectTransform? rt = GameObject.RectTransform;
        if (rt == null) return;

        Rect rect = rt.ComputedRect;
        float w = rect.Size.X;
        float h = rect.Size.Y;
        if (w <= 0 || h <= 0) return;

        paper.Box($"txt_{InstanceID}")
            .Height(Size)
            .PositionType(PositionType.SelfDirected)
            .Left(rect.Min.X)
            .Top(rect.Min.Y)
            .Width(w)
            .Height(h)
            .Text(Text, Font.GetScribeFont())
            .TextColor(TextColor)
            .Alignment(Alignment)
            .FontSize(Size);

        //Debug.Log($"Drawing text...!");
    }
}
