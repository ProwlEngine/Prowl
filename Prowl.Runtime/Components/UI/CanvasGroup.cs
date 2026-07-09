// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

namespace Prowl.Runtime.UI;

public class CanvasGroup : UIBehaviour
{
    /// <summary>Opacity multiplier applied to all child elements (0 = transparent, 1 = opaque).</summary>
    [SerializeField] private float _alpha = 1f;
    public float Alpha
    {
        get => _alpha;
        set
        {
            // SetField marks this component + the canvas dirty when the value changes.
            // Alpha is also baked into every descendant's vertex color during GenerateMesh,
            // so on a real change we additionally re-dirty the whole subtree.
            if (SetField(ref _alpha, value, UIDirtyFlags.Vertices))
                MarkDescendantsDirty(UIDirtyFlags.Vertices);
        }
    }

    private void MarkDescendantsDirty(UIDirtyFlags flags)
    {
        WalkAndDirty(GameObject, flags, isRoot: true);

        static void WalkAndDirty(GameObject go, UIDirtyFlags f, bool isRoot)
        {
            if (!isRoot)
            {
                // Don't descend into a nested canvas - it owns its own dirty state.
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
        set
        {
            // Descendant Selectables read this through Selectable.IsInteractable(), but nothing
            // notifies them when it flips - so push a visual refresh across the subtree so they
            // grey out (or come back) immediately.
            if (SetField(ref _interactable, value, UIDirtyFlags.Hierarchy))
                RefreshDescendantSelectables();
        }
    }

    private void RefreshDescendantSelectables()
    {
        Walk(GameObject, isRoot: true);

        static void Walk(GameObject go, bool isRoot)
        {
            if (!isRoot)
            {
                if (go.GetComponent<GameCanvas>() != null) return;
                CanvasGroup? nested = go.GetComponent<CanvasGroup>();
                if (nested is { IgnoreParentGroups: true }) return;
            }

            foreach (Selectable s in go.GetComponents<Selectable>())
                s.RefreshInteractable();

            foreach (GameObject child in go.Children)
                Walk(child, isRoot: false);
        }
    }

    /// <summary>When <c>false</c>, raycasts pass through all descendant elements.</summary>
    [SerializeField] private bool _blocksRaycasts = true;
    public bool BlocksRaycasts
    {
        get => _blocksRaycasts;
        set => SetField(ref _blocksRaycasts, value, UIDirtyFlags.Hierarchy);
    }

    /// <summary>When <c>true</c>, this group and its children ignore parent <see cref="CanvasGroup"/> settings.</summary>
    [SerializeField] private bool _ignoreParentGroups = false;
    public bool IgnoreParentGroups
    {
        get => _ignoreParentGroups;
        set
        {
            // Toggling this changes every descendant's effective alpha - re-bake them.
            if (SetField(ref _ignoreParentGroups, value, UIDirtyFlags.Vertices))
                MarkDescendantsDirty(UIDirtyFlags.Vertices);
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
