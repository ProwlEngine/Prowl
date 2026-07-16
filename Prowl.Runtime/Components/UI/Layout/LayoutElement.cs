// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// Reports desired sizes to a parent <see cref="LayoutGroup"/> / <see cref="ContentSizeFitter"/>.
/// A value of <c>-1</c> means "not specified" - the layout system falls back to the next source
/// (min, then the element's intrinsic size).
/// </summary>
public interface ILayoutElement
{
    float MinWidth { get; }
    float PreferredWidth { get; }
    /// <summary>Relative weight for distributing leftover space along an axis. <c>&lt;= 0</c> = none.</summary>
    float FlexibleWidth { get; }

    float MinHeight { get; }
    float PreferredHeight { get; }
    float FlexibleHeight { get; }
}

/// <summary>
/// Overrides the sizes an element reports to its parent layout group, and can opt the element out of
/// layout entirely. Attach next to a graphic to give it an explicit min/preferred/flexible size.
/// </summary>
[AddComponentMenu("UI/Layout/Layout Element")]
[ComponentIcon("")] // Table
public class LayoutElement : UIBehaviour, ILayoutElement
{
    [SerializeField] private bool _ignoreLayout;
    /// <summary>When true, a parent layout group leaves this element where its RectTransform puts it.</summary>
    public bool IgnoreLayout { get => _ignoreLayout; set => SetField(ref _ignoreLayout, value, UIDirtyFlags.Layout); }

    [SerializeField] private float _minWidth = -1f;
    [SerializeField] private float _minHeight = -1f;
    [SerializeField] private float _preferredWidth = -1f;
    [SerializeField] private float _preferredHeight = -1f;
    [SerializeField] private float _flexibleWidth = -1f;
    [SerializeField] private float _flexibleHeight = -1f;

    public float MinWidth       { get => _minWidth;       set => SetField(ref _minWidth, value, UIDirtyFlags.Layout); }
    public float MinHeight      { get => _minHeight;      set => SetField(ref _minHeight, value, UIDirtyFlags.Layout); }
    public float PreferredWidth { get => _preferredWidth; set => SetField(ref _preferredWidth, value, UIDirtyFlags.Layout); }
    public float PreferredHeight{ get => _preferredHeight;set => SetField(ref _preferredHeight, value, UIDirtyFlags.Layout); }
    public float FlexibleWidth  { get => _flexibleWidth;  set => SetField(ref _flexibleWidth, value, UIDirtyFlags.Layout); }
    public float FlexibleHeight { get => _flexibleHeight; set => SetField(ref _flexibleHeight, value, UIDirtyFlags.Layout); }

    public override void GenerateMesh(UIMeshBuilder builder, in UIContext context) { /* no geometry */ }
}

/// <summary>
/// Resolves the sizes the layout system arranges against: <see cref="ILayoutElement"/> components take
/// priority (a nested <see cref="LayoutGroup"/> reports its content size this way), otherwise the
/// element's intrinsic size (its <see cref="RectTransform.SizeDelta"/>).
/// </summary>
public static class LayoutUtility
{
    public static Float2 GetPreferredSize(GameObject go)
    {
        float pw = -1f, ph = -1f, mw = -1f, mh = -1f;
        foreach (MonoBehaviour c in go.GetComponents<MonoBehaviour>())
        {
            if (c is not ILayoutElement le || !c.EnabledInHierarchy) continue;
            pw = Maths.Max(pw, le.PreferredWidth);
            ph = Maths.Max(ph, le.PreferredHeight);
            mw = Maths.Max(mw, le.MinWidth);
            mh = Maths.Max(mh, le.MinHeight);
        }

        Float2 intrinsic = Intrinsic(go);
        float w = pw >= 0f ? pw : (mw >= 0f ? mw : intrinsic.X);
        float h = ph >= 0f ? ph : (mh >= 0f ? mh : intrinsic.Y);
        if (mw >= 0f) w = Maths.Max(w, mw);
        if (mh >= 0f) h = Maths.Max(h, mh);
        return new Float2(w, h);
    }

    public static Float2 GetMinSize(GameObject go)
    {
        float mw = -1f, mh = -1f;
        foreach (MonoBehaviour c in go.GetComponents<MonoBehaviour>())
        {
            if (c is not ILayoutElement le || !c.EnabledInHierarchy) continue;
            mw = Maths.Max(mw, le.MinWidth);
            mh = Maths.Max(mh, le.MinHeight);
        }
        Float2 intrinsic = Intrinsic(go);
        return new Float2(mw >= 0f ? mw : intrinsic.X, mh >= 0f ? mh : intrinsic.Y);
    }

    /// <summary>Per-axis flexible weight (0 when none set).</summary>
    public static Float2 GetFlexible(GameObject go)
    {
        float fw = 0f, fh = 0f;
        foreach (MonoBehaviour c in go.GetComponents<MonoBehaviour>())
        {
            if (c is not ILayoutElement le || !c.EnabledInHierarchy) continue;
            if (le.FlexibleWidth  > fw) fw = le.FlexibleWidth;
            if (le.FlexibleHeight > fh) fh = le.FlexibleHeight;
        }
        return new Float2(fw, fh);
    }

    private static Float2 Intrinsic(GameObject go)
        => go.RectTransform is { } rt ? rt.SizeDelta : new Float2(100f, 100f);
}
