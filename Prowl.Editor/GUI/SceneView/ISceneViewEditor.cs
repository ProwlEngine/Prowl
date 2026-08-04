// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Interface for custom scene view editors that extend the scene viewport with
/// custom toolbars, input handling, and overlays.
/// Implementations are discovered via [SceneViewEditorFor] attribute and activated
/// when a GameObject with the matching component is selected.
/// Examples: Terrain editor, CSG editor, Spline editor.
/// </summary>
public interface ISceneViewEditor
{
    /// <summary>Priority for ordering when multiple editors could apply. Lower = higher priority.</summary>
    int Priority => 0;

    /// <summary>
    /// Hide the object transform gizmo entirely while this editor is active. This is a display
    /// choice, not an arbitration one: use it only when moving the GameObject's Transform is
    /// meaningless for the thing being edited (a RectTransform, say). Editors that simply want their
    /// own handles to win the cursor should leave this alone and let arbitration do the work.
    /// </summary>
    bool SuppressTransformGizmo => false;

    /// <summary>
    /// Draw custom toolbar buttons in the scene view.
    /// Return true to suppress the default transform gizmo toolbar.
    /// </summary>
    bool DrawToolbar(Paper paper, string id, Scribe.FontFile font);

    /// <summary>
    /// Handle scene input (mouse, keyboard). Called each frame when this editor is active.
    /// Register interactive elements with <see cref="HandleContext.AddControl"/> and act on the ones
    /// that win; there is no return value, because consumption is expressed by winning arbitration
    /// rather than by suppressing the whole viewport.
    /// </summary>
    /// <param name="ctx">Handle arbitration context - camera, cursor, ray and viewport all live here.</param>
    /// <param name="scene">The active scene</param>
    void OnSceneInput(HandleContext ctx, Scene scene);

    /// <summary>
    /// Draw 2D overlays in the scene viewport foreground (e.g. brush indicators, handles).
    /// </summary>
    void DrawOverlay(Quill.Canvas canvas, Rect viewport) { }

    /// <summary>
    /// Called when this editor is activated (component selected).
    /// </summary>
    void OnActivate(GameObject target) { }

    /// <summary>
    /// Called when this editor is deactivated (selection changed away).
    /// </summary>
    void OnDeactivate() { }
}
