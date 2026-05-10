// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

namespace Prowl.Runtime.UI;

/// <summary>
/// Controls the alpha, interactivity, and raycast blocking of all child UI elements.
/// Analogous to Unity's <c>CanvasGroup</c>.
/// </summary>
/// <remarks>
/// Attach a <see cref="CanvasGroup"/> to any GameObject in the UI hierarchy.
/// The <see cref="GameCanvas"/> will apply this group's settings to the
/// <see cref="UIContext"/> before building child elements.
/// </remarks>
public class CanvasGroup : UIBehaviour
{
    /// <summary>Opacity multiplier applied to all child elements (0 = transparent, 1 = opaque).</summary>
    [SerializeField] private float _alpha = 1f;
    public float Alpha
    {
        get => _alpha;
        set
        {
            if (_alpha == value) return;
            _alpha = value;
            // Alpha is baked into vertex color during GenerateMesh, so every descendant
            // UIBehaviour needs its mesh re-baked. MarkDirty on each descendant also
            // bubbles a Vertices flag up to the canvas via UIBehaviour.MarkDirty.
            MarkDescendantsDirty(UIDirtyFlags.Vertices);
            MarkDirty(UIDirtyFlags.Vertices);
        }
    }

    /// <summary>
    /// Walks the GameObject subtree rooted at this component and marks every
    /// <see cref="UIBehaviour"/> with the given flags. Stops at any nested
    /// <see cref="GameCanvas"/> or <see cref="CanvasGroup"/> with
    /// <see cref="IgnoreParentGroups"/> set, which form their own context.
    /// </summary>
    private void MarkDescendantsDirty(UIDirtyFlags flags)
    {
        WalkAndDirty(GameObject, flags, isRoot: true);

        static void WalkAndDirty(GameObject go, UIDirtyFlags f, bool isRoot)
        {
            if (!isRoot)
            {
                // Don't descend into a nested canvas — it owns its own dirty state.
                if (go.GetComponent<GameCanvas>() != null) return;
                // A nested CanvasGroup that ignores parents starts a fresh context.
                CanvasGroup? nested = go.GetComponent<CanvasGroup>();
                if (nested is { IgnoreParentGroups: true }) return;

                foreach (UIBehaviour ui in go.GetComponents<UIBehaviour>())
                    ui.MarkDirty(f);
            }

            foreach (GameObject child in go.Children)
                WalkAndDirty(child, f, isRoot: false);
        }
    }

    /// <summary>When <c>false</c>, disables pointer interaction for all descendants.</summary>
    [SerializeField] private bool _interactable = true;
    public bool Interactable
    {
        get => _interactable;
        set { if (_interactable == value) return; _interactable = value; MarkDirty(UIDirtyFlags.Hierarchy); }
    }

    /// <summary>When <c>false</c>, raycasts pass through all descendant elements.</summary>
    [SerializeField] private bool _blocksRaycasts = true;
    public bool BlocksRaycasts
    {
        get => _blocksRaycasts;
        set { if (_blocksRaycasts == value) return; _blocksRaycasts = value; MarkDirty(UIDirtyFlags.Hierarchy); }
    }

    /// <summary>When <c>true</c>, this group and its children ignore parent <see cref="CanvasGroup"/> settings.</summary>
    [SerializeField] private bool _ignoreParentGroups = false;
    public bool IgnoreParentGroups
    {
        get => _ignoreParentGroups;
        set
        {
            if (_ignoreParentGroups == value) return;
            _ignoreParentGroups = value;
            // Toggling this changes every descendant's effective alpha — re-bake them.
            MarkDescendantsDirty(UIDirtyFlags.Vertices);
            MarkDirty(UIDirtyFlags.Vertices);
        }
    }

    /// <summary>Applies this group's settings to the given context.</summary>
    internal UIContext ApplyTo(UIContext parent)
    {
        UIContext ctx = IgnoreParentGroups ? UIContext.Default : parent;
        ctx.Alpha *= Alpha;
        ctx.Interactable = ctx.Interactable && Interactable;
        ctx.BlocksRaycasts = ctx.BlocksRaycasts && BlocksRaycasts;
        return ctx;
    }

    /// <summary>CanvasGroup itself does not emit visual elements.</summary>
    public override void GenerateMesh(UIMeshBuilder _, in UIContext __) { /* no geometry */ }
}
