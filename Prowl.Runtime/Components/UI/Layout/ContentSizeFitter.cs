// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// Resizes its own <see cref="RectTransform"/> to fit its content (its <see cref="ILayoutElement"/>
/// sources - e.g. a child <see cref="LayoutGroup"/> or a <see cref="LayoutElement"/>). The canvas
/// applies the fit just before the element computes its rect, so the fitted size drives layout.
/// </summary>
[AddComponentMenu("UI/Layout/Content Size Fitter")]
[ComponentIcon("")] // ArrowsToDot
public class ContentSizeFitter : UIBehaviour
{
    public enum FitMode { Unconstrained, MinSize, PreferredSize }

    [SerializeField] private FitMode _horizontalFit = FitMode.Unconstrained;
    [SerializeField] private FitMode _verticalFit = FitMode.Unconstrained;

    public FitMode HorizontalFit { get => _horizontalFit; set => SetField(ref _horizontalFit, value, UIDirtyFlags.Layout); }
    public FitMode VerticalFit { get => _verticalFit; set => SetField(ref _verticalFit, value, UIDirtyFlags.Layout); }

    public override void GenerateMesh(UIMeshBuilder builder, in UIContext context) { /* no geometry */ }

    /// <summary>Writes the fitted size into the RectTransform's <see cref="RectTransform.SizeDelta"/>.
    /// Called by the canvas during rebuild before this element's rect is computed.</summary>
    public void ApplyFit()
    {
        RectTransform? rt = GameObject.RectTransform;
        if (rt is null) return;
        if (_horizontalFit == FitMode.Unconstrained && _verticalFit == FitMode.Unconstrained) return;

        Float2 sd = rt.SizeDelta;

        if (_horizontalFit != FitMode.Unconstrained)
            sd.X = _horizontalFit == FitMode.MinSize
                ? LayoutUtility.GetMinSize(GameObject).X
                : LayoutUtility.GetPreferredSize(GameObject).X;

        if (_verticalFit != FitMode.Unconstrained)
            sd.Y = _verticalFit == FitMode.MinSize
                ? LayoutUtility.GetMinSize(GameObject).Y
                : LayoutUtility.GetPreferredSize(GameObject).Y;

        // Setter is change-guarded, so a stable layout stops dirtying after it settles.
        rt.SizeDelta = sd;
    }
}
