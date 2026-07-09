// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.UI;

/// <summary>
/// Base for components that arrange their children's rects automatically. A group reports its own
/// content size (via <see cref="ILayoutElement"/>) so it can nest inside another group, and
/// <see cref="Arrange"/> writes each child's <see cref="RectTransform.ComputedRect"/> during the
/// canvas rebuild. Children marked <see cref="LayoutElement.IgnoreLayout"/> are skipped.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public abstract class LayoutGroup : UIBehaviour, ILayoutElement
{
    [SerializeField] protected int _paddingLeft;
    [SerializeField] protected int _paddingRight;
    [SerializeField] protected int _paddingTop;
    [SerializeField] protected int _paddingBottom;
    [SerializeField] protected TextAlignment _childAlignment = TextAlignment.TopLeft;

    public int PaddingLeft   { get => _paddingLeft;   set => SetField(ref _paddingLeft, value, UIDirtyFlags.Layout); }
    public int PaddingRight  { get => _paddingRight;  set => SetField(ref _paddingRight, value, UIDirtyFlags.Layout); }
    public int PaddingTop    { get => _paddingTop;    set => SetField(ref _paddingTop, value, UIDirtyFlags.Layout); }
    public int PaddingBottom { get => _paddingBottom; set => SetField(ref _paddingBottom, value, UIDirtyFlags.Layout); }
    public TextAlignment ChildAlignment { get => _childAlignment; set => SetField(ref _childAlignment, value, UIDirtyFlags.Layout); }

    public override void GenerateMesh(UIMeshBuilder builder, in UIContext context) { /* no geometry */ }

    /// <summary>Positions every laid-out child inside <paramref name="rect"/> (canvas design pixels).</summary>
    public abstract void Arrange(Rect rect);

    // ---- ILayoutElement (content size, reported to a parent group / fitter) ----
    public abstract float MinWidth { get; }
    public abstract float PreferredWidth { get; }
    public virtual float FlexibleWidth => -1f;
    public abstract float MinHeight { get; }
    public abstract float PreferredHeight { get; }
    public virtual float FlexibleHeight => -1f;

    protected List<GameObject> GetLayoutChildren()
    {
        var list = new List<GameObject>();
        foreach (GameObject child in GameObject.Children)
        {
            if (!child.EnabledInHierarchy || child.RectTransform is null) continue;
            LayoutElement? le = child.GetComponent<LayoutElement>();
            if (le is { IgnoreLayout: true }) continue;
            list.Add(child);
        }
        return list;
    }

    protected static void SetChildRect(GameObject child, Rect rect)
    {
        if (child.RectTransform is { } rt) rt.ComputedRect = rect;
    }

    // Alignment weights: horizontal 0=Left, .5=Middle, 1=Right; vertical (+Y up) 0=Bottom, .5=Center, 1=Top.
    protected float HorizontalFactor()
        => (_childAlignment & TextAlignment.Right) != 0 ? 1f : (_childAlignment & TextAlignment.Middle) != 0 ? 0.5f : 0f;
    protected float VerticalFactor()
        => (_childAlignment & TextAlignment.Top) != 0 ? 1f : (_childAlignment & TextAlignment.Center) != 0 ? 0.5f : 0f;

    protected static float AlignStart(float min, float available, float size, float factor)
        => min + Maths.Max(0f, available - size) * factor;
}

/// <summary>Shared row/column arranger. Places children end-to-end along one axis with spacing,
/// grows flexible children into leftover space, and shrinks toward min sizes when short.</summary>
public abstract class HorizontalOrVerticalLayoutGroup : LayoutGroup
{
    protected abstract bool IsVertical { get; }

    [SerializeField] protected float _spacing;
    [SerializeField] protected bool _childControlWidth = true;
    [SerializeField] protected bool _childControlHeight = true;
    [SerializeField] protected bool _childForceExpandWidth;
    [SerializeField] protected bool _childForceExpandHeight;

    public float Spacing { get => _spacing; set => SetField(ref _spacing, value, UIDirtyFlags.Layout); }
    public bool ChildControlWidth  { get => _childControlWidth;  set => SetField(ref _childControlWidth, value, UIDirtyFlags.Layout); }
    public bool ChildControlHeight { get => _childControlHeight; set => SetField(ref _childControlHeight, value, UIDirtyFlags.Layout); }
    public bool ChildForceExpandWidth  { get => _childForceExpandWidth;  set => SetField(ref _childForceExpandWidth, value, UIDirtyFlags.Layout); }
    public bool ChildForceExpandHeight { get => _childForceExpandHeight; set => SetField(ref _childForceExpandHeight, value, UIDirtyFlags.Layout); }

    private float PaddingAlong => IsVertical ? _paddingTop + _paddingBottom : _paddingLeft + _paddingRight;
    private float PaddingCross => IsVertical ? _paddingLeft + _paddingRight : _paddingTop + _paddingBottom;

