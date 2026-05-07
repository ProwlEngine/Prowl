// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Runtime.UI;

namespace Prowl.Runtime;

/// <summary>
/// Render mode for a <see cref="GameCanvas"/>.
/// </summary>
public enum RenderMode
{
    /// <summary>
    /// The GameCanvas is rendered as an overlay on top of the entire screen.
    /// UI elements are sized in screen-space pixels.
    /// </summary>
    ScreenSpaceOverlay,

    /// <summary>
    /// The GameCanvas is rendered as a screen-space overlay on a specific camera's
    /// output. Currently behaves identically to <see cref="ScreenSpaceOverlay"/>.
    /// </summary>
    ScreenSpaceCamera,

    /// <summary>
    /// The GameCanvas lives in world space. Use <see cref="WorldCanvas"/> for this mode.
    /// </summary>
    WorldSpace,
}

/// <summary>
/// GameCanvas component that handles drawing UI components to the full screen.
/// </summary>
[AddComponentMenu("UI/Game Canvas")]
[ComponentIcon("\uf03e")] // Image
public class GameCanvas : MonoBehaviour
{
     /// <summary>
    /// When set, GameCanvas uses this size instead of the window framebuffer size.
    /// Used by the editor to render into off-screen render textures.
    /// </summary>
    public static Float2? ScreenSizeOverride { get; set; }

    /// <summary>
    /// The rendering mode of this GameCanvas.
    /// </summary>
    public RenderMode RenderMode = RenderMode.ScreenSpaceOverlay;

    /// <summary>
    /// Global scale factor applied to all elements in the GameCanvas.
    /// </summary>
    public float ScaleFactor = 1f;

    /// <summary>
    /// Sort order relative to other screen-space canvases.
    /// Higher values render on top.
    /// </summary>
    public int SortOrder;

    /// <summary>
    /// Ensures this GameCanvas's own GameObject and all child GameObjects
    /// have a <see cref="RectTransform"/> instead of a plain Transform.
    /// </summary>
    public override void OnAddedToScene()
    {
        GameObject.EnsureRectTransform();
        EnsureChildRectTransforms(GameObject);
    }

    /// <summary>
    /// Called every frame by the Scene's OnGui pipeline.
    /// Builds the full Paper UI tree from the child hierarchy.
    /// </summary>
    public override void OnGui(Paper paper)
    {
        // WorldSpace canvases are handled by WorldCanvas / IRenderable.
        if (RenderMode == RenderMode.WorldSpace)
            return;

        // Determine screen-space root rect.
        // When rendering into an off-screen RT (e.g. editor game view), the
        // override provides the correct dimensions instead of the window size.
        float rawW = ScreenSizeOverride?.X ?? Window.InternalWindow.FramebufferSize.X;
        float rawH = ScreenSizeOverride?.Y ?? Window.InternalWindow.FramebufferSize.Y;
        float screenW = rawW / ScaleFactor;
        float screenH = rawH / ScaleFactor;
        Rect rootRect = new(0, 0, screenW, screenH);

        // Compute layout for the root RectTransform
        RectTransform? rootRt = GameObject.RectTransform;
        if (rootRt != null)
        {
            rootRt.AnchorMin = Float2.Zero;
            rootRt.AnchorMax = Float2.One;
            rootRt.SizeDelta = Float2.Zero;
            rootRt.AnchoredPosition = Float2.Zero;
            rootRt.ComputedRect = rootRect;
        }

        UIContext context = UIContext.Default;

        // Create a root Paper container that covers the full screen
        var root = paper.Box($"canvas_{InstanceID}")
            .PositionType(PositionType.SelfDirected)
            .Left(0)
            .Top(0)
            .Width(screenW)
            .Height(screenH);

        using (root.Enter())
        {
            // Traverse children depth-first
            BuildChildren(paper, GameObject, rootRect, context);
        }
    }

    /// <summary>
    /// Recursively traverses child GameObjects, computes RectTransform layout,
    /// applies CanvasGroup contexts, and invokes BuildUI on UIBehaviours.
    /// </summary>
    private void BuildChildren(Paper paper, GameObject parent, Rect parentRect, UIContext context)
    {
        foreach (GameObject child in parent.Children)
        {
            if (!child.EnabledInHierarchy)
                continue;

            // Nested GameCanvas — skip; it will handle itself
            GameCanvas? nestedCanvas = child.GetComponent<GameCanvas>();
            if (nestedCanvas != null && nestedCanvas != this)
                continue;

            // Apply CanvasGroup if present
            UIContext childContext = context;
            CanvasGroup? group = child.GetComponent<CanvasGroup>();
            if (group != null && group.EnabledInHierarchy)
                childContext = group.ApplyTo(context);

            // Compute layout if the child has a RectTransform
            Rect childRect = parentRect;
            RectTransform? rt = child.RectTransform;
            if (rt != null)
                childRect = rt.ComputeRect(parentRect);

            // Build visual UI for all UIBehaviours on this object
            foreach (UIBehaviour uiBehaviour in child.GetComponents<UIBehaviour>())
            {
                if (uiBehaviour.EnabledInHierarchy)
                    uiBehaviour.BuildUI(paper, childContext);
            }

            // Recurse into grandchildren
            BuildChildren(paper, child, childRect, childContext);
        }
    }

    /// <summary>
    /// Recursively ensures all child GameObjects under a GameCanvas use RectTransform.
    /// </summary>
    private static void EnsureChildRectTransforms(GameObject parent)
    {
        foreach (GameObject child in parent.Children)
        {
            child.EnsureRectTransform();
            EnsureChildRectTransforms(child);
        }
    }
}

