// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.Editor.Theming;
using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// A scene-view mode with its own handles, overlays and toolbar. Tools register controls with the
/// viewport's <see cref="HandleContext"/> and act on whichever ones win the cursor, so several
/// interactive things coexist without any of them claiming the frame.
///
/// <para>Every available tool ticks every frame - there is no exclusive "active tool". A tool that
/// should only act when the user asks for it keeps its own enabled flag, draws a toggle in
/// <see cref="OnToolStripGUI"/>, and returns early from <see cref="OnSceneInput"/> while off.</para>
///
/// <para>Discovered by <see cref="EditorRegistries"/> via <see cref="GlobalSceneToolAttribute"/> or
/// <see cref="ComponentSceneToolAttribute"/>. One instance exists per tool type for the whole
/// editor; anything that varies per viewport belongs in
/// <see cref="SceneToolContext.State{T}"/> rather than in a field.</para>
/// </summary>
public abstract class SceneTool
{
    /// <summary>Display name for the toolbar and tooltips.</summary>
    public virtual string Name => GetType().Name;

    /// <summary>Icon glyph shown in the tool strip.</summary>
    public virtual string Icon => EditorIcons.Wrench;

    public virtual string? Tooltip => null;

    /// <summary>Sort order within the tool strip. Lower comes first.</summary>
    public virtual int Order => 0;

    /// <summary>
    /// Hide the object transform gizmo entirely while this tool is active. A display choice, not an
    /// arbitration one: use it when moving the GameObject's Transform is meaningless for whatever is
    /// being edited. Tools that just want their handles to win the cursor should leave this alone.
    /// </summary>
    public virtual bool SuppressTransformGizmo => false;

    /// <summary>Replace the viewport's default transform tool strip with <see cref="OnToolStripGUI"/>.
    /// When false (the default) the tool's buttons are appended after the built-in ones.</summary>
    public virtual bool OverridesToolStrip => false;

    /// <summary>Replace the viewport's orientation cube with <see cref="OnViewManipulatorGUI"/>.</summary>
    public virtual bool OverridesViewManipulator => false;

    /// <summary>Extra self-gating on top of the attribute. Return false to sit out entirely - the
    /// tool stops ticking and its per-viewport state is dropped.</summary>
    public virtual bool IsAvailable(SceneToolContext ctx) => true;

    /// <summary>Called when the tool becomes available (its component was selected, or the editor
    /// started for a global tool).</summary>
    public virtual void OnActivated(SceneToolContext ctx) { }

    /// <summary>Called when the tool stops being available.</summary>
    public virtual void OnDeactivated() { }

    /// <summary>
    /// Register controls and act on the ones that won. Called once per viewport per frame for every
    /// available tool, before the transform gizmo and object picking register theirs.
    /// </summary>
    public abstract void OnSceneInput(SceneToolContext ctx);

    /// <summary>Draw 2D overlays in the viewport foreground.</summary>
    public virtual void OnDrawOverlay(SceneToolContext ctx, Canvas canvas) { }

    /// <summary>Tool strip contents. Appended after the built-in transform buttons unless
    /// <see cref="OverridesToolStrip"/> is set, in which case this replaces them.</summary>
    public virtual void OnToolStripGUI(SceneToolContext ctx, Paper paper, string id) { }

    /// <summary>Replacement orientation-cube UI. Only called when
    /// <see cref="OverridesViewManipulator"/> is set.</summary>
    public virtual void OnViewManipulatorGUI(SceneToolContext ctx, Paper paper, string id, Rect rect) { }

    /// <summary>Contents of the tool's floating settings panel. Drawing nothing hides the panel.</summary>
    public virtual void OnSettingsGUI(SceneToolContext ctx, Paper paper, string id) { }

    /// <summary>
    /// Handle an asset dropped into the viewport while this tool is active. Return true to consume
    /// the drop; returning false falls through to the registered <see cref="ISceneDropHandler"/>.
    /// </summary>
    public virtual bool OnAssetDropped(SceneToolContext ctx, AssetDragPayload payload) => false;
}

/// <summary>
/// Marks a tool that is always available, including with an empty selection - which is what lets a
/// tool build geometry from nothing. Always-available does not mean always-acting: a tool that
/// should only do something when the user asks keeps its own enabled flag and draws a toggle in
/// <see cref="SceneTool.OnToolStripGUI"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GlobalSceneToolAttribute : Attribute
{
    /// <summary>Optional group label for the tool strip.</summary>
    public string? Group { get; init; }
}

/// <summary>
/// Marks a tool that is available only while a component of <see cref="ComponentType"/> is selected.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ComponentSceneToolAttribute : Attribute
{
    public Type ComponentType { get; }

    public ComponentSceneToolAttribute(Type componentType) => ComponentType = componentType;
}