    private float AlongOf(Float2 s) => IsVertical ? s.Y : s.X;
    private float CrossOf(Float2 s) => IsVertical ? s.X : s.Y;

    public override float PreferredWidth => IsVertical ? CrossPreferred() : AlongPreferred();
    public override float PreferredHeight => IsVertical ? AlongPreferred() : CrossPreferred();
    public override float MinWidth => IsVertical ? CrossMin() : AlongMin();
    public override float MinHeight => IsVertical ? AlongMin() : CrossMin();

    private float AlongPreferred()
    {
        var kids = GetLayoutChildren();
        float sum = _spacing * Maths.Max(0, kids.Count - 1) + PaddingAlong;
        foreach (GameObject k in kids) sum += AlongOf(LayoutUtility.GetPreferredSize(k));
        return sum;
    }
    private float AlongMin()
    {
        var kids = GetLayoutChildren();
        float sum = _spacing * Maths.Max(0, kids.Count - 1) + PaddingAlong;
        foreach (GameObject k in kids) sum += AlongOf(LayoutUtility.GetMinSize(k));
        return sum;
    }
    private float CrossPreferred()
    {
        var kids = GetLayoutChildren();
        float max = 0f;
        foreach (GameObject k in kids) max = Maths.Max(max, CrossOf(LayoutUtility.GetPreferredSize(k)));
        return max + PaddingCross;
    }
    private float CrossMin()
    {
        var kids = GetLayoutChildren();
        float max = 0f;
        foreach (GameObject k in kids) max = Maths.Max(max, CrossOf(LayoutUtility.GetMinSize(k)));
        return max + PaddingCross;
    }

    public override void Arrange(Rect rect)
    {
        List<GameObject> kids = GetLayoutChildren();
        int n = kids.Count;
        if (n == 0) return;

        Rect content = new Rect(
            rect.Min.X + _paddingLeft, rect.Min.Y + _paddingBottom,
            rect.Max.X - _paddingRight, rect.Max.Y - _paddingTop);
        float contentW = Maths.Max(0f, content.Size.X);
        float contentH = Maths.Max(0f, content.Size.Y);
        float mainSize = IsVertical ? contentH : contentW;
        float crossSize = IsVertical ? contentW : contentH;

        bool forceExpandMain = IsVertical ? _childForceExpandHeight : _childForceExpandWidth;
        bool controlCross = IsVertical ? _childControlWidth : _childControlHeight;

        float[] pref = new float[n], min = new float[n], flex = new float[n], size = new float[n];
        float totalPref = _spacing * (n - 1), totalFlex = 0f, totalShrink = 0f;
        for (int i = 0; i < n; i++)
        {
            pref[i] = AlongOf(LayoutUtility.GetPreferredSize(kids[i]));
            min[i]  = AlongOf(LayoutUtility.GetMinSize(kids[i]));
            float f = AlongOf(LayoutUtility.GetFlexible(kids[i]));
            if (f <= 0f && forceExpandMain) f = 1f;
            flex[i] = f;
            totalPref += pref[i];
            totalFlex += f;
            totalShrink += Maths.Max(0f, pref[i] - min[i]);
        }

        float extra = mainSize - totalPref;
        for (int i = 0; i < n; i++)
        {
            if (extra > 0f && totalFlex > 0f)
                size[i] = pref[i] + extra * (flex[i] / totalFlex);
            else if (extra < 0f && totalShrink > 0f)
                size[i] = Maths.Max(min[i], pref[i] + extra * (Maths.Max(0f, pref[i] - min[i]) / totalShrink));
            else
                size[i] = pref[i];
        }

        // Vertical fills top-to-bottom (+Y up: start at content top and walk down); horizontal left-to-right.
        float pos = IsVertical ? content.Max.Y : content.Min.X;
        for (int i = 0; i < n; i++)
        {
            float cross = controlCross ? crossSize : Maths.Min(CrossOf(LayoutUtility.GetPreferredSize(kids[i])), crossSize);
            Rect r;
            if (IsVertical)
            {
                float x = AlignStart(content.Min.X, contentW, cross, HorizontalFactor());
                r = new Rect(x, pos - size[i], x + cross, pos);
                pos -= size[i] + _spacing;
            }
            else
            {
                float y = AlignStart(content.Min.Y, contentH, cross, VerticalFactor());
                r = new Rect(pos, y, pos + size[i], y + cross);
                pos += size[i] + _spacing;
            }
            SetChildRect(kids[i], r);
        }
    }
}

/// <summary>Arranges children in a horizontal row.</summary>
[AddComponentMenu("UI/Layout/Horizontal Layout Group")]
[ComponentIcon("")] // Bars (rotated)
public sealed class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup
{
    protected override bool IsVertical => false;
}

/// <summary>Arranges children in a vertical column.</summary>
[AddComponentMenu("UI/Layout/Vertical Layout Group")]
[ComponentIcon("")] // Bars
public sealed class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup
{
    protected override bool IsVertical => true;
}
