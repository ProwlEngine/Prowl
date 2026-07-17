// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Color = Prowl.Vector.Color;
using SColor = System.Drawing.Color;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>
/// Scene-view editor for <see cref="LightProbeGroup"/>: click to
/// select probes (Shift adds, Ctrl toggles), drag the position handle to move the selection, and
/// Add / Delete / Duplicate / Select-All from the toolbar or keyboard. Built on the public
/// <see cref="Handles"/> transform API (dogfooding it for a non-GameObject target).
/// </summary>
[SceneViewEditorFor(typeof(LightProbeGroup))]
public class LightProbeGroupSceneEditor : ISceneViewEditor
{
    private const string MoveHandleId = "lightprobegroup_move";

    private LightProbeGroup? _group;
    private readonly List<int> _selection = new();
    private Camera? _cam;
    private bool _dragging;

    public int Priority => 0;

    public void OnActivate(GameObject target)
    {
        _group = target.GetComponent<LightProbeGroup>();
        _selection.Clear();
        _dragging = false;
        Handles.Forget(MoveHandleId);
    }

    public void OnDeactivate()
    {
        _group = null;
        _selection.Clear();
        _dragging = false;
        Handles.Forget(MoveHandleId);
    }

    public bool OnSceneInput(Camera camera, Scene scene, Rect viewport, Ray mouseRay, Float2 mousePos, bool viewportHovered)
    {
        if (_group == null) return false;
        _cam = camera;

        bool ctrl = Input.GetKey(KeyCode.ControlLeft) || Input.GetKey(KeyCode.ControlRight);
        bool shift = Input.GetKey(KeyCode.ShiftLeft) || Input.GetKey(KeyCode.ShiftRight);

        // --- Keyboard shortcuts ---
        if (_selection.Count > 0 && (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)))
        { DeleteSelected(); DrawProbes(); return true; }
        if (ctrl && Input.GetKeyDown(KeyCode.D) && _selection.Count > 0) { DuplicateSelected(); DrawProbes(); return true; }
        if (ctrl && Input.GetKeyDown(KeyCode.A)) { SelectAll(); DrawProbes(); return true; }

        bool consumed = false;
        bool hot = false;

        // --- Move handle for the current selection ---
        if (_selection.Count > 0)
        {
            Float3 centroid = SelectionCentroidWorld();
            Float3 before = centroid;
            bool down = Input.GetMouseButtonDown(0);
            bool moved = Handles.PositionHandle(MoveHandleId, camera, viewport, mouseRay, mousePos, ref centroid, out hot);

            if (hot && down && !_dragging) _dragging = true;
            if (_dragging) Undo.Snapshot(_group);
            if (moved)
            {
                ApplyWorldDelta(centroid - before);
                EditorSceneManager.MarkDirty();
            }
            if (Input.GetMouseButtonUp(0)) _dragging = false;
            if (hot) consumed = true;
        }

        // --- Click selection (only when the handle didn't grab the click) ---
        if (viewportHovered && !hot && Input.GetMouseButtonDown(0))
        {
            int hit = PickProbe(mouseRay);
            if (hit >= 0)
            {
                if (shift) { if (!_selection.Contains(hit)) _selection.Add(hit); }
                else if (ctrl) { if (!_selection.Remove(hit)) _selection.Add(hit); }
                else { _selection.Clear(); _selection.Add(hit); }
                consumed = true;
            }
            else if (!shift && !ctrl && _selection.Count > 0)
            {
                _selection.Clear(); // absorb the click to deselect; stay in edit mode
                consumed = true;
            }
        }

        DrawProbes();

