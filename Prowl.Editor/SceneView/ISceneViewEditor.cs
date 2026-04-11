// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor;

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
    /// Draw custom toolbar buttons in the scene view.
    /// Return true to suppress the default transform gizmo toolbar.
    /// </summary>
    bool DrawToolbar(Paper paper, string id, Prowl.Scribe.FontFile font);

    /// <summary>
    /// Handle scene input (mouse, keyboard). Called each frame when this editor is active.
    /// Return true to consume the input (suppresses object picking and transform gizmo).
    /// </summary>
    /// <param name="camera">The editor camera</param>
    /// <param name="scene">The active scene</param>
    /// <param name="mouseRay">Ray from mouse position into the scene</param>
    /// <param name="mousePos">Mouse position in viewport-local pixels</param>
    /// <param name="viewportHovered">Whether the mouse is over the viewport</param>
    bool OnSceneInput(Camera camera, Scene scene, Ray mouseRay, Float2 mousePos, bool viewportHovered);

    /// <summary>
    /// Draw 2D overlays in the scene viewport foreground (e.g. brush indicators, handles).
    /// </summary>
    void DrawOverlay(Prowl.Quill.Canvas canvas, Rect viewport) { }

    /// <summary>
    /// Called when this editor is activated (component selected).
    /// </summary>
    void OnActivate(GameObject target) { }

    /// <summary>
    /// Called when this editor is deactivated (selection changed away).
    /// </summary>
    void OnDeactivate() { }
}