        // While probes are selected, take over the viewport so the object transform gizmo is
        // replaced by our probe handle (and a stray click deselects rather than picking objects).
        return consumed || _selection.Count > 0;
    }

    public void DrawOverlay(Quill.Canvas canvas, Rect viewport) => Handles.Draw(canvas);

    public bool DrawToolbar(Paper paper, string id, Scribe.FontFile font)
    {
        if (_group == null) return false;

        ToolBtn(paper, $"{id}_add", EditorIcons.Plus, font, AddProbe);
        ToolBtn(paper, $"{id}_dup", EditorIcons.Clone, font, () => { if (_selection.Count > 0) DuplicateSelected(); });
        ToolBtn(paper, $"{id}_del", EditorIcons.Trash, font, () => { if (_selection.Count > 0) DeleteSelected(); });
        paper.Box($"{id}_sep").Width(1).Height(18).BackgroundColor(EditorTheme.Ink200);
        ToolBtn(paper, $"{id}_all", EditorIcons.CheckDouble, font, SelectAll);
        ToolBtn(paper, $"{id}_none", EditorIcons.Xmark, font, () => _selection.Clear());

        paper.Box($"{id}_count").Height(24).Width(70)
            .Text($"{_selection.Count}/{_group.ProbePositions.Count}", font)
            .TextColor(EditorTheme.Ink400).FontSize(10f).Alignment(TextAlignment.MiddleCenter);

        return true; // replace the default transform toolbar while editing probes
    }

    private void ToolBtn(Paper paper, string id, string icon, Scribe.FontFile font, Action onClick)
    {
        paper.Box(id)
            .Width(24).Height(24).Rounded(4)
            .BackgroundColor(SColor.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
            .Text(icon, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => onClick());
    }

    // --- probe ops ---

    private void AddProbe()
    {
        if (_group == null) return;
        Undo.Snapshot(_group);
        Float3 world = _cam != null
            ? _cam.GameObject.Transform.Position + _cam.GameObject.Transform.Forward * 5f
            : _group.Transform.Position;
        _group.ProbePositions.Add(_group.Transform.InverseTransformPoint(world));
        _selection.Clear();
        _selection.Add(_group.ProbePositions.Count - 1);
        EditorSceneManager.MarkDirty();
    }

    private void DeleteSelected()
    {
        if (_group == null || _selection.Count == 0) return;
        Undo.Snapshot(_group);
        _selection.Sort();
        for (int k = _selection.Count - 1; k >= 0; k--)
        {
            int idx = _selection[k];
            if (idx >= 0 && idx < _group.ProbePositions.Count)
                _group.ProbePositions.RemoveAt(idx);
        }
        _selection.Clear();
        EditorSceneManager.MarkDirty();
    }

    private void DuplicateSelected()
    {
        if (_group == null || _selection.Count == 0) return;
        Undo.Snapshot(_group);
        var dup = new List<int>();
        var src = new List<int>(_selection);
        foreach (int i in src)
        {
            if (i < 0 || i >= _group.ProbePositions.Count) continue;
            _group.ProbePositions.Add(_group.ProbePositions[i]);
            dup.Add(_group.ProbePositions.Count - 1);
        }
        _selection.Clear();
        _selection.AddRange(dup);
        EditorSceneManager.MarkDirty();
    }

    private void SelectAll()
    {
        if (_group == null) return;
        _selection.Clear();
        for (int i = 0; i < _group.ProbePositions.Count; i++) _selection.Add(i);
    }

    // --- helpers ---

    private void ApplyWorldDelta(Float3 worldDelta)
    {
        var l2w = _group!.Transform.LocalToWorldMatrix;
        var w2l = _group.Transform.WorldToLocalMatrix;
        foreach (int i in _selection)
        {
            Float3 world = Float4x4.TransformPoint(_group.ProbePositions[i], l2w) + worldDelta;
            _group.ProbePositions[i] = Float4x4.TransformPoint(world, w2l);
        }
    }

    private Float3 SelectionCentroidWorld()
    {
        var l2w = _group!.Transform.LocalToWorldMatrix;
        Float3 sum = Float3.Zero;
        foreach (int i in _selection) sum += Float4x4.TransformPoint(_group.ProbePositions[i], l2w);
        return sum / _selection.Count;
    }

    private int PickProbe(Ray ray)
    {
        var l2w = _group!.Transform.LocalToWorldMatrix;
        Float3 o = ray.Origin, d = ray.Direction;
        int best = -1; float bestT = float.MaxValue;
        for (int i = 0; i < _group.ProbePositions.Count; i++)
        {
            Float3 p = Float4x4.TransformPoint(_group.ProbePositions[i], l2w);
            float t = Float3.Dot(p - o, d);
            if (t < 0) continue;
            Float3 diff = (o + d * t) - p;
            float dist = MathF.Sqrt(Float3.Dot(diff, diff));
            float pickR = MathF.Max(0.12f, t * 0.03f); // roughly screen-constant
            if (dist <= pickR && t < bestT) { bestT = t; best = i; }
        }
        return best;
    }

    private void DrawProbes()
    {
        if (_group == null) return;
        var l2w = _group.Transform.LocalToWorldMatrix;
        var selColor = new Color(0.3f, 0.6f, 1f, 1f);
        foreach (int i in _selection)
        {
            if (i < 0 || i >= _group.ProbePositions.Count) continue;
            Debug.DrawWireSphere(Float4x4.TransformPoint(_group.ProbePositions[i], l2w), 0.13f, selColor, 8);
        }
    }
}
