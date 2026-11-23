// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// The Editor Animation Window ("Animate Anything"):
/// authors unified <see cref="AnimationClip"/>s with a Dope Sheet view, Curves view,
/// Animation Event lane, live scene previewing (both skeletal poses and property tracks), Rig Curve Unpacking, and Auto-Key Recording mode.
/// </summary>
public class AnimationPanel : DockPanel
{
    [MenuItem("Window/Animation/Animation", priority: 20)]
    public static void OpenMenu() => EditorApplication.Instance?.OpenPanel<AnimationPanel>();

    /// <summary>Opens the Animation panel focused on a specific <see cref="AnimationClip"/>.</summary>
    public static void OpenClip(AnimationClip clip)
    {
        AnimationPanel? panel = EditorApplication.Instance?.OpenPanel<AnimationPanel>();
        panel?.SelectClip(clip);
    }

    /// <summary>Opens the Animation panel focused on a specific <see cref="Animator"/>.</summary>
    public static void OpenFor(Animator animator)
    {
        if (animator.IsValid() && animator.GameObject.IsValid())
            Selection.Select(animator.GameObject);
        AnimationPanel? panel = EditorApplication.Instance?.OpenPanel<AnimationPanel>();
        if (panel != null && animator.IsValid())
            panel.BindAnimator(animator);
    }

    public override string Title => "Animation";
    public override string Icon => EditorIcons.Film;

    // ================================================================
    //  Target & Clip State
    // ================================================================

    private GameObject? _targetRoot;
    private Animator? _animator;
    private AnimationClip? _clip;

    private AnimationClip? ClipAsset => _clip;
    private bool IsClipValid => _clip.IsValid();

    // Playback / Preview / Record state
    private bool _previewing;
    private bool _recording;
    private bool _playing;
    private float _currentTime;

    // View mode: false = Dope Sheet, true = Curves
    private bool _curvesMode;

    // Left panel width
    private const float LeftPaneWidth = 340f;
    private const float HeaderBarHeight = 32f;
    private const float TimeRulerHeight = 24f;
    private const float EventBarHeight = 18f;
    private const float TrackRowHeight = 24f;
    private const float BottomBarHeight = 26f;

    // Timeline horizontal view range in seconds
    private float _viewMinTime = 0f;
    private float _viewMaxTime = 2f;

    // Sequencer-style In / Out working loop range
    private bool _useWorkRange;
    private float _workRangeIn = 0f;
    private float _workRangeOut = 1f;

    // Curve vertical view range
    private float _curveMinVal = -2f;
    private float _curveMaxVal = 2f;
    private bool _curveBoundsInitialized;

    // Selected track and keyframe (plus multi-keyframe selection set for Unreal-style marquee & group dragging)
    private int _selectedRowIndex = -1;
    private int _selectedKeyIndex = -1;
    private int _selectedEventIndex = -1;
    private readonly HashSet<(int RowIndex, int KeyIndex)> _selectedKeys = [];
    private readonly List<(int RowIndex, int KeyIndex, float StartTime, float StartValue)> _multiDragSnapshot = [];

    // Keyframe / Playhead / Marquee dragging state
    private enum DragPart { None, Playhead, Keyframe, MarqueeSelect, Event, EventStart, EventEnd, WorkRangeIn, WorkRangeOut, PanTimeline }
    private DragPart _dragPart;
    private int _dragRow = -1;
    private int _dragKey = -1;
    private float _dragStartTime;
    private float _dragEndTime;
    private float _dragClickOffset;
    private float _dragStartValue;
    private Float2 _dragPressLocalPos;
    private Float2 _marqueeStartLocal;
    private Float2 _marqueeEndLocal;
    private float _timelineWidth = 400f;
    private float _timelineHeight = 240f;

    // Unreal Engine Sequencer RMB / MMB navigation state (RMB drag = Pan, Alt+RMB drag = Zoom, RMB click = Context Menu, MMB drag = Pan / Scrub)
    private Rect _canvasScreenRect;
    private Rect _rulerScreenRect;
    private bool _rmbPanActive;
    private bool _rmbZoomActive;
    private bool _rmbMoved;
    private bool _mmbPanActive;
    private Float2 _rmbPressScreenPos;
    private Float2 _rmbLastScreenPos;
    private Float2 _rmbPivotNorm;

    // Add Property popup state
    private bool _addPropertyOpen;
    private readonly MenuTreeState _addPropertyTreeState = new();
    private List<MenuTreeEntry> _addPropertyEntries = [];

    // Baseline snapshots taken when Preview / Record mode starts so stopping Preview restores the scene
    private readonly Dictionary<(GameObject Go, string ComponentType, string BasePath), (object? Value, Type? MemberType)> _previewBaseline = [];
    private SceneClipBinding? _cachedPreviewBinding;
    private GameObject? _cachedPreviewRoot;
    private AnimationClip? _cachedPreviewClip;
    private int _cachedPreviewVersion = -1;

    // Last observed scalar / object / string values for Auto-Key Recording
    private readonly Dictionary<(string RelPath, string CompType, string PropPath), float> _recordedScalars = [];
    private readonly Dictionary<(string RelPath, string CompType, string PropPath), EngineObject?> _recordedObjects = [];
    private readonly Dictionary<(string RelPath, string CompType, string PropPath), string> _recordedStrings = [];
    private readonly Dictionary<GameObject, (Float3 Pos, Float3 Euler, Float3 Scale, bool Enabled)> _watchedTransforms = new(ReferenceEqualityComparer.Instance);
    private bool _suppressAutoRecord;

    private bool _subscribedUndo;

    // ================================================================
    //  Display Row Model
    // ================================================================

    private enum TrackRowKind { Group, Curve, Object, String }

    private sealed class TrackRow
    {
        public required TrackRowKind Kind;
        public required string RelativePath;
        public required string ComponentType;
        public required string PropertyPath;
        public required string Label;
        public required Color AccentColor;
        public int Indent;
        public string GroupKey = string.Empty;
        public List<SceneCurveTrack>? GroupTracks;
        public List<float>? SummaryTimes;
        public SceneCurveTrack? CurveTrack;
        public SceneObjectTrack? ObjectTrack;
        public SceneStringTrack? StringTrack;
    }

    private readonly List<TrackRow> _rows = [];
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);

    public override void OnClosed()
    {
        StopPreviewAndRestore();
        UnsubscribeUndoHook();
    }

    private void EnsureUndoHook()
    {
        if (_subscribedUndo) return;
        Undo.postprocessModifications += OnPostprocessUndoModifications;
        _subscribedUndo = true;
    }

    private void UnsubscribeUndoHook()
    {
        if (!_subscribedUndo) return;
        Undo.postprocessModifications -= OnPostprocessUndoModifications;
        _subscribedUndo = false;
    }

    /// <summary> selects an explicit clip to edit in the window. </summary>
    public void SelectClip(AnimationClip clip)
    {
        if (ReferenceEquals(_clip, clip)) return;
        StopPreviewAndRestore();
        _clip = clip;
        _currentTime = 0f;
        _selectedRowIndex = -1;
        _selectedKeyIndex = -1;
        _selectedEventIndex = -1;
        _selectedKeys.Clear();
        _curveBoundsInitialized = false;
        FitTimelineRange();
    }

    /// <summary> Binds a specific <see cref="Animator"/> to the window. </summary>
    public void BindAnimator(Animator animator)
    {
        if (animator.IsNotValid()) return;
        _targetRoot = animator.GameObject;
        _animator = animator;
        AnimationClip? first = animator.CurrentClip.IsValid() ? animator.CurrentClip
            : animator.Clips.FirstOrDefault(c => c.IsValid());
        if (first.IsValid())
            SelectClip(first!);
    }

    private void FitTimelineRange()
    {
        float dur = IsClipValid ? _clip!.Duration : 1f;
        _viewMinTime = 0f;
        _viewMaxTime = MathF.Max(1f, dur * 1.25f);
        _workRangeIn = 0f;
        _workRangeOut = MathF.Max(0.25f, dur > 0f ? dur : 1f);
    }

    // ================================================================
    //  Main GUI Loop
    // ================================================================

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        EnsureUndoHook();
        SyncTargetFromSelection();
        TickPlayback((float)Time.UnscaledDeltaTime);

        if (_recording && _targetRoot.IsValid() && IsClipValid && !Application.IsPlaying)
            PollAutoKeyChanges();

        RebuildRows();

        using (paper.Column("anim_root").Size(width, height).Clip().Enter())
        {
            DrawTopTransportBar(paper, font, width);
            EditorGUI.Divider(paper, "anim_top_div");

            if (!IsClipValid)
            {
                DrawEmptyOrSetupState(paper, font, width, height - HeaderBarHeight - 1f);
                return;
            }

            float bodyHeight = MathF.Max(60f, height - HeaderBarHeight - BottomBarHeight - 2f);
            using (paper.Row("anim_body").Width(width).Height(bodyHeight).Clip().Enter())
            {
                DrawLeftTrackList(paper, font, LeftPaneWidth, bodyHeight);
                paper.Box("anim_vdiv").Width(1).Height(bodyHeight).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
                DrawRightTimelineArea(paper, font, MathF.Max(100f, width - LeftPaneWidth - 1f), bodyHeight);
            }

            EditorGUI.Divider(paper, "anim_bot_div");
            DrawBottomBar(paper, font, width);
        }
    }

    // ================================================================
    //  Selection & Context Synchronization
    // ================================================================

    private void SyncTargetFromSelection()
    {
        // If user selected an AnimationClip asset directly in the Project panel
        if (Selection.ActiveObject is ContentItem ci && !ci.IsFolder && ci.Guid != Guid.Empty)
        {
            if (AssetDatabase.Get(ci.Guid) is AnimationClip selectedAnimClip && selectedAnimClip.IsValid())
            {
                if (!ReferenceEquals(_clip, selectedAnimClip))
                    SelectClip(selectedAnimClip);
            }
        }
        else if (Selection.ActiveObject is AnimationClip directAnimClip && directAnimClip.IsValid())
        {
            if (!ReferenceEquals(_clip, directAnimClip))
                SelectClip(directAnimClip);
        }

        // If user selected a GameObject in the Hierarchy / Scene
        GameObject? selectedGo = Selection.GetActiveAs<GameObject>();
        if (selectedGo.IsValid())
        {
            Animator? foundAnimator = selectedGo!.GetComponent<Animator>();
            if (foundAnimator.IsNotValid())
                foundAnimator = selectedGo.GetComponentInParent<Animator>();

            GameObject newRoot = foundAnimator.IsValid() ? foundAnimator!.GameObject : selectedGo;

            if (!ReferenceEquals(_targetRoot, newRoot) || !ReferenceEquals(_animator, foundAnimator))
            {
                StopPreviewAndRestore();
                _targetRoot = newRoot;
                _animator = foundAnimator;

                List<AnimationClip> clips = GetAvailableClips();
                if (clips.Count > 0 && (!IsClipValid || !clips.Contains(_clip!)))
                    SelectClip(clips[0]);
            }
        }

        if (_targetRoot.IsNotValid())
        {
            if (_previewing || _recording)
                StopPreviewAndRestore();
            _targetRoot = null;
            _animator = null;
        }
    }

    private List<AnimationClip> GetAvailableClips()
    {
        var list = new List<AnimationClip>();
        if (_animator.IsValid())
        {
            for (int i = 0; i < _animator!.Clips.Count; i++)
            {
                AnimationClip c = _animator.Clips[i];
                if (c.IsValid() && !list.Contains(c))
                    list.Add(c);
            }
        }
        return list;
    }

    // ================================================================
    //  Playback, Preview & Auto-Key Recording
    // ================================================================

    private void TickPlayback(float unscaledDt)
    {
        if (Application.IsPlaying)
        {
            if (_previewing)
                StopPreviewAndRestore();

            // In Play mode, follow the live Animator if bound
            if (_animator.IsValid() && _animator!.CurrentClip.IsValid())
            {
                _clip = _animator.CurrentClip;
                _currentTime = _animator.Time;
                _playing = !_animator.IsPaused;
            }
            return;
        }

        if (!_playing || !IsClipValid) return;

        float duration = _clip!.Duration;
        if (duration <= 1e-4f)
        {
            _currentTime = 0f;
            _playing = false;
            return;
        }

        _currentTime += unscaledDt;
        if (_useWorkRange && _workRangeOut > _workRangeIn + 1e-4f)
        {
            float rangeStart = MathF.Max(0f, _workRangeIn);
            float rangeEnd = MathF.Min(duration, _workRangeOut);
            if (rangeEnd > rangeStart + 1e-4f)
            {
                if (_currentTime >= rangeEnd || _currentTime < rangeStart)
                    _currentTime = rangeStart + ((_currentTime - rangeStart) % (rangeEnd - rangeStart));
                SampleAtPlayhead();
                return;
            }
        }

        if (_currentTime >= duration)
        {
            if (_clip.WrapMode == SceneClipWrapMode.Once)
            {
                _currentTime = duration;
                _playing = false;
            }
            else
            {
                _currentTime %= duration;
            }
        }

        SampleAtPlayhead();
    }

    private void SetPlayhead(float time, bool sampleScene = true, bool snapToFrame = true)
    {
        float fps = IsClipValid ? MathF.Max(1f, _clip!.FrameRate) : 60f;
        float clamped = MathF.Max(0f, time);
        _currentTime = snapToFrame ? MathF.Round(clamped * fps) / fps : clamped;

        if (sampleScene && !Application.IsPlaying && IsClipValid && _targetRoot.IsValid())
        {
            if (!_previewing)
                EnterPreviewMode();
            SampleAtPlayhead();
        }
    }

    private void EnterPreviewMode()
    {
        if (_previewing) return;
        _previewing = true;
        CapturePreviewBaseline();
        SnapshotCurrentValuesForRecording();
    }

    private void StopPreviewAndRestore()
    {
        _playing = false;
        _recording = false;
        if (!_previewing) return;
        _previewing = false;

        _suppressAutoRecord = true;
        try
        {
            foreach (((GameObject go, string compType, string basePath), (object? val, Type? _)) in _previewBaseline)
            {
                if (go.IsNotValid()) continue;
                SceneClipBinding.TryWriteRawProperty(go, "", compType, basePath, val);
            }
        }
        finally
        {
            _suppressAutoRecord = false;
            _previewBaseline.Clear();
            _recordedScalars.Clear();
            _recordedObjects.Clear();
            _recordedStrings.Clear();
            _watchedTransforms.Clear();
            _cachedPreviewBinding = null;
            _cachedPreviewRoot = null;
            _cachedPreviewClip = null;
            _cachedPreviewVersion = -1;
        }
    }

    private void CapturePreviewBaseline()
    {
        if (_targetRoot.IsNotValid() || !IsClipValid) return;

        // If previewing a skeletal/humanoid AnimationClip, also snapshot all hierarchy transforms so stopping Preview restores the bind pose
        if (_clip is AnimationClip { HasMotionPayload: true })
            CaptureHierarchyTransformsBaseline(_targetRoot!);

        for (int i = 0; i < _clip!.CurveTracks.Count; i++)
        {
            SceneCurveTrack t = _clip.CurveTracks[i];
            CacheBaselineProperty(t.RelativePath, t.ComponentType, StripSubChannel(t.PropertyPath));
        }
        for (int i = 0; i < _clip.ObjectTracks.Count; i++)
        {
            SceneObjectTrack t = _clip.ObjectTracks[i];
            CacheBaselineProperty(t.RelativePath, t.ComponentType, t.PropertyPath);
        }
        for (int i = 0; i < _clip.StringTracks.Count; i++)
        {
            SceneStringTrack t = _clip.StringTracks[i];
            CacheBaselineProperty(t.RelativePath, t.ComponentType, t.PropertyPath);
        }
    }

    private void CaptureHierarchyTransformsBaseline(GameObject current)
    {
        if (current.IsNotValid() || _targetRoot.IsNotValid()) return;
        string relPath = Transform.GetRelativePath(current.Transform, _targetRoot!.Transform);
        CacheBaselineProperty(relPath, "Transform", "LocalPosition");
        CacheBaselineProperty(relPath, "Transform", "LocalRotation");
        CacheBaselineProperty(relPath, "Transform", "LocalScale");
        for (int i = 0; i < current.Children.Count; i++)
            CaptureHierarchyTransformsBaseline(current.Children[i]);
    }

    private void CacheBaselineProperty(string relativePath, string componentType, string basePath)
    {
        if (_targetRoot.IsNotValid()) return;
        GameObject? go = SceneClipBinding.ResolveGameObject(_targetRoot!, relativePath);
        if (go.IsNotValid()) return;

        var key = (go!, componentType, basePath);
        if (_previewBaseline.ContainsKey(key)) return;

        if (SceneClipBinding.TryReadRawProperty(_targetRoot!, relativePath, componentType, basePath, out object? val, out Type? memberType))
            _previewBaseline[key] = (val, memberType);
    }

    private static string StripSubChannel(string propertyPath)
    {
        int lastDot = propertyPath.LastIndexOf('.');
        if (lastDot < 0) return propertyPath;
        string suffix = propertyPath[(lastDot + 1)..];
        if (suffix is "X" or "Y" or "Z" or "W" or "R" or "G" or "B" or "A")
            return propertyPath[..lastDot];
        return propertyPath;
    }

    private void SampleAtPlayhead()
    {
        if (_targetRoot.IsNotValid() || !IsClipValid) return;

        CapturePreviewBaseline();
        _suppressAutoRecord = true;
        try
        {
            // 1. If this is an AnimationClip with skeletal/humanoid Motion data, sample the skeletal pose first
            if (_clip is AnimationClip animClip && animClip.HasMotionPayload)
            {
                Avatar? avatar = _animator.IsValid() && _animator!.Avatar.IsValid() ? _animator.Avatar : animClip.Avatar;
                if (avatar.IsValid())
                {
                    if (avatar!.State is not (AssetState.Missing or AssetState.Failed))
                        avatar.Load();
                    if (avatar.IsLoaded && avatar.Skeleton != null)
                    {
                        Motion.AnimationClipBase? runtime = animClip.GetClip(avatar.Runtime);
                        if (runtime != null)
                        {
                            var rigBinding = new AnimatorBinding(_targetRoot!.Transform, avatar.Skeleton);
                            var pose = new Motion.Pose(avatar.Skeleton);
                            pose.SetToReferencePose(false);
                            float norm = animClip.Duration > 1e-6f ? Math.Clamp(_currentTime / animClip.Duration, 0f, 1f) : 0f;
                            runtime.GetPose(norm, pose);
                            for (int b = 0; b < pose.BoneCount; b++)
                                rigBinding.ApplyBone(b, pose.GetTransform(b));
                            for (int c = 0; c < pose.FloatChannelCount; c++)
                                rigBinding.ApplyChannel(c, pose.GetFloat(c));
                        }
                    }
                }
            }

            // 2. Sample property tracks layered on top
            if (_cachedPreviewBinding == null ||
                !ReferenceEquals(_cachedPreviewRoot, _targetRoot) ||
                !ReferenceEquals(_cachedPreviewClip, _clip) ||
                _cachedPreviewVersion != _clip!.Version)
            {
                _cachedPreviewBinding = new SceneClipBinding(_targetRoot!, _clip!);
                _cachedPreviewRoot = _targetRoot;
                _cachedPreviewClip = _clip;
                _cachedPreviewVersion = _clip.Version;
            }
            _cachedPreviewBinding.Sample(_currentTime);
        }
        finally
        {
            _suppressAutoRecord = false;
        }
        SnapshotCurrentValuesForRecording();
    }

    private void SnapshotCurrentValuesForRecording()
    {
        _recordedScalars.Clear();
        _recordedObjects.Clear();
        _recordedStrings.Clear();
        _watchedTransforms.Clear();

        if (_targetRoot.IsNotValid() || !IsClipValid) return;

        for (int i = 0; i < _clip!.CurveTracks.Count; i++)
        {
            SceneCurveTrack t = _clip.CurveTracks[i];
            if (SceneClipBinding.TryReadScalar(_targetRoot!, t.RelativePath, t.ComponentType, t.PropertyPath, out float v))
                _recordedScalars[(t.RelativePath, t.ComponentType, t.PropertyPath)] = v;
        }
        for (int i = 0; i < _clip.ObjectTracks.Count; i++)
        {
            SceneObjectTrack t = _clip.ObjectTracks[i];
            if (SceneClipBinding.TryReadObject(_targetRoot!, t.RelativePath, t.ComponentType, t.PropertyPath, out EngineObject? v))
                _recordedObjects[(t.RelativePath, t.ComponentType, t.PropertyPath)] = v;
        }
        for (int i = 0; i < _clip.StringTracks.Count; i++)
        {
            SceneStringTrack t = _clip.StringTracks[i];
            if (SceneClipBinding.TryReadString(_targetRoot!, t.RelativePath, t.ComponentType, t.PropertyPath, out string v))
                _recordedStrings[(t.RelativePath, t.ComponentType, t.PropertyPath)] = v;
        }

        SnapshotHierarchyTransforms(_targetRoot!);
    }

    private void SnapshotHierarchyTransforms(GameObject go)
    {
        if (go.IsNotValid()) return;
        _watchedTransforms[go] = (go.Transform.LocalPosition, go.Transform.LocalEulerAngles, go.Transform.LocalScale, go.Enabled);
        for (int i = 0; i < go.Children.Count; i++)
            SnapshotHierarchyTransforms(go.Children[i]);
    }

    private void PollAutoKeyChanges()
    {
        if (_suppressAutoRecord || _playing || _targetRoot.IsNotValid() || !IsClipValid) return;

        bool changedAny = false;

        // 1. Check all existing tracks for value changes
        for (int i = 0; i < _clip!.CurveTracks.Count; i++)
        {
            SceneCurveTrack t = _clip.CurveTracks[i];
            var key = (t.RelativePath, t.ComponentType, t.PropertyPath);
            if (!SceneClipBinding.TryReadScalar(_targetRoot!, t.RelativePath, t.ComponentType, t.PropertyPath, out float current))
                continue;

            if (_recordedScalars.TryGetValue(key, out float prev) && MathF.Abs(current - prev) > 1e-4f)
            {
                if (!changedAny) { Undo.RecordObject(ClipAsset!, "Record Keyframe"); changedAny = true; }
                _clip.SetCurveKey(t.RelativePath, t.ComponentType, t.PropertyPath, _currentTime, current);
                _recordedScalars[key] = current;
            }
        }

        for (int i = 0; i < _clip.ObjectTracks.Count; i++)
        {
            SceneObjectTrack t = _clip.ObjectTracks[i];
            var key = (t.RelativePath, t.ComponentType, t.PropertyPath);
            if (!SceneClipBinding.TryReadObject(_targetRoot!, t.RelativePath, t.ComponentType, t.PropertyPath, out EngineObject? current))
                continue;

            if (_recordedObjects.TryGetValue(key, out EngineObject? prev) && !ReferenceEquals(current, prev))
            {
                if (!changedAny) { Undo.RecordObject(ClipAsset!, "Record Object Keyframe"); changedAny = true; }
                _clip.SetObjectKey(t.RelativePath, t.ComponentType, t.PropertyPath, _currentTime, current);
                _recordedObjects[key] = current;
            }
        }

        for (int i = 0; i < _clip.StringTracks.Count; i++)
        {
            SceneStringTrack t = _clip.StringTracks[i];
            var key = (t.RelativePath, t.ComponentType, t.PropertyPath);
            if (!SceneClipBinding.TryReadString(_targetRoot!, t.RelativePath, t.ComponentType, t.PropertyPath, out string current))
                continue;

            if (_recordedStrings.TryGetValue(key, out string? prev) && current != prev)
            {
                if (!changedAny) { Undo.RecordObject(ClipAsset!, "Record String Keyframe"); changedAny = true; }
                _clip.SetStringKey(t.RelativePath, t.ComponentType, t.PropertyPath, _currentTime, current);
                _recordedStrings[key] = current;
            }
        }

        // 2. Check Transform / GameObject changes across the hierarchy (even if tracks don't exist yet)
        PollHierarchyTransforms(_targetRoot!, ref changedAny);

        if (changedAny)
            EditorUtility.SetDirty(ClipAsset!);
    }

    private void PollHierarchyTransforms(GameObject go, ref bool changedAny)
    {
        if (go.IsNotValid() || _targetRoot.IsNotValid() || !IsClipValid) return;

        Float3 pos = go.Transform.LocalPosition;
        Float3 euler = go.Transform.LocalEulerAngles;
        Float3 scale = go.Transform.LocalScale;
        bool enabled = go.Enabled;

        if (_watchedTransforms.TryGetValue(go, out (Float3 Pos, Float3 Euler, Float3 Scale, bool Enabled) prev))
        {
            string relPath = Transform.GetRelativePath(go.Transform, _targetRoot!.Transform);

            if (!pos.Equals(prev.Pos))
            {
                CacheBaselineProperty(relPath, "Transform", "LocalPosition");
                if (MathF.Abs(pos.X - prev.Pos.X) > 1e-4f) RecordAutoCurve(relPath, "Transform", "LocalPosition.X", pos.X, ref changedAny);
                if (MathF.Abs(pos.Y - prev.Pos.Y) > 1e-4f) RecordAutoCurve(relPath, "Transform", "LocalPosition.Y", pos.Y, ref changedAny);
                if (MathF.Abs(pos.Z - prev.Pos.Z) > 1e-4f) RecordAutoCurve(relPath, "Transform", "LocalPosition.Z", pos.Z, ref changedAny);
            }
            if (!euler.Equals(prev.Euler))
            {
                CacheBaselineProperty(relPath, "Transform", "LocalEulerAngles");
                if (MathF.Abs(euler.X - prev.Euler.X) > 1e-4f) RecordAutoCurve(relPath, "Transform", "LocalEulerAngles.X", euler.X, ref changedAny);
                if (MathF.Abs(euler.Y - prev.Euler.Y) > 1e-4f) RecordAutoCurve(relPath, "Transform", "LocalEulerAngles.Y", euler.Y, ref changedAny);
                if (MathF.Abs(euler.Z - prev.Euler.Z) > 1e-4f) RecordAutoCurve(relPath, "Transform", "LocalEulerAngles.Z", euler.Z, ref changedAny);
            }
            if (!scale.Equals(prev.Scale))
            {
                CacheBaselineProperty(relPath, "Transform", "LocalScale");
                if (MathF.Abs(scale.X - prev.Scale.X) > 1e-4f) RecordAutoCurve(relPath, "Transform", "LocalScale.X", scale.X, ref changedAny);
                if (MathF.Abs(scale.Y - prev.Scale.Y) > 1e-4f) RecordAutoCurve(relPath, "Transform", "LocalScale.Y", scale.Y, ref changedAny);
                if (MathF.Abs(scale.Z - prev.Scale.Z) > 1e-4f) RecordAutoCurve(relPath, "Transform", "LocalScale.Z", scale.Z, ref changedAny);
            }
            if (enabled != prev.Enabled)
            {
                CacheBaselineProperty(relPath, "GameObject", "Enabled");
                RecordAutoCurve(relPath, "GameObject", "Enabled", enabled ? 1f : 0f, ref changedAny);
            }
        }

        _watchedTransforms[go] = (pos, euler, scale, enabled);

        for (int i = 0; i < go.Children.Count; i++)
            PollHierarchyTransforms(go.Children[i], ref changedAny);
    }

    private void RecordAutoCurve(string relPath, string compType, string propPath, float value, ref bool changedAny)
    {
        if (!IsClipValid) return;
        if (!changedAny)
        {
            Undo.RecordObject(ClipAsset!, "Record Keyframe");
            changedAny = true;
        }
        _clip!.SetCurveKey(relPath, compType, propPath, _currentTime, value);
        _recordedScalars[(relPath, compType, propPath)] = value;
    }

    // Intercepts any Inspector modification on a Component under _targetRoot while _recording is active
    private UndoPropertyModification[] OnPostprocessUndoModifications(UndoPropertyModification[] modifications)
    {
        if (!_recording || _suppressAutoRecord || _targetRoot.IsNotValid() || !IsClipValid || Application.IsPlaying)
            return modifications;

        bool recorded = false;
        foreach (UndoPropertyModification mod in modifications)
        {
            if (mod.Target is not Component comp || comp.IsNotValid() || comp.GameObject.IsNotValid())
                continue;

            if (!GameObject.IsChildOrSameTransform(comp.GameObject, _targetRoot!))
                continue;

            string relPath = Transform.GetRelativePath(comp.GameObject.Transform, _targetRoot!.Transform);
            string compName = comp.GetType().Name;
            string memberName = mod.PropertyPath.TrimStart('_');

            if (!SceneClipBinding.TryResolveAccessChain(comp, memberName, out SceneClipBinding.MemberAccessChain chain))
                continue;

            string basePath = chain.BasePath;
            CacheBaselineProperty(relPath, compName, basePath);

            if (!SceneClipBinding.TryDescribeType(compName, basePath, chain.LeafType, out AnimatablePropertyDescriptor desc))
                continue;

            if (!recorded)
            {
                Undo.RecordObject(ClipAsset!, "Record Keyframe");
                recorded = true;
            }

            if (desc.IsObjectReference)
            {
                if (SceneClipBinding.TryReadObject(_targetRoot!, relPath, compName, basePath, out EngineObject? objVal))
                {
                    _clip!.SetObjectKey(relPath, compName, basePath, _currentTime, objVal);
                    _recordedObjects[(relPath, compName, basePath)] = objVal;
                }
            }
            else if (desc.IsString)
            {
                if (SceneClipBinding.TryReadString(_targetRoot!, relPath, compName, basePath, out string strVal))
                {
                    _clip!.SetStringKey(relPath, compName, basePath, _currentTime, strVal);
                    _recordedStrings[(relPath, compName, basePath)] = strVal;
                }
            }
            else if (desc.SubChannels.Length > 0)
            {
                foreach (string sub in desc.SubChannels)
                {
                    string subPath = $"{basePath}.{sub}";
                    if (SceneClipBinding.TryReadScalar(_targetRoot!, relPath, compName, subPath, out float fVal))
                    {
                        _clip!.SetCurveKey(relPath, compName, subPath, _currentTime, fVal);
                        _recordedScalars[(relPath, compName, subPath)] = fVal;
                    }
                }
            }
            else
            {
                if (SceneClipBinding.TryReadScalar(_targetRoot!, relPath, compName, basePath, out float fVal))
                {
                    _clip!.SetCurveKey(relPath, compName, basePath, _currentTime, fVal);
                    _recordedScalars[(relPath, compName, basePath)] = fVal;
                }
            }
        }

        if (recorded)
            EditorUtility.SetDirty(ClipAsset!);

        return modifications;
    }

    // ================================================================
    //  Row Rebuilding
    // ================================================================

    private void RebuildRows()
    {
        _rows.Clear();
        if (!IsClipValid) return;

        // Group sub-channel curve tracks (e.g. LocalPosition.X/.Y/.Z, Color.R/.G/.B/.A) into collapsible parent rows
        var grouped = new Dictionary<string, List<(int Index, SceneCurveTrack Track)>>(StringComparer.Ordinal);
        var groupOrder = new List<string>();

        for (int i = 0; i < _clip!.CurveTracks.Count; i++)
        {
            SceneCurveTrack t = _clip.CurveTracks[i];
            string basePath = StripSubChannel(t.PropertyPath);
            string key = $"{t.RelativePath}|{t.ComponentType}|{basePath}";
            if (!grouped.TryGetValue(key, out List<(int Index, SceneCurveTrack Track)>? list))
            {
                list = [];
                grouped[key] = list;
                groupOrder.Add(key);
            }
            list.Add((i, t));
        }

        foreach (string key in groupOrder)
        {
            List<(int Index, SceneCurveTrack Track)> list = grouped[key];
            SceneCurveTrack first = list[0].Track;
            string basePath = StripSubChannel(first.PropertyPath);
            bool isMultiChannelGroup = list.Count > 1 || basePath != first.PropertyPath;

            if (isMultiChannelGroup)
            {
                var groupTracks = new List<SceneCurveTrack>(list.Count);
                for (int i = 0; i < list.Count; i++)
                    groupTracks.Add(list[i].Track);

                _rows.Add(new TrackRow
                {
                    Kind = TrackRowKind.Group,
                    RelativePath = first.RelativePath,
                    ComponentType = first.ComponentType,
                    PropertyPath = basePath,
                    Label = FormatRowLabel(first.RelativePath, first.ComponentType, basePath),
                    AccentColor = EditorTheme.Blue400,
                    GroupKey = key,
                    GroupTracks = groupTracks,
                    SummaryTimes = ComputeGroupSummaryTimes(groupTracks),
                });

                if (!_collapsedGroups.Contains(key))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        (int idx, SceneCurveTrack t) = list[i];
                        string subLabel = t.PropertyPath.Length > basePath.Length + 1
                            ? t.PropertyPath[(basePath.Length + 1)..]
                            : t.PropertyPath;
                        _rows.Add(new TrackRow
                        {
                            Kind = TrackRowKind.Curve,
                            RelativePath = t.RelativePath,
                            ComponentType = t.ComponentType,
                            PropertyPath = t.PropertyPath,
                            Label = $"{basePath}.{subLabel}",
                            AccentColor = ChannelColor(t.PropertyPath, idx),
                            Indent = 1,
                            GroupKey = key,
                            CurveTrack = t,
                        });
                    }
                }
            }
            else
            {
                (int idx, SceneCurveTrack t) = list[0];
                _rows.Add(new TrackRow
                {
                    Kind = TrackRowKind.Curve,
                    RelativePath = t.RelativePath,
                    ComponentType = t.ComponentType,
                    PropertyPath = t.PropertyPath,
                    Label = FormatRowLabel(t.RelativePath, t.ComponentType, t.PropertyPath),
                    AccentColor = ChannelColor(t.PropertyPath, idx),
                    CurveTrack = t,
                });
            }
        }

        for (int i = 0; i < _clip.ObjectTracks.Count; i++)
        {
            SceneObjectTrack t = _clip.ObjectTracks[i];
            _rows.Add(new TrackRow
            {
                Kind = TrackRowKind.Object,
                RelativePath = t.RelativePath,
                ComponentType = t.ComponentType,
                PropertyPath = t.PropertyPath,
                Label = FormatRowLabel(t.RelativePath, t.ComponentType, t.PropertyPath),
                AccentColor = EditorTheme.Purple400,
                ObjectTrack = t,
            });
        }

        for (int i = 0; i < _clip.StringTracks.Count; i++)
        {
            SceneStringTrack t = _clip.StringTracks[i];
            _rows.Add(new TrackRow
            {
                Kind = TrackRowKind.String,
                RelativePath = t.RelativePath,
                ComponentType = t.ComponentType,
                PropertyPath = t.PropertyPath,
                Label = FormatRowLabel(t.RelativePath, t.ComponentType, t.PropertyPath),
                AccentColor = EditorTheme.Amber400,
                StringTrack = t,
            });
        }

        if (_selectedRowIndex >= _rows.Count)
        {
            _selectedRowIndex = -1;
            _selectedKeyIndex = -1;
        }
    }

    private static List<float> ComputeGroupSummaryTimes(List<SceneCurveTrack> tracks)
    {
        var times = new List<float>();
        for (int i = 0; i < tracks.Count; i++)
        {
            AnimationCurve c = tracks[i].Curve;
            for (int k = 0; k < c.Count; k++)
            {
                float t = c[k].Time;
                bool exists = false;
                for (int j = 0; j < times.Count; j++)
                {
                    if (MathF.Abs(times[j] - t) <= 0.0005f)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists) times.Add(t);
            }
        }
        times.Sort();
        return times;
    }

    private static bool IsRowMuted(TrackRow row)
    {
        if (row.Kind == TrackRowKind.Group && row.GroupTracks is { Count: > 0 } gt)
        {
            for (int i = 0; i < gt.Count; i++)
                if (!gt[i].Muted) return false;
            return true;
        }
        if (row.CurveTrack != null) return row.CurveTrack.Muted;
        if (row.ObjectTrack != null) return row.ObjectTrack.Muted;
        if (row.StringTrack != null) return row.StringTrack.Muted;
        return false;
    }

    private static bool IsRowSolo(TrackRow row)
    {
        if (row.Kind == TrackRowKind.Group && row.GroupTracks is { Count: > 0 } gt)
        {
            for (int i = 0; i < gt.Count; i++)
                if (!gt[i].Solo) return false;
            return true;
        }
        if (row.CurveTrack != null) return row.CurveTrack.Solo;
        if (row.ObjectTrack != null) return row.ObjectTrack.Solo;
        if (row.StringTrack != null) return row.StringTrack.Solo;
        return false;
    }

    private static bool IsRowLocked(TrackRow row)
    {
        if (row.Kind == TrackRowKind.Group && row.GroupTracks is { Count: > 0 } gt)
        {
            for (int i = 0; i < gt.Count; i++)
                if (gt[i].Locked) return true;
            return false;
        }
        if (row.CurveTrack != null) return row.CurveTrack.Locked;
        if (row.ObjectTrack != null) return row.ObjectTrack.Locked;
        if (row.StringTrack != null) return row.StringTrack.Locked;
        return false;
    }

    private static bool IsRowAdditive(TrackRow row)
    {
        if (row.Kind == TrackRowKind.Group && row.GroupTracks is { Count: > 0 } gt)
        {
            for (int i = 0; i < gt.Count; i++)
                if (gt[i].BlendMode != SceneTrackBlendMode.Additive) return false;
            return true;
        }
        return row.CurveTrack is { BlendMode: SceneTrackBlendMode.Additive };
    }

    private void ToggleRowMute(TrackRow row)
    {
        if (!IsClipValid) return;
        Undo.RecordObject(ClipAsset!, "Toggle Track Mute");
        bool next = !IsRowMuted(row);
        if (row.Kind == TrackRowKind.Group && row.GroupTracks != null)
            foreach (SceneCurveTrack t in row.GroupTracks) t.Muted = next;
        else if (row.CurveTrack != null) row.CurveTrack.Muted = next;
        else if (row.ObjectTrack != null) row.ObjectTrack.Muted = next;
        else if (row.StringTrack != null) row.StringTrack.Muted = next;
        _clip!.Invalidate();
        EditorUtility.SetDirty(ClipAsset!);
        if (_previewing) SampleAtPlayhead();
    }

    private void ToggleRowSolo(TrackRow row)
    {
        if (!IsClipValid) return;
        bool next = !IsRowSolo(row);
        if (row.Kind == TrackRowKind.Group && row.GroupTracks != null)
            foreach (SceneCurveTrack t in row.GroupTracks) t.Solo = next;
        else if (row.CurveTrack != null) row.CurveTrack.Solo = next;
        else if (row.ObjectTrack != null) row.ObjectTrack.Solo = next;
        else if (row.StringTrack != null) row.StringTrack.Solo = next;
        if (_previewing) SampleAtPlayhead();
    }

    private void ToggleRowLock(TrackRow row)
    {
        if (!IsClipValid) return;
        Undo.RecordObject(ClipAsset!, "Toggle Track Lock");
        bool next = !IsRowLocked(row);
        if (row.Kind == TrackRowKind.Group && row.GroupTracks != null)
            foreach (SceneCurveTrack t in row.GroupTracks) t.Locked = next;
        else if (row.CurveTrack != null) row.CurveTrack.Locked = next;
        else if (row.ObjectTrack != null) row.ObjectTrack.Locked = next;
        else if (row.StringTrack != null) row.StringTrack.Locked = next;
        EditorUtility.SetDirty(ClipAsset!);
    }

    private void ToggleRowAdditive(TrackRow row)
    {
        if (!IsClipValid) return;
        Undo.RecordObject(ClipAsset!, "Toggle Track Blend Mode");
        SceneTrackBlendMode next = IsRowAdditive(row) ? SceneTrackBlendMode.Override : SceneTrackBlendMode.Additive;
        if (row.Kind == TrackRowKind.Group && row.GroupTracks != null)
            foreach (SceneCurveTrack t in row.GroupTracks) t.BlendMode = next;
        else if (row.CurveTrack != null) row.CurveTrack.BlendMode = next;
        _clip!.Invalidate();
        EditorUtility.SetDirty(ClipAsset!);
        if (_previewing) SampleAtPlayhead();
    }

    private static string FormatRowLabel(string relPath, string compType, string propPath)
    {
        string prefix = string.IsNullOrEmpty(relPath) ? "" : $"{relPath} : ";
        return $"{prefix}{compType}.{propPath}";
    }

    private static Color ChannelColor(string propPath, int index)
    {
        if (propPath.EndsWith(".X", StringComparison.OrdinalIgnoreCase) || propPath.EndsWith(".R", StringComparison.OrdinalIgnoreCase))
            return EditorTheme.Red400;
        if (propPath.EndsWith(".Y", StringComparison.OrdinalIgnoreCase) || propPath.EndsWith(".G", StringComparison.OrdinalIgnoreCase))
            return EditorTheme.Green400;
        if (propPath.EndsWith(".Z", StringComparison.OrdinalIgnoreCase) || propPath.EndsWith(".B", StringComparison.OrdinalIgnoreCase))
            return EditorTheme.Blue400;
        if (propPath.EndsWith(".W", StringComparison.OrdinalIgnoreCase) || propPath.EndsWith(".A", StringComparison.OrdinalIgnoreCase))
            return EditorTheme.Amber400;

        return (index % 4) switch
        {
            0 => EditorTheme.Green400,
            1 => EditorTheme.Blue400,
            2 => EditorTheme.Amber400,
            _ => EditorTheme.Purple400,
        };
    }

    // ================================================================
    //  Top Transport & Clip Toolbar
    // ================================================================

    private void DrawTopTransportBar(Paper paper, Scribe.FontFile font, float width)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        float fps = IsClipValid ? MathF.Max(1f, _clip!.FrameRate) : 60f;
        int currentFrame = (int)MathF.Round(_currentTime * fps);

        using (paper.Row("anim_top").Width(width).Height(HeaderBarHeight)
            .Padding(8, 8, 0, 0).Gap(6).AlignItems(LayoutAlignment.Center)
            .BackgroundColor(_recording ? EditorTheme.WithAlpha(EditorTheme.Red400, 28) : EditorTheme.Neutral200)
            .Enter())
        {
            // Preview toggle button
            EditorGUI.Chip(paper, "anim_btn_preview", "Preview", _previewing, () =>
            {
                if (_previewing) StopPreviewAndRestore();
                else { EnterPreviewMode(); SampleAtPlayhead(); }
            }, height: 22f);

            // Record toggle button (red circle + Record)
            bool canRecord = IsClipValid && _targetRoot.IsValid() && !Application.IsPlaying;
            using (paper.Row("anim_btn_rec").Width(UnitValue.Auto).Height(22).Padding(8, 8, 0, 0).Gap(5)
                .Rounded(m.SmallRounding)
                .BackgroundColor(_recording ? EditorTheme.Red400 : EditorTheme.Neutral300)
                .Hovered.BackgroundColor(_recording ? EditorTheme.Red500 : EditorTheme.Neutral400).End()
                .Cursor(canRecord ? PaperCursor.Pointer : PaperCursor.Default)
                .Tooltip("Toggle Auto-Key Recording mode: any change made to the target hierarchy in the Inspector or Scene View records a keyframe at the playhead")
                .OnClick(0, (_, _) =>
                {
                    if (!canRecord) return;
                    _recording = !_recording;
                    if (_recording && !_previewing)
                    {
                        EnterPreviewMode();
                        SampleAtPlayhead();
                    }
                }).Enter())
            {
                paper.Box("anim_rec_dot").Width(8).Height(8).Rounded(4)
                    .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .BackgroundColor(_recording ? Color.White : EditorTheme.Red400).IsNotInteractable();
                paper.Box("anim_rec_lbl").Width(UnitValue.Auto).Height(22).IsNotInteractable()
                    .Text("Record", font).TextColor(_recording ? Color.White : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            }

            paper.Box("anim_tdiv1").Width(1).Height(18).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            // Transport controls: First Frame, Prev Frame, Play/Pause, Next Frame, Last Frame
            EditorGUI.ToolbarIconBtn(paper, "anim_first", EditorIcons.BackwardStep, "First Frame", false, () =>
            {
                _playing = false;
                SetPlayhead(0f);
            });
            EditorGUI.ToolbarIconBtn(paper, "anim_prev", EditorIcons.CaretLeft, "Previous Frame", false, () =>
            {
                _playing = false;
                SetPlayhead(MathF.Max(0f, _currentTime - 1f / fps));
            });
            EditorGUI.ToolbarIconBtn(paper, "anim_play", _playing ? EditorIcons.Pause : EditorIcons.Play, _playing ? "Pause" : "Play", _playing, () =>
            {
                if (!IsClipValid) return;
                _playing = !_playing;
                if (_playing && !_previewing && !Application.IsPlaying)
                    EnterPreviewMode();
            });
            EditorGUI.ToolbarIconBtn(paper, "anim_next", EditorIcons.CaretRight, "Next Frame", false, () =>
            {
                _playing = false;
                SetPlayhead(_currentTime + 1f / fps);
            });
            EditorGUI.ToolbarIconBtn(paper, "anim_last", EditorIcons.ForwardStep, "Last Frame", false, () =>
            {
                _playing = false;
                if (IsClipValid) SetPlayhead(_clip!.Duration);
            });

            // Frame / Time readout
            paper.Box("anim_time_readout").Width(96).Height(22).Padding(6, 6, 0, 0)
                .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.Neutral300)
                .Text($"F {currentFrame}  ({_currentTime:0.00}s)", EditorTheme.FontMono ?? font)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter).IsNotInteractable();

            paper.Box("anim_tdiv2").Width(1).Height(18).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            // Clip selector dropdown + New Clip button
            DrawClipPickerInToolbar(paper, font);

            if (IsClipValid)
            {
                // Add Keyframe at Playhead button
                EditorGUI.ToolbarIconBtn(paper, "anim_add_key", EditorIcons.Diamond, "Key Selected / All Tracks at Playhead", false, KeyAtPlayhead);

                // Add Instant Animation Event at Playhead button
                EditorGUI.ToolbarIconBtn(paper, "anim_add_ev", EditorIcons.Bookmark, "Add Instant Animation Event at Playhead", false, () => AddEventAtPlayhead(ranged: false));

                // Add Ranged (Start/End) Animation Event at Playhead button
                EditorGUI.ToolbarIconBtn(paper, "anim_add_rev", EditorIcons.ArrowsLeftRight, "Add Ranged (Start/End) Animation Event at Playhead", false, () => AddEventAtPlayhead(ranged: true));

                // Unpack Rig Curves button when editing an imported skeletal/humanoid AnimationClip
                if (_clip is AnimationClip { HasMotionPayload: true } importedClip)
                {
                    Origami.Button(paper, "anim_unpack_rig", $"{EditorIcons.Sliders} Unpack Rig Curves", () =>
                    {
                        Undo.RecordObject(importedClip, "Unpack Rig Curves");
                        importedClip.UnpackMotionToCurveTracks(_targetRoot);
                        EditorUtility.SetDirty(importedClip);
                    }).Small().Height(22).Show();
                }
            }

            paper.Box("anim_top_sp").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();

            if (IsClipValid && EditorUtility.IsDirty(ClipAsset!))
            {
                Origami.Button(paper, "anim_save_clip", $"{EditorIcons.FloppyDisk} Save", () =>
                {
                    if (EditorAssetBackend.Instance is { } db && db.SaveAsset(ClipAsset!))
                        EditorUtility.ClearAssetDirty(ClipAsset!.AssetID);
                }).Primary().Small().Height(22).Show();
            }
        }
    }

    private void DrawClipPickerInToolbar(Paper paper, Scribe.FontFile font)
    {
        List<AnimationClip> available = GetAvailableClips();
        if (IsClipValid && !available.Contains(_clip!))
            available.Insert(0, _clip!);

        if (available.Count > 0)
        {
            string[] names = [.. available.Select(c => c.Name)];
            int currentIdx = IsClipValid ? available.IndexOf(_clip!) : 0;
            using (paper.Box("anim_clip_dd_w").Width(150).Height(24).Enter())
            {
                Origami.Dropdown(paper, "anim_clip_dd", Math.Max(0, currentIdx), idx =>
                {
                    if (idx >= 0 && idx < available.Count)
                        SelectClip(available[idx]);
                }, names).Show();
            }
        }
        else
        {
            paper.Box("anim_no_clip_lbl").Width(120).Height(22).IsNotInteractable()
                .Text(IsClipValid ? _clip!.Name : "(No Clip)", font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
        }

        Origami.Button(paper, "anim_pick_clip", "Pick", () =>
        {
            SelectorModal.Open("Select Animation Clip", typeof(AnimationClip), SelectorTabs.Assets, picked =>
            {
                if (picked is AnimationClip ac && ac.IsValid())
                {
                    if (_animator.IsValid())
                    {
                        Undo.RecordObject(_animator!, "Assign Animation Clip");
                        _animator!.AddClip(ac);
                    }
                    SelectClip(ac);
                }
            });
        }).Small().Height(22).Show();

        Origami.Button(paper, "anim_new_clip", "+ New Clip", CreateNewClipForTarget).Small().Height(22).Show();
    }

    // ================================================================
    //  Empty / Setup State (Unity-style "Create Animator & AnimationClip")
    // ================================================================

    private void DrawEmptyOrSetupState(Paper paper, Scribe.FontFile font, float width, float height)
    {
        using (paper.Column("anim_empty_col").Size(width, height).AlignItems(LayoutAlignment.Center).Enter())
        {
            paper.Box("anim_emp_top_sp").Height(UnitValue.StretchOne).IsNotInteractable();

            if (_targetRoot.IsNotValid())
            {
                paper.Box("anim_emp_msg").Width(440).Height(32).IsNotInteractable()
                    .Text("Select a GameObject in the scene or an Animation Clip asset in the Project to begin animating.", font)
                    .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
            }
            else
            {
                paper.Box("anim_emp_msg").Width(480).Height(32).IsNotInteractable()
                    .Text($"To begin animating '{_targetRoot!.Name}', create an Animator and an Animation Clip.", font)
                    .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);

                paper.Box("anim_emp_gap").Height(10).IsNotInteractable();

                Origami.Button(paper, "anim_emp_create", $"{EditorIcons.Plus}  Create Animation Clip for '{_targetRoot.Name}'", CreateNewClipForTarget)
                    .Primary().Width(280).Height(34).Show();
            }

            paper.Box("anim_emp_bot_sp").Height(UnitValue.StretchOne).IsNotInteractable();
        }
    }

    private void CreateNewClipForTarget()
    {
        string baseName = _targetRoot.IsValid() ? $"{_targetRoot!.Name}_Anim" : "New Animation Clip";
        var newClip = new AnimationClip
        {
            Name = baseName,
            FrameRate = 60f,
            WrapMode = SceneClipWrapMode.Loop,
            Loop = true,
        };

        if (Project.Current != null && EditorAssetBackend.Instance is { } db)
        {
            string folder = AssetCreateMenu.GetCurrentFolder();
            string absFolder = AssetCreateMenu.GetAbsoluteFolder(folder);
            if (Directory.Exists(absFolder))
            {
                string fileName = AssetCreateMenu.FindUniqueName(absFolder, baseName, ".animclip");
                string relPath = string.IsNullOrEmpty(folder) ? fileName : $"{folder}/{fileName}";
                db.CreateAsset(newClip, relPath);
            }
        }

        if (_targetRoot.IsValid())
        {
            if (_animator.IsNotValid())
                _animator = Undo.AddComponent<Animator>(_targetRoot!);

            if (_animator.IsValid())
            {
                Undo.RecordObject(_animator!, "Assign Animation Clip");
                _animator!.AddClip(newClip);
            }
        }

        SelectClip(newClip);
    }

    // ================================================================
    //  Left Pane: Track List & Add Property Button
    // ================================================================

    private void DrawLeftTrackList(Paper paper, Scribe.FontFile font, float width, float height)
    {
        OrigamiMetrics m = Origami.Current.Metrics;

        using (paper.Column("anim_left").Width(width).Height(height).Enter())
        {
            // Header aligned with right time ruler + event bar
            float topHeaderH = TimeRulerHeight + EventBarHeight;
            using (paper.Row("anim_left_hdr").Width(width).Height(topHeaderH)
                .Padding(8, 8, 0, 0).Gap(6).AlignItems(LayoutAlignment.Center)
                .BackgroundColor(EditorTheme.Neutral200).Enter())
            {
                string targetText = _targetRoot.IsValid() ? _targetRoot!.Name : "(No Target GameObject)";
                paper.Box("anim_target_lbl").Width(UnitValue.Stretch()).Height(topHeaderH).IsNotInteractable()
                    .Text(targetText, EditorTheme.FontSemiBold ?? font)
                    .TextColor(_targetRoot.IsValid() ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                ElementBuilder addBtn = paper.Box("anim_add_prop_btn")
                    .Width(104).Height(24).Rounded(m.SmallRounding)
                    .BackgroundColor(EditorTheme.Accent)
                    .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                    .Text($"{EditorIcons.Plus} Add Property", EditorTheme.FontSemiBold ?? font)
                    .TextColor(Color.White).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleCenter)
                    .Cursor(PaperCursor.Pointer)
                    .OnClick(0, (_, _) => ToggleAddPropertyPopup());

                using (addBtn.Enter())
                {
                    if (_addPropertyOpen)
                    {
                        MenuTreePopup.Backdrop(paper, "anim_add_prop", () => _addPropertyOpen = false);
                        MenuTreePopup.Popover(
                            paper,
                            "anim_add_prop",
                            paper.CurrentParent,
                            _addPropertyEntries,
                            _addPropertyTreeState,
                            OnPickPropertyToAdd,
                            "Search hierarchy properties...",
                            "No animatable properties found.",
                            minWidth: 320f);
                    }
                }
            }

            EditorGUI.Divider(paper, "anim_left_hdiv");

            float listH = MathF.Max(20f, height - topHeaderH - 1f);
            Origami.ScrollView(paper, "anim_left_scroll", width, listH).Body(() =>
            {
                if (_rows.Count == 0)
                {
                    EditorGUI.EmptyState(paper, "anim_no_tracks", "Click '+ Add Property' or enable Record to animate properties.", font);
                    return;
                }

                for (int i = 0; i < _rows.Count; i++)
                    DrawTrackListRow(paper, font, _rows[i], i, width);
            });
        }
    }

    private void DrawTrackListRow(Paper paper, Scribe.FontFile font, TrackRow row, int index, float width)
    {
        bool selected = index == _selectedRowIndex;
        bool muted = IsRowMuted(row);
        bool solo = IsRowSolo(row);
        bool locked = IsRowLocked(row);
        bool additive = IsRowAdditive(row);
        OrigamiMetrics m = Origami.Current.Metrics;

        using (paper.Row($"anim_trow_{index}").Width(width).Height(TrackRowHeight)
            .Padding(8 + row.Indent * 14f, 6, 0, 0).Gap(4).AlignItems(LayoutAlignment.Center)
            .BackgroundColor(selected ? EditorTheme.Selected : (index % 2 == 0 ? Color.Transparent : EditorTheme.WithAlpha(EditorTheme.Neutral300, 70)))
            .Hovered.BackgroundColor(selected ? EditorTheme.Selected : EditorTheme.Hover).End()
            .OnClick(index, (idx, _) =>
            {
                _selectedRowIndex = idx;
                _selectedKeyIndex = -1;
                _selectedEventIndex = -1;
                _curveBoundsInitialized = false;
            })
            .OnRightClick(index, (idx, _) =>
            {
                _selectedRowIndex = idx;
                TrackRow r = _rows[idx];
                Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
                {
                    if (!IsRowLocked(r))
                        menu.Item("Add Key at Playhead", () => AddKeyToRowAt(r, _currentTime));
                    if (r.Kind is TrackRowKind.Group or TrackRowKind.Curve)
                        menu.Item(IsRowAdditive(r) ? "Blend Mode: Switch to Override" : "Blend Mode: Switch to Additive", () => ToggleRowAdditive(r));
                    menu.Item(IsRowMuted(r) ? "Unmute Track" : "Mute Track", () => ToggleRowMute(r));
                    menu.Item(IsRowSolo(r) ? "Unsolo Track" : "Solo Track", () => ToggleRowSolo(r));
                    menu.Item(IsRowLocked(r) ? "Unlock Track" : "Lock Track", () => ToggleRowLock(r));
                    menu.Separator();
                    menu.Item("Remove Property Track", () => RemoveRowTrack(r), danger: true);
                });
            })
            .Enter())
        {
            if (row.Kind == TrackRowKind.Group)
            {
                bool collapsed = _collapsedGroups.Contains(row.GroupKey);
                paper.Box($"anim_trow_{index}_caret").Width(14).Height(18).Rounded(3)
                    .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                    .Text(collapsed ? EditorIcons.CaretRight : EditorIcons.CaretDown, font)
                    .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall * 0.9f)
                    .Alignment(TextAlignment.MiddleCenter)
                    .StopEventPropagation()
                    .OnClick(row.GroupKey, (gk, _) =>
                    {
                        if (!_collapsedGroups.Add(gk))
                            _collapsedGroups.Remove(gk);
                    });
            }
            else
            {
                // Channel color indicator dot
                paper.Box($"anim_trow_{index}_dot").Width(7).Height(7).Rounded(4)
                    .BackgroundColor(row.AccentColor).IsNotInteractable();
            }

            // Property name
            paper.Box($"anim_trow_{index}_lbl").Width(UnitValue.Stretch()).Height(TrackRowHeight).IsNotInteractable()
                .Text(row.Label, row.Kind == TrackRowKind.Group ? (EditorTheme.FontSemiBold ?? font) : font)
                .TextColor(muted ? EditorTheme.Ink300 : (selected ? EditorTheme.Ink600 : EditorTheme.Ink500))
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            // Live value editor / readout at current time
            if (!locked)
                DrawTrackRowInlineValue(paper, font, row, index);

            // Additive / Override pill on curve and group rows
            if (row.Kind is TrackRowKind.Group or TrackRowKind.Curve)
            {
                DrawMicroRowToggle(paper, font, $"anim_trow_{index}_add", additive ? "ADD" : "OVR", additive,
                    EditorTheme.Purple400, "Toggle Additive vs Override blend mode", () => ToggleRowAdditive(row), width: 26f);
            }

            // Mute (M), Solo (S), Lock (L) Sequencer toggles
            DrawMicroRowToggle(paper, font, $"anim_trow_{index}_m", "M", muted, EditorTheme.Red400, "Mute track", () => ToggleRowMute(row));
            DrawMicroRowToggle(paper, font, $"anim_trow_{index}_s", "S", solo, EditorTheme.Amber400, "Solo track", () => ToggleRowSolo(row));
            DrawMicroRowToggle(paper, font, $"anim_trow_{index}_l", "L", locked, EditorTheme.Blue400, "Lock track against edits", () => ToggleRowLock(row));

            // Delete track button
            paper.Box($"anim_trow_{index}_del").Width(16).Height(16).Rounded(m.SmallRounding)
                .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall * 0.85f).Alignment(TextAlignment.MiddleCenter)
                .Tooltip("Remove track")
                .StopEventPropagation()
                .OnClick(index, (idx, _) => RemoveRowTrack(_rows[idx]));
        }
    }

    private static void DrawMicroRowToggle(Paper paper, Scribe.FontFile font, string id, string text, bool active, Color activeColor, string tooltip, Action onClick, float width = 16f)
    {
        paper.Box(id).Width(width).Height(16).Rounded(3)
            .BackgroundColor(active ? EditorTheme.WithAlpha(activeColor, 190) : EditorTheme.Neutral300)
            .Hovered.BackgroundColor(active ? activeColor : EditorTheme.Neutral400).End()
            .Text(text, EditorTheme.FontMono ?? font)
            .TextColor(active ? Color.White : EditorTheme.Ink300)
            .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
            .Tooltip(tooltip)
            .StopEventPropagation()
            .OnClick(0, (_, _) => onClick());
    }

    private void DrawTrackRowInlineValue(Paper paper, Scribe.FontFile font, TrackRow row, int index)
    {
        if (!IsClipValid) return;

        if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null)
        {
            float val = row.CurveTrack.Curve.Count > 0 ? row.CurveTrack.Curve.Evaluate(_currentTime) : 0f;
            if (_targetRoot.IsValid() && SceneClipBinding.TryReadScalar(_targetRoot!, row.RelativePath, row.ComponentType, row.PropertyPath, out float live))
                val = live;

            using (paper.Box($"anim_trow_{index}_vw").Width(62).Height(20).StopEventPropagation().Enter())
            {
                Origami.NumericField<float>(paper, $"anim_trow_{index}_v", val, newVal =>
                {
                    Undo.RecordObject(ClipAsset!, "Edit Keyframe Value");
                    _clip!.SetCurveKey(row.RelativePath, row.ComponentType, row.PropertyPath, _currentTime, newVal);
                    EditorUtility.SetDirty(ClipAsset!);
                    if (_targetRoot.IsValid())
                    {
                        if (!_previewing) EnterPreviewMode();
                        SampleAtPlayhead();
                    }
                }).Format("0.##").Height(20).Show();
            }
        }
        else if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null)
        {
            EngineObject? val = row.ObjectTrack.Evaluate(_currentTime);
            string text = val.IsValid() ? val!.Name : "None";
            paper.Box($"anim_trow_{index}_ov").Width(62).Height(18).Padding(4, 4, 0, 0)
                .Rounded(3).BackgroundColor(EditorTheme.Neutral300)
                .Text(text, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall * 0.9f)
                .Alignment(TextAlignment.MiddleLeft).TextTruncate()
                .StopEventPropagation()
                .OnClick(index, (idx, _) => OpenObjectKeyPicker(_rows[idx], _currentTime));
        }
        else if (row.Kind == TrackRowKind.String && row.StringTrack != null)
        {
            string val = row.StringTrack.Evaluate(_currentTime);
            if (_targetRoot.IsValid() && SceneClipBinding.TryReadString(_targetRoot!, row.RelativePath, row.ComponentType, row.PropertyPath, out string live))
                val = live;

            using (paper.Box($"anim_trow_{index}_sw").Width(62).Height(20).StopEventPropagation().Enter())
            {
                Origami.TextField(paper, $"anim_trow_{index}_s", val, newVal =>
                {
                    Undo.RecordObject(ClipAsset!, "Edit String Keyframe");
                    _clip!.SetStringKey(row.RelativePath, row.ComponentType, row.PropertyPath, _currentTime, newVal);
                    EditorUtility.SetDirty(ClipAsset!);
                    if (_targetRoot.IsValid())
                    {
                        if (!_previewing) EnterPreviewMode();
                        SampleAtPlayhead();
                    }
                }).Height(20).CommitOnEnter().Show();
            }
        }
    }

    private void OpenObjectKeyPicker(TrackRow row, float time)
    {
        if (!IsClipValid || row.ObjectTrack == null) return;
        Type targetType = typeof(EngineObject);
        if (_targetRoot.IsValid() && SceneClipBinding.TryReadRawProperty(_targetRoot!, row.RelativePath, row.ComponentType, row.PropertyPath, out _, out Type? mt) && mt != null)
            targetType = mt;

        SelectorModal.Open($"Select {targetType.Name}", targetType, SelectorTabs.Assets, picked =>
        {
            if (!IsClipValid) return;
            Undo.RecordObject(ClipAsset!, "Set Object Keyframe");
            _clip!.SetObjectKey(row.RelativePath, row.ComponentType, row.PropertyPath, time, picked as EngineObject);
            EditorUtility.SetDirty(ClipAsset!);
            if (_targetRoot.IsValid())
            {
                if (!_previewing) EnterPreviewMode();
                SampleAtPlayhead();
            }
        });
    }

    private void ToggleAddPropertyPopup()
    {
        if (_addPropertyOpen)
        {
            _addPropertyOpen = false;
            return;
        }

        _addPropertyTreeState.Reset();
        _addPropertyEntries = BuildAddPropertyEntries();
        _addPropertyOpen = true;
    }

    private readonly record struct AddPropertyPayload(string RelativePath, AnimatablePropertyDescriptor Descriptor);

    private List<MenuTreeEntry> BuildAddPropertyEntries()
    {
        var entries = new List<MenuTreeEntry>();
        if (_targetRoot.IsNotValid()) return entries;

        CollectHierarchyProperties(_targetRoot!, _targetRoot!, entries);
        return entries;
    }

    private static void CollectHierarchyProperties(GameObject root, GameObject current, List<MenuTreeEntry> into)
    {
        if (current.IsNotValid()) return;

        string relPath = Transform.GetRelativePath(current.Transform, root.Transform);
        string folderPrefix = string.IsNullOrEmpty(relPath) ? "" : $"{relPath}/";

        List<AnimatablePropertyDescriptor> props = SceneClipBinding.GetAnimatableProperties(current);
        foreach (AnimatablePropertyDescriptor prop in props)
        {
            string menuPath = $"{folderPrefix}{prop.ComponentType}/{prop.PropertyPath}";
            string icon = prop.IsObjectReference ? EditorIcons.Cube : EditorIcons.Sliders;
            into.Add(MenuTreeEntry.FromPath(menuPath, icon, new AddPropertyPayload(relPath, prop)));
        }

        for (int i = 0; i < current.Children.Count; i++)
            CollectHierarchyProperties(root, current.Children[i], into);
    }

    private void OnPickPropertyToAdd(MenuTreeEntry entry)
    {
        _addPropertyOpen = false;
        if (!IsClipValid || entry.Tag is not AddPropertyPayload payload) return;

        string relPath = payload.RelativePath;
        AnimatablePropertyDescriptor desc = payload.Descriptor;

        Undo.RecordObject(ClipAsset!, $"Add Track {desc.DisplayName}");
        CacheBaselineProperty(relPath, desc.ComponentType, desc.PropertyPath);

        float dur = MathF.Max(1f, _clip!.Duration);

        if (desc.IsObjectReference)
        {
            EngineObject? initial = null;
            if (_targetRoot.IsValid())
                SceneClipBinding.TryReadObject(_targetRoot!, relPath, desc.ComponentType, desc.PropertyPath, out initial);
            _clip.SetObjectKey(relPath, desc.ComponentType, desc.PropertyPath, 0f, initial);
        }
        else if (desc.IsString)
        {
            string initial = "";
            if (_targetRoot.IsValid())
                SceneClipBinding.TryReadString(_targetRoot!, relPath, desc.ComponentType, desc.PropertyPath, out initial);
            _clip.SetStringKey(relPath, desc.ComponentType, desc.PropertyPath, 0f, initial);
        }
        else if (desc.SubChannels.Length > 0)
        {
            foreach (string sub in desc.SubChannels)
            {
                string subPath = $"{desc.PropertyPath}.{sub}";
                float initial = 0f;
                if (_targetRoot.IsValid())
                    SceneClipBinding.TryReadScalar(_targetRoot!, relPath, desc.ComponentType, subPath, out initial);

                SceneCurveTrack track = _clip.GetOrCreateCurveTrack(relPath, desc.ComponentType, subPath);
                if (track.Curve.Count == 0)
                {
                    track.Curve.AddKey(new Keyframe(0f, initial));
                    track.Curve.AddKey(new Keyframe(dur, initial));
                }
            }
        }
        else
        {
            float initial = 0f;
            if (_targetRoot.IsValid())
                SceneClipBinding.TryReadScalar(_targetRoot!, relPath, desc.ComponentType, desc.PropertyPath, out initial);

            SceneCurveTrack track = _clip.GetOrCreateCurveTrack(relPath, desc.ComponentType, desc.PropertyPath);
            if (track.Curve.Count == 0)
            {
                track.Curve.AddKey(new Keyframe(0f, initial));
                track.Curve.AddKey(new Keyframe(dur, initial));
            }
        }

        _clip.Invalidate();
        EditorUtility.SetDirty(ClipAsset!);
    }

    private void AddKeyToRowAt(TrackRow row, float time)
    {
        if (!IsClipValid || IsRowLocked(row)) return;
        Undo.RecordObject(ClipAsset!, "Add Keyframe");

        if (row.Kind == TrackRowKind.Group && row.GroupTracks != null)
        {
            foreach (SceneCurveTrack ct in row.GroupTracks)
            {
                if (ct.Locked) continue;
                float v = ct.Curve.Count > 0 ? ct.Curve.Evaluate(time) : 0f;
                if (_targetRoot.IsValid() && SceneClipBinding.TryReadScalar(_targetRoot!, ct.RelativePath, ct.ComponentType, ct.PropertyPath, out float live))
                    v = live;
                _clip!.SetCurveKey(ct.RelativePath, ct.ComponentType, ct.PropertyPath, time, v);
            }
        }
        else if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null)
        {
            float v = row.CurveTrack.Curve.Count > 0 ? row.CurveTrack.Curve.Evaluate(time) : 0f;
            if (_targetRoot.IsValid() && SceneClipBinding.TryReadScalar(_targetRoot!, row.RelativePath, row.ComponentType, row.PropertyPath, out float live))
                v = live;
            _clip!.SetCurveKey(row.RelativePath, row.ComponentType, row.PropertyPath, time, v);
        }
        else if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null)
        {
            EngineObject? v = row.ObjectTrack.Evaluate(time);
            if (_targetRoot.IsValid() && SceneClipBinding.TryReadObject(_targetRoot!, row.RelativePath, row.ComponentType, row.PropertyPath, out EngineObject? live))
                v = live;
            _clip!.SetObjectKey(row.RelativePath, row.ComponentType, row.PropertyPath, time, v);
        }
        else if (row.Kind == TrackRowKind.String && row.StringTrack != null)
        {
            string v = row.StringTrack.Evaluate(time);
            if (_targetRoot.IsValid() && SceneClipBinding.TryReadString(_targetRoot!, row.RelativePath, row.ComponentType, row.PropertyPath, out string live))
                v = live;
            _clip!.SetStringKey(row.RelativePath, row.ComponentType, row.PropertyPath, time, v);
        }

        EditorUtility.SetDirty(ClipAsset!);
    }

    private void RemoveRowTrack(TrackRow row)
    {
        if (!IsClipValid) return;
        Undo.RecordObject(ClipAsset!, "Remove Track");
        if (row.Kind == TrackRowKind.Group && row.GroupTracks != null)
        {
            foreach (SceneCurveTrack ct in row.GroupTracks)
                _clip!.RemoveCurveTrack(ct);
        }
        if (row.CurveTrack != null) _clip!.RemoveCurveTrack(row.CurveTrack);
        if (row.ObjectTrack != null) _clip!.RemoveObjectTrack(row.ObjectTrack);
        if (row.StringTrack != null) _clip!.RemoveStringTrack(row.StringTrack);
        EditorUtility.SetDirty(ClipAsset!);
    }

    private void KeyAtPlayhead()
    {
        if (!IsClipValid) return;
        if (_selectedRowIndex >= 0 && _selectedRowIndex < _rows.Count)
        {
            AddKeyToRowAt(_rows[_selectedRowIndex], _currentTime);
            return;
        }

        for (int i = 0; i < _rows.Count; i++)
            AddKeyToRowAt(_rows[i], _currentTime);
    }

    private void AddEventAtPlayhead(bool ranged = false)
    {
        if (!IsClipValid) return;
        Undo.RecordObject(ClipAsset!, ranged ? "Add Ranged Animation Event" : "Add Animation Event");
        float defaultLength = ranged ? MathF.Max(0.25f, _clip!.Duration > 0f ? _clip.Duration * 0.25f : 0.25f) : 0f;
        _clip!.PropertyEvents.Add(new SceneAnimationEvent
        {
            Time = _currentTime,
            EndTime = ranged ? _currentTime + defaultLength : _currentTime,
            FunctionName = ranged ? "OnEventEnter" : "OnAnimationEvent",
            ExitFunctionName = ranged ? "OnEventExit" : string.Empty,
        });
        _selectedEventIndex = _clip.PropertyEvents.Count - 1;
        _selectedKeyIndex = -1;
        _clip.Invalidate();
        EditorUtility.SetDirty(ClipAsset!);
    }

    // ================================================================
    //  Right Pane: Time Ruler, Event Lane, Dope Sheet & Curve Canvas
    // ================================================================

    private void DrawRightTimelineArea(Paper paper, Scribe.FontFile font, float width, float height)
    {
        float rangeT = MathF.Max(0.05f, _viewMaxTime - _viewMinTime);

        float TimeFromLocalX(float x) => MathF.Max(0f, _viewMinTime + (x / MathF.Max(1f, _timelineWidth)) * rangeT);
        float LocalXFromTime(float t) => (t - _viewMinTime) / rangeT * _timelineWidth;

        ProcessUnrealCanvasNavigationAndShortcuts(paper, TimeFromLocalX, LocalXFromTime);

        using (paper.Column("anim_right").Width(width).Height(height).Clip().Enter())
        {
            // 1. Time Ruler (LMB scrubbing, In/Out working range handles, RMB/MMB pan & scroll zoom)
            ElementBuilder ruler = paper.Box("anim_ruler").Width(width).Height(TimeRulerHeight)
                .BackgroundColor(EditorTheme.Neutral200)
                .OnPostLayout((_, rect) =>
                {
                    _rulerScreenRect = rect;
                    _timelineWidth = MathF.Max(1f, (float)rect.Size.X);
                })
                .OnPress(e =>
                {
                    _playing = false;
                    if (_useWorkRange)
                    {
                        float inX = LocalXFromTime(_workRangeIn);
                        float outX = LocalXFromTime(_workRangeOut);
                        if (MathF.Abs(e.RelativePosition.X - inX) <= 6f)
                        {
                            _dragPart = DragPart.WorkRangeIn;
                            return;
                        }
                        if (MathF.Abs(e.RelativePosition.X - outX) <= 6f)
                        {
                            _dragPart = DragPart.WorkRangeOut;
                            return;
                        }
                    }
                    _dragPart = DragPart.Playhead;
                    SetPlayhead(TimeFromLocalX(e.RelativePosition.X), snapToFrame: !IsAltDown(paper));
                })
                .OnDragging(e =>
                {
                    float t = TimeFromLocalX(e.RelativePosition.X);
                    float fps = IsClipValid ? MathF.Max(1f, _clip!.FrameRate) : 60f;
                    bool freeSnap = IsAltDown(paper);
                    float snapped = freeSnap ? MathF.Max(0f, t) : MathF.Max(0f, MathF.Round(t * fps) / fps);
                    if (_dragPart == DragPart.Playhead)
                        SetPlayhead(t, snapToFrame: !freeSnap);
                    else if (_dragPart == DragPart.WorkRangeIn)
                        _workRangeIn = MathF.Min(snapped, MathF.Max(0f, _workRangeOut - 1f / fps));
                    else if (_dragPart == DragPart.WorkRangeOut)
                        _workRangeOut = MathF.Max(snapped, _workRangeIn + 1f / fps);
                })
                .OnDragEnd(_ => _dragPart = DragPart.None)
                .OnScroll(e => ZoomTimeline((float)e.Delta, (float)e.NormalizedPosition.X));

            using (ruler.Enter())
            {
                paper.Draw((canvas, rect) => PaintTimeRuler(canvas, rect, font));
            }

            // 2. Animation Event Bar
            ElementBuilder eventBar = paper.Box("anim_event_bar").Width(width).Height(EventBarHeight)
                .BackgroundColor(EditorTheme.Neutral300)
                .OnPress(e => OnPressEventBar(e.RelativePosition, TimeFromLocalX, LocalXFromTime))
                .OnDragging(e => OnDragEventBar(e.RelativePosition, TimeFromLocalX))
                .OnDragEnd(_ => _dragPart = DragPart.None)
                .OnRightClick(e =>
                {
                    SetPlayhead(TimeFromLocalX(e.RelativePosition.X));
                    Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
                    {
                        menu.Item("Add Instant Event Here", () => AddEventAtPlayhead(ranged: false));
                        menu.Item("Add Ranged (Start/End) Event Here", () => AddEventAtPlayhead(ranged: true));
                    });
                })
                .OnDoubleClick(e =>
                {
                    SetPlayhead(TimeFromLocalX(e.RelativePosition.X));
                    AddEventAtPlayhead(ranged: false);
                });

            using (eventBar.Enter())
            {
                paper.Draw((canvas, rect) => PaintEventBar(canvas, rect, font));
            }

            EditorGUI.Divider(paper, "anim_right_hdiv");

            // 3. Main Canvas: either Dope Sheet or Curves view
            float mainH = MathF.Max(40f, height - TimeRulerHeight - EventBarHeight - 1f);
            if (_curvesMode)
                DrawCurvesCanvas(paper, font, width, mainH, TimeFromLocalX, LocalXFromTime);
            else
                DrawDopeSheetCanvas(paper, font, width, mainH, TimeFromLocalX, LocalXFromTime);
        }
    }

    private void ZoomTimeline(float scrollDelta, float normalizedPivotX)
    {
        float factor = scrollDelta > 0f ? 0.85f : 1.18f;
        float range = MathF.Max(0.1f, _viewMaxTime - _viewMinTime);
        float pivotTime = _viewMinTime + Math.Clamp(normalizedPivotX, 0f, 1f) * range;
        float newRange = Math.Clamp(range * factor, 0.1f, 600f);

        _viewMinTime = MathF.Max(0f, pivotTime - normalizedPivotX * newRange);
        _viewMaxTime = _viewMinTime + newRange;
    }

    private void PaintTimeRuler(Canvas canvas, Rect rect, Scribe.FontFile font)
    {
        float x = (float)rect.Min.X, y = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        float range = MathF.Max(0.05f, _viewMaxTime - _viewMinTime);

        canvas.SaveState();
        float step = CurveRenderer.GetNiceStep(range, w, 65f);
        float subStep = step * 0.25f;

        // Sub-ticks
        canvas.SetStrokeColor(Color32.FromArgb(35, 255, 255, 255));
        canvas.SetStrokeWidth(1f);
        for (float t = MathF.Ceiling(_viewMinTime / subStep) * subStep; t <= _viewMaxTime; t += subStep)
        {
            float px = x + (t - _viewMinTime) / range * w;
            canvas.BeginPath();
            canvas.MoveTo(px, y + h - 4f);
            canvas.LineTo(px, y + h);
            canvas.Stroke();
        }

        // Major ticks + labels
        canvas.SetStrokeColor(Color32.FromArgb(90, 255, 255, 255));
        for (float t = MathF.Ceiling(_viewMinTime / step) * step; t <= _viewMaxTime; t += step)
        {
            float px = x + (t - _viewMinTime) / range * w;
            canvas.BeginPath();
            canvas.MoveTo(px, y + h - 9f);
            canvas.LineTo(px, y + h);
            canvas.Stroke();
            canvas.DrawText($"{t:0.##}s", px + 3f, y + 4f, Color32.FromArgb(180, 210, 210, 220), 10f, font);
        }

        // Duration marker bar
        if (IsClipValid && _clip!.Duration > 0f)
        {
            float durX = x + (_clip.Duration - _viewMinTime) / range * w;
            if (durX >= x && durX <= x + w)
                canvas.RectFilled(durX - 1f, y, 2f, h, EditorTheme.ToColor32(EditorTheme.Purple400, 0.7f));
        }

        // Sequencer In / Out Working Range Bracket
        if (_useWorkRange && _workRangeOut > _workRangeIn)
        {
            float inX = x + (_workRangeIn - _viewMinTime) / range * w;
            float outX = x + (_workRangeOut - _viewMinTime) / range * w;
            float clampedIn = Math.Clamp(inX, x, x + w);
            float clampedOut = Math.Clamp(outX, x, x + w);
            if (clampedOut > clampedIn)
                canvas.RectFilled(clampedIn, y, clampedOut - clampedIn, h, EditorTheme.ToColor32(EditorTheme.Green400, 0.18f));
            if (inX >= x - 4f && inX <= x + w + 4f)
            {
                canvas.RectFilled(inX, y, 2f, h, EditorTheme.ToColor32(EditorTheme.Green400, 0.95f));
                canvas.RectFilled(inX, y, 6f, 2f, EditorTheme.ToColor32(EditorTheme.Green400, 0.95f));
                canvas.RectFilled(inX, y + h - 2f, 6f, 2f, EditorTheme.ToColor32(EditorTheme.Green400, 0.95f));
            }
            if (outX >= x - 4f && outX <= x + w + 4f)
            {
                canvas.RectFilled(outX - 2f, y, 2f, h, EditorTheme.ToColor32(EditorTheme.Green400, 0.95f));
                canvas.RectFilled(outX - 6f, y, 6f, 2f, EditorTheme.ToColor32(EditorTheme.Green400, 0.95f));
                canvas.RectFilled(outX - 6f, y + h - 2f, 6f, 2f, EditorTheme.ToColor32(EditorTheme.Green400, 0.95f));
            }
        }

        // Playhead handle at top
        float playX = x + (_currentTime - _viewMinTime) / range * w;
        if (playX >= x - 4f && playX <= x + w + 4f)
        {
            Color headCol = _recording ? EditorTheme.Red400 : EditorTheme.Accent;
            canvas.RoundedRectFilled(playX - 5f, y + 2f, 10f, h - 2f, 2f, 2f, 2f, 2f, EditorTheme.ToColor32(headCol, 1f));
        }

        canvas.RestoreState();
    }

    // ================================================================
    //  Event Lane Painting & Interaction
    // ================================================================

    private void OnPressEventBar(Float2 localPos, Func<float, float> timeFromX, Func<float, float> xFromTime)
    {
        if (!IsClipValid) return;

        // Hit-test in reverse so top-most / latest events win
        for (int i = _clip!.PropertyEvents.Count - 1; i >= 0; i--)
        {
            SceneAnimationEvent ev = _clip.PropertyEvents[i];
            float ex0 = xFromTime(ev.Time);

            if (ev.IsRange)
            {
                float ex1 = xFromTime(ev.EndTime);
                if (MathF.Abs(localPos.X - ex1) <= 6f)
                {
                    _selectedEventIndex = i;
                    _selectedKeyIndex = -1;
                    _dragPart = DragPart.EventEnd;
                    _dragKey = i;
                    _dragStartTime = ev.Time;
                    _dragEndTime = ev.EndTime;
                    Undo.RecordObject(ClipAsset!, "Resize Animation Event End");
                    return;
                }
                if (MathF.Abs(localPos.X - ex0) <= 6f)
                {
                    _selectedEventIndex = i;
                    _selectedKeyIndex = -1;
                    _dragPart = DragPart.EventStart;
                    _dragKey = i;
                    _dragStartTime = ev.Time;
                    _dragEndTime = ev.EndTime;
                    Undo.RecordObject(ClipAsset!, "Resize Animation Event Start");
                    return;
                }
                if (localPos.X >= ex0 && localPos.X <= ex1)
                {
                    _selectedEventIndex = i;
                    _selectedKeyIndex = -1;
                    _dragPart = DragPart.Event;
                    _dragKey = i;
                    _dragStartTime = ev.Time;
                    _dragEndTime = ev.EndTime;
                    _dragClickOffset = timeFromX(localPos.X) - ev.Time;
                    Undo.RecordObject(ClipAsset!, "Move Ranged Animation Event");
                    return;
                }
            }
            else if (MathF.Abs(localPos.X - ex0) <= 7f)
            {
                _selectedEventIndex = i;
                _selectedKeyIndex = -1;
                _dragPart = DragPart.Event;
                _dragKey = i;
                _dragStartTime = ev.Time;
                _dragEndTime = ev.EndTime;
                _dragClickOffset = 0f;
                Undo.RecordObject(ClipAsset!, "Move Animation Event");
                return;
            }
        }

        _selectedEventIndex = -1;
        _dragPart = DragPart.Playhead;
        SetPlayhead(timeFromX(localPos.X));
    }

    private void OnDragEventBar(Float2 localPos, Func<float, float> timeFromX)
    {
        if (_dragPart == DragPart.Playhead)
        {
            SetPlayhead(timeFromX(localPos.X));
            return;
        }
        if (!IsClipValid || _dragKey < 0 || _dragKey >= _clip!.PropertyEvents.Count) return;

        float fps = MathF.Max(1f, _clip.FrameRate);
        float minStep = 1f / fps;
        float snappedT = MathF.Max(0f, MathF.Round(timeFromX(localPos.X) * fps) / fps);
        SceneAnimationEvent ev = _clip.PropertyEvents[_dragKey];

        if (_dragPart == DragPart.Event)
        {
            float span = MathF.Max(0f, _dragEndTime - _dragStartTime);
            float newStart = MathF.Max(0f, MathF.Round((timeFromX(localPos.X) - _dragClickOffset) * fps) / fps);
            ev.Time = newStart;
            ev.EndTime = span > 1e-5f ? newStart + span : newStart;
            _clip.Invalidate();
            EditorUtility.SetDirty(ClipAsset!);
        }
        else if (_dragPart == DragPart.EventStart)
        {
            ev.Time = MathF.Min(snappedT, MathF.Max(0f, ev.EndTime - minStep));
            _clip.Invalidate();
            EditorUtility.SetDirty(ClipAsset!);
        }
        else if (_dragPart == DragPart.EventEnd)
        {
            ev.EndTime = MathF.Max(snappedT, ev.Time + minStep);
            _clip.Invalidate();
            EditorUtility.SetDirty(ClipAsset!);
        }
    }

    private void PaintEventBar(Canvas canvas, Rect rect, Scribe.FontFile font)
    {
        float x = (float)rect.Min.X, y = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        float range = MathF.Max(0.05f, _viewMaxTime - _viewMinTime);

        canvas.SaveState();
        if (IsClipValid)
        {
            for (int i = 0; i < _clip!.PropertyEvents.Count; i++)
            {
                SceneAnimationEvent ev = _clip.PropertyEvents[i];
                bool sel = i == _selectedEventIndex;

                if (ev.IsRange)
                {
                    float ex0 = x + (ev.Time - _viewMinTime) / range * w;
                    float ex1 = x + (ev.EndTime - _viewMinTime) / range * w;
                    if (ex1 < x - 8f || ex0 > x + w + 8f) continue;

                    Color barCol = sel ? EditorTheme.Amber400 : EditorTheme.Purple400;
                    float barW = MathF.Max(4f, ex1 - ex0);
                    canvas.RoundedRectFilled(ex0, y + 2f, barW, h - 4f, 3f, 3f, 3f, 3f, EditorTheme.ToColor32(barCol, sel ? 0.75f : 0.48f));
                    // Start & End handles
                    canvas.RectFilled(ex0, y + 2f, 2f, h - 4f, EditorTheme.ToColor32(barCol, 1f));
                    canvas.RectFilled(ex0 + barW - 2f, y + 2f, 2f, h - 4f, EditorTheme.ToColor32(barCol, 1f));
                }
                else
                {
                    float ex = x + (ev.Time - _viewMinTime) / range * w;
                    if (ex < x - 8f || ex > x + w + 8f) continue;

                    Color col = sel ? EditorTheme.Amber400 : EditorTheme.Blue400;
                    canvas.RectFilled(ex - 1f, y + 2f, 2f, h - 4f, EditorTheme.ToColor32(col, 1f));
                    canvas.CircleFilled(ex, y + h * 0.5f, sel ? 4.5f : 3.5f, EditorTheme.ToColor32(col, 1f));
                }
            }
        }

        // Playhead line through event bar
        float playX = x + (_currentTime - _viewMinTime) / range * w;
        if (playX >= x && playX <= x + w)
        {
            Color headCol = _recording ? EditorTheme.Red400 : EditorTheme.Accent;
            canvas.RectFilled(playX - 1f, y, 2f, h, EditorTheme.ToColor32(headCol, 0.9f));
        }

        canvas.RestoreState();
    }

    // ================================================================
    //  Unreal Engine Sequencer Mouse & Keyboard Navigation
    // ================================================================

    private static bool IsShiftDown(Paper paper)
        => paper.IsKeyDown(PaperKey.LeftShift) || paper.IsKeyDown(PaperKey.RightShift);

    private static bool IsCtrlDown(Paper paper)
        => paper.IsKeyDown(PaperKey.LeftControl) || paper.IsKeyDown(PaperKey.RightControl);

    private static bool IsAltDown(Paper paper)
        => paper.IsKeyDown(PaperKey.LeftAlt) || paper.IsKeyDown(PaperKey.RightAlt);

    private static bool IsKeySelected(HashSet<(int RowIndex, int KeyIndex)> selectedKeys, int rowIdx, int keyIdx, int primaryRow, int primaryKey)
        => (rowIdx == primaryRow && keyIdx == primaryKey) || selectedKeys.Contains((rowIdx, keyIdx));

    private void ProcessUnrealCanvasNavigationAndShortcuts(Paper paper, Func<float, float> timeFromX, Func<float, float> xFromTime)
    {
        Float2 mousePos = paper.PointerPos;
        bool overCanvas = _canvasScreenRect.Contains(mousePos);
        bool overRuler = _rulerScreenRect.Contains(mousePos);

        // 1. Right Mouse Button: Drag = Pan, Alt + Drag = Zoom around pivot, Quick Click = Context Menu
        if ((overCanvas || overRuler) && paper.IsPointerPressed(PaperMouseBtn.Right))
        {
            _rmbPressScreenPos = mousePos;
            _rmbLastScreenPos = mousePos;
            _rmbMoved = false;
            _rmbZoomActive = IsAltDown(paper);
            _rmbPanActive = !_rmbZoomActive;

            Rect activeRect = overCanvas ? _canvasScreenRect : _rulerScreenRect;
            float normX = activeRect.Size.X > 1f ? Math.Clamp((float)((mousePos.X - activeRect.Min.X) / activeRect.Size.X), 0f, 1f) : 0.5f;
            float normY = activeRect.Size.Y > 1f ? Math.Clamp((float)((mousePos.Y - activeRect.Min.Y) / activeRect.Size.Y), 0f, 1f) : 0.5f;
            _rmbPivotNorm = new Float2(normX, normY);
        }

        if ((_rmbPanActive || _rmbZoomActive) && paper.IsPointerDown(PaperMouseBtn.Right))
        {
            Float2 delta = mousePos - _rmbLastScreenPos;
            _rmbLastScreenPos = mousePos;

            if (Float2.Length(mousePos - _rmbPressScreenPos) > 4f)
                _rmbMoved = true;

            if (_rmbMoved && (MathF.Abs((float)delta.X) > 0.01f || MathF.Abs((float)delta.Y) > 0.01f))
            {
                if (_rmbZoomActive)
                {
                    // Alt + RMB Drag: Unreal smooth zoom around initial press pivot
                    float zoomH = MathF.Exp(-(float)delta.X * 0.008f);
                    float rangeT = MathF.Max(0.1f, _viewMaxTime - _viewMinTime);
                    float pivotT = _viewMinTime + _rmbPivotNorm.X * rangeT;
                    float newRangeT = Math.Clamp(rangeT * zoomH, 0.05f, 600f);
                    _viewMinTime = MathF.Max(0f, pivotT - _rmbPivotNorm.X * newRangeT);
                    _viewMaxTime = _viewMinTime + newRangeT;

                    if (_curvesMode)
                    {
                        float zoomV = MathF.Exp((float)delta.Y * 0.008f);
                        float rangeV = MathF.Max(0.01f, _curveMaxVal - _curveMinVal);
                        float invY = 1f - _rmbPivotNorm.Y;
                        float pivotV = _curveMinVal + invY * rangeV;
                        float newRangeV = Math.Clamp(rangeV * zoomV, 0.02f, 100000f);
                        _curveMinVal = pivotV - invY * newRangeV;
                        _curveMaxVal = pivotV + (1f - invY) * newRangeV;
                    }
                }
                else
                {
                    // RMB Drag: Pan timeline horizontally (and vertically in Curves view)
                    PanViewByPixels((float)delta.X, (float)delta.Y);
                }
            }
        }

        if ((_rmbPanActive || _rmbZoomActive) && paper.IsPointerReleased(PaperMouseBtn.Right))
        {
            bool wasClick = !_rmbMoved;
            _rmbPanActive = false;
            _rmbZoomActive = false;
            _rmbMoved = false;

            if (wasClick)
            {
                if (_rulerScreenRect.Contains(_rmbPressScreenPos))
                {
                    float localX = (float)(_rmbPressScreenPos.X - _rulerScreenRect.Min.X);
                    OpenTimeRulerContextMenu(paper, timeFromX(localX));
                }
                else if (_canvasScreenRect.Contains(_rmbPressScreenPos))
                {
                    var localPos = new Float2(_rmbPressScreenPos.X - _canvasScreenRect.Min.X, _rmbPressScreenPos.Y - _canvasScreenRect.Min.Y);
                    if (_curvesMode)
                        OpenCurvesContextMenu(paper, localPos, timeFromX, xFromTime);
                    else
                        OnRightClickDopeSheet(paper, localPos, timeFromX, xFromTime);
                }
            }
        }

        // 2. Middle Mouse Button: Drag = Pan, Click = Move Playhead without losing selection
        if ((overCanvas || overRuler) && paper.IsPointerPressed(PaperMouseBtn.Middle))
        {
            _mmbPanActive = true;
            _rmbPressScreenPos = mousePos;
            _rmbLastScreenPos = mousePos;
            _rmbMoved = false;
        }

        if (_mmbPanActive && paper.IsPointerDown(PaperMouseBtn.Middle))
        {
            Float2 delta = mousePos - _rmbLastScreenPos;
            _rmbLastScreenPos = mousePos;
            if (Float2.Length(mousePos - _rmbPressScreenPos) > 4f)
                _rmbMoved = true;

            if (_rmbMoved)
                PanViewByPixels((float)delta.X, (float)delta.Y);
        }

        if (_mmbPanActive && paper.IsPointerReleased(PaperMouseBtn.Middle))
        {
            bool wasClick = !_rmbMoved;
            _mmbPanActive = false;
            _rmbMoved = false;

            if (wasClick)
            {
                float localX = (float)(mousePos.X - _canvasScreenRect.Min.X);
                _playing = false;
                SetPlayhead(timeFromX(localX), snapToFrame: !IsAltDown(paper));
            }
        }

        // 3. Unreal Sequencer Keyboard Shortcuts when pointer is over the timeline canvas
        if ((overCanvas || overRuler) && paper.FocusedElementId == 0)
        {
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
            {
                DeleteSelectedKeysOrEvent();
            }
            else if (Input.GetKeyDown(KeyCode.F))
            {
                FrameSelectedOrAll();
            }
            else if (Input.GetKeyDown(KeyCode.Space) && IsClipValid)
            {
                _playing = !_playing;
                if (_playing && !_previewing && !Application.IsPlaying)
                    EnterPreviewMode();
            }
            else if (Input.GetKeyDown(KeyCode.LeftBracket) && IsClipValid)
            {
                _useWorkRange = true;
                _workRangeIn = _currentTime;
                if (_workRangeOut <= _workRangeIn)
                    _workRangeOut = _workRangeIn + MathF.Max(0.25f, 1f / MathF.Max(1f, _clip!.FrameRate));
            }
            else if (Input.GetKeyDown(KeyCode.RightBracket) && IsClipValid)
            {
                _useWorkRange = true;
                _workRangeOut = MathF.Max(_currentTime, _workRangeIn + 1f / MathF.Max(1f, _clip!.FrameRate));
            }
        }
    }

    private void PanViewByPixels(float deltaX, float deltaY)
    {
        float rangeT = MathF.Max(0.05f, _viewMaxTime - _viewMinTime);
        float dt = -deltaX / MathF.Max(1f, _timelineWidth) * rangeT;
        float newMinT = MathF.Max(0f, _viewMinTime + dt);
        _viewMaxTime = newMinT + rangeT;
        _viewMinTime = newMinT;

        if (_curvesMode)
        {
            float rangeV = MathF.Max(0.01f, _curveMaxVal - _curveMinVal);
            float dv = deltaY / MathF.Max(1f, _timelineHeight) * rangeV;
            _curveMinVal += dv;
            _curveMaxVal += dv;
        }
    }

    private void OpenTimeRulerContextMenu(Paper paper, float clickTime)
    {
        float fps = IsClipValid ? MathF.Max(1f, _clip!.FrameRate) : 60f;
        float snapped = MathF.Max(0f, MathF.Round(clickTime * fps) / fps);
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
        {
            menu.Item("Set Work Range In [ Here", () =>
            {
                _useWorkRange = true;
                _workRangeIn = snapped;
                if (_workRangeOut <= _workRangeIn)
                    _workRangeOut = _workRangeIn + MathF.Max(0.25f, 1f / fps);
            });
            menu.Item("Set Work Range Out ] Here", () =>
            {
                _useWorkRange = true;
                _workRangeOut = MathF.Max(snapped, _workRangeIn + 1f / fps);
            });
            if (_useWorkRange)
                menu.Item("Clear Work Range", () => _useWorkRange = false);
            menu.Separator();
            menu.Item("Frame All (F)", FrameSelectedOrAll);
        });
    }

    private void FrameSelectedOrAll()
    {
        if (!IsClipValid) return;

        float minT = float.MaxValue, maxT = float.MinValue;
        float minV = float.MaxValue, maxV = float.MinValue;
        int count = 0;

        foreach ((int rIdx, int kIdx) in _selectedKeys)
        {
            if (rIdx < 0 || rIdx >= _rows.Count) continue;
            TrackRow r = _rows[rIdx];
            float kt = GetKeyTime(r, kIdx);
            minT = MathF.Min(minT, kt);
            maxT = MathF.Max(maxT, kt);
            if (r.CurveTrack != null && kIdx >= 0 && kIdx < r.CurveTrack.Curve.Count)
            {
                float kv = r.CurveTrack.Curve[kIdx].Value;
                minV = MathF.Min(minV, kv);
                maxV = MathF.Max(maxV, kv);
            }
            count++;
        }

        if (count > 0)
        {
            float padT = MathF.Max(0.25f, (maxT - minT) * 0.25f);
            _viewMinTime = MathF.Max(0f, minT - padT);
            _viewMaxTime = maxT + padT;
            if (_curvesMode && minV <= maxV)
            {
                float padV = MathF.Max(0.5f, (maxV - minV) * 0.25f);
                _curveMinVal = minV - padV;
                _curveMaxVal = maxV + padV;
            }
            return;
        }

        _curveBoundsInitialized = false;
        FitTimelineRange();
    }

    private void DeleteSelectedKeysOrEvent()
    {
        if (!IsClipValid) return;

        if (_selectedEventIndex >= 0 && _selectedEventIndex < _clip!.PropertyEvents.Count)
        {
            Undo.RecordObject(ClipAsset!, "Delete Animation Event");
            _clip.PropertyEvents.RemoveAt(_selectedEventIndex);
            _selectedEventIndex = -1;
            _clip.Invalidate();
            EditorUtility.SetDirty(ClipAsset!);
            return;
        }

        if (_selectedKeys.Count == 0 && _selectedRowIndex >= 0 && _selectedKeyIndex >= 0)
            _selectedKeys.Add((_selectedRowIndex, _selectedKeyIndex));

        if (_selectedKeys.Count == 0) return;

        Undo.RecordObject(ClipAsset!, "Delete Selected Keyframes");

        // Sort descending by KeyIndex per row so removals don't shift earlier indices
        var ordered = _selectedKeys.OrderByDescending(k => k.KeyIndex).ToList();
        foreach ((int rIdx, int kIdx) in ordered)
        {
            if (rIdx < 0 || rIdx >= _rows.Count) continue;
            TrackRow row = _rows[rIdx];
            if (IsRowLocked(row)) continue;

            if (row.Kind == TrackRowKind.Group && row.GroupTracks != null && row.SummaryTimes != null && kIdx >= 0 && kIdx < row.SummaryTimes.Count)
            {
                float targetTime = row.SummaryTimes[kIdx];
                foreach (SceneCurveTrack ct in row.GroupTracks)
                {
                    if (ct.Locked) continue;
                    for (int k = ct.Curve.Count - 1; k >= 0; k--)
                        if (MathF.Abs(ct.Curve[k].Time - targetTime) <= 0.0005f)
                            ct.Curve.RemoveKey(k);
                }
            }
            else if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null && kIdx >= 0 && kIdx < row.CurveTrack.Curve.Count)
                row.CurveTrack.Curve.RemoveKey(kIdx);
            else if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null && kIdx >= 0 && kIdx < row.ObjectTrack.Keyframes.Count)
                row.ObjectTrack.Keyframes.RemoveAt(kIdx);
            else if (row.Kind == TrackRowKind.String && row.StringTrack != null && kIdx >= 0 && kIdx < row.StringTrack.Keyframes.Count)
                row.StringTrack.Keyframes.RemoveAt(kIdx);
        }

        _selectedKeys.Clear();
        _selectedKeyIndex = -1;
        _clip!.Invalidate();
        EditorUtility.SetDirty(ClipAsset!);
        if (_previewing) SampleAtPlayhead();
    }

    // ================================================================
    //  Dope Sheet Canvas
    // ================================================================

    private void DrawDopeSheetCanvas(Paper paper, Scribe.FontFile font, float width, float height,
        Func<float, float> timeFromX, Func<float, float> xFromTime)
    {
        ElementBuilder sheet = paper.Box("anim_dopesheet").Size(width, height).Clip()
            .BackgroundColor(EditorTheme.Neutral300)
            .OnPostLayout((_, rect) =>
            {
                _canvasScreenRect = rect;
                _timelineWidth = MathF.Max(1f, (float)rect.Size.X);
                _timelineHeight = MathF.Max(1f, (float)rect.Size.Y);
            })
            .OnPress(e => OnPressDopeSheet(paper, e.RelativePosition, timeFromX, xFromTime))
            .OnDragging(e => OnDragDopeSheet(paper, e, timeFromX, xFromTime))
            .OnDragEnd(_ => _dragPart = DragPart.None)
            .OnDoubleClick(e => OnDoubleClickDopeSheet(e.RelativePosition, timeFromX))
            .OnScroll(e => ZoomTimeline((float)e.Delta, (float)e.NormalizedPosition.X));

        using (sheet.Enter())
        {
            paper.Draw((canvas, rect) => PaintDopeSheet(canvas, rect));
        }
    }

    private (int RowIndex, int KeyIndex) HitTestDopeSheetKey(Float2 localPos, Func<float, float> xFromTime)
    {
        int rowIdx = (int)(localPos.Y / TrackRowHeight);
        if (rowIdx < 0 || rowIdx >= _rows.Count) return (-1, -1);

        TrackRow row = _rows[rowIdx];
        const float hitRadius = 7f;

        if (row.Kind == TrackRowKind.Group && row.SummaryTimes != null)
        {
            for (int k = 0; k < row.SummaryTimes.Count; k++)
                if (MathF.Abs(localPos.X - xFromTime(row.SummaryTimes[k])) <= hitRadius)
                    return (rowIdx, k);
        }
        else if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null)
        {
            AnimationCurve c = row.CurveTrack.Curve;
            for (int k = 0; k < c.Count; k++)
                if (MathF.Abs(localPos.X - xFromTime(c[k].Time)) <= hitRadius)
                    return (rowIdx, k);
        }
        else if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null)
        {
            List<SceneObjectKeyframe> kfs = row.ObjectTrack.Keyframes;
            for (int k = 0; k < kfs.Count; k++)
                if (MathF.Abs(localPos.X - xFromTime(kfs[k].Time)) <= hitRadius)
                    return (rowIdx, k);
        }
        else if (row.Kind == TrackRowKind.String && row.StringTrack != null)
        {
            List<SceneStringKeyframe> kfs = row.StringTrack.Keyframes;
            for (int k = 0; k < kfs.Count; k++)
                if (MathF.Abs(localPos.X - xFromTime(kfs[k].Time)) <= hitRadius)
                    return (rowIdx, k);
        }

        return (rowIdx, -1);
    }

    private void OnPressDopeSheet(Paper paper, Float2 localPos, Func<float, float> timeFromX, Func<float, float> xFromTime)
    {
        (int rowIdx, int keyIdx) = HitTestDopeSheetKey(localPos, xFromTime);
        bool shift = IsShiftDown(paper);
        bool ctrl = IsCtrlDown(paper);

        _selectedEventIndex = -1;
        _dragPressLocalPos = localPos;

        // Unreal Ctrl + LMB Click on empty row space: add keyframe immediately
        if (ctrl && rowIdx >= 0 && keyIdx < 0 && IsClipValid && !IsRowLocked(_rows[rowIdx]))
        {
            float fps = MathF.Max(1f, _clip!.FrameRate);
            float t = IsAltDown(paper) ? timeFromX(localPos.X) : MathF.Round(timeFromX(localPos.X) * fps) / fps;
            AddKeyToRowAt(_rows[rowIdx], t);
            return;
        }

        if (rowIdx >= 0 && keyIdx >= 0 && IsClipValid && !IsRowLocked(_rows[rowIdx]))
        {
            var pair = (rowIdx, keyIdx);
            if (ctrl)
            {
                if (!_selectedKeys.Remove(pair))
                    _selectedKeys.Add(pair);
            }
            else if (shift)
            {
                _selectedKeys.Add(pair);
            }
            else if (!_selectedKeys.Contains(pair))
            {
                _selectedKeys.Clear();
                _selectedKeys.Add(pair);
            }

            _selectedRowIndex = rowIdx;
            _selectedKeyIndex = keyIdx;
            _dragPart = DragPart.Keyframe;
            _dragRow = rowIdx;
            _dragKey = keyIdx;
            _dragStartTime = GetKeyTime(_rows[rowIdx], keyIdx);
            CaptureMultiDragSnapshot();
            Undo.RecordObject(ClipAsset!, "Move Keyframes");
            return;
        }

        // LMB Drag on empty space -> Unreal Marquee Box Selection
        _selectedRowIndex = rowIdx;
        if (!shift && !ctrl)
        {
            _selectedKeys.Clear();
            _selectedKeyIndex = -1;
        }
        _dragPart = DragPart.MarqueeSelect;
        _marqueeStartLocal = localPos;
        _marqueeEndLocal = localPos;
    }

    private void CaptureMultiDragSnapshot()
    {
        _multiDragSnapshot.Clear();
        foreach ((int rIdx, int kIdx) in _selectedKeys)
        {
            if (rIdx < 0 || rIdx >= _rows.Count) continue;
            TrackRow row = _rows[rIdx];
            if (IsRowLocked(row)) continue;
            float t = GetKeyTime(row, kIdx);
            float v = row.CurveTrack != null && kIdx >= 0 && kIdx < row.CurveTrack.Curve.Count ? row.CurveTrack.Curve[kIdx].Value : 0f;
            _multiDragSnapshot.Add((rIdx, kIdx, t, v));
        }
    }

    private void OnDragDopeSheet(Paper paper, DragEvent e, Func<float, float> timeFromX, Func<float, float> xFromTime)
    {
        if (_dragPart == DragPart.Keyframe && IsClipValid && _dragRow >= 0 && _dragRow < _rows.Count)
        {
            float fps = MathF.Max(1f, _clip!.FrameRate);
            float rawTargetT = timeFromX(e.RelativePosition.X);
            float targetT = IsAltDown(paper) ? rawTargetT : MathF.Round(rawTargetT * fps) / fps;
            float dt = targetT - _dragStartTime;

            if (_multiDragSnapshot.Count <= 1)
            {
                TrackRow row = _rows[_dragRow];
                if (IsRowLocked(row)) return;
                _dragKey = MoveKeyTime(row, _dragKey, MathF.Max(0f, _dragStartTime + dt));
                _selectedKeyIndex = _dragKey;
                _selectedKeys.Clear();
                _selectedKeys.Add((_dragRow, _dragKey));
            }
            else
            {
                _selectedKeys.Clear();
                for (int i = 0; i < _multiDragSnapshot.Count; i++)
                {
                    (int rIdx, int kIdx, float startT, float startV) = _multiDragSnapshot[i];
                    if (rIdx < 0 || rIdx >= _rows.Count) continue;
                    TrackRow r = _rows[rIdx];
                    if (IsRowLocked(r)) continue;
                    int newKeyIdx = MoveKeyTime(r, kIdx, MathF.Max(0f, startT + dt));
                    _multiDragSnapshot[i] = (rIdx, newKeyIdx, startT, startV);
                    _selectedKeys.Add((rIdx, newKeyIdx));
                    if (rIdx == _dragRow)
                    {
                        _dragKey = newKeyIdx;
                        _selectedKeyIndex = newKeyIdx;
                    }
                }
            }

            _clip.Invalidate();
            EditorUtility.SetDirty(ClipAsset!);
            if (_previewing) SampleAtPlayhead();
        }
        else if (_dragPart == DragPart.MarqueeSelect)
        {
            _marqueeEndLocal = e.RelativePosition;
            UpdateDopeSheetMarqueeSelection(paper, xFromTime);
        }
    }

    private void UpdateDopeSheetMarqueeSelection(Paper paper, Func<float, float> xFromTime)
    {
        float minX = MathF.Min(_marqueeStartLocal.X, _marqueeEndLocal.X);
        float maxX = MathF.Max(_marqueeStartLocal.X, _marqueeEndLocal.X);
        float minY = MathF.Min(_marqueeStartLocal.Y, _marqueeEndLocal.Y);
        float maxY = MathF.Max(_marqueeStartLocal.Y, _marqueeEndLocal.Y);

        if (!IsShiftDown(paper) && !IsCtrlDown(paper))
            _selectedKeys.Clear();

        for (int i = 0; i < _rows.Count; i++)
        {
            float cy = i * TrackRowHeight + TrackRowHeight * 0.5f;
            if (cy < minY || cy > maxY) continue;

            TrackRow row = _rows[i];
            int count = GetRowKeyCount(row);
            for (int k = 0; k < count; k++)
            {
                float kx = xFromTime(GetKeyTime(row, k));
                if (kx >= minX && kx <= maxX)
                {
                    _selectedKeys.Add((i, k));
                    _selectedRowIndex = i;
                    _selectedKeyIndex = k;
                }
            }
        }
    }

    private static int GetRowKeyCount(TrackRow row) => row.Kind switch
    {
        TrackRowKind.Group => row.SummaryTimes?.Count ?? 0,
        TrackRowKind.Curve => row.CurveTrack?.Curve.Count ?? 0,
        TrackRowKind.Object => row.ObjectTrack?.Keyframes.Count ?? 0,
        TrackRowKind.String => row.StringTrack?.Keyframes.Count ?? 0,
        _ => 0,
    };

    private void OnDoubleClickDopeSheet(Float2 localPos, Func<float, float> timeFromX)
    {
        int rowIdx = (int)(localPos.Y / TrackRowHeight);
        if (rowIdx < 0 || rowIdx >= _rows.Count || IsRowLocked(_rows[rowIdx])) return;

        float fps = IsClipValid ? MathF.Max(1f, _clip!.FrameRate) : 60f;
        float time = MathF.Round(timeFromX(localPos.X) * fps) / fps;
        AddKeyToRowAt(_rows[rowIdx], time);
    }

    private void OnRightClickDopeSheet(Paper paper, Float2 localPos, Func<float, float> timeFromX, Func<float, float> xFromTime)
    {
        (int rowIdx, int keyIdx) = HitTestDopeSheetKey(localPos, xFromTime);
        if (rowIdx >= 0 && rowIdx < _rows.Count)
        {
            _selectedRowIndex = rowIdx;
            if (keyIdx >= 0)
            {
                _selectedKeyIndex = keyIdx;
                _selectedKeys.Add((rowIdx, keyIdx));
            }
        }

        TrackRow? row = rowIdx >= 0 && rowIdx < _rows.Count ? _rows[rowIdx] : null;
        float clickTime = timeFromX(localPos.X);

        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
        {
            if (row != null && !IsRowLocked(row))
            {
                if (keyIdx >= 0)
                {
                    if (row.Kind == TrackRowKind.Object)
                        menu.Item("Assign Object Reference...", () => OpenObjectKeyPicker(row, GetKeyTime(row, keyIdx)));
                    string delLabel = _selectedKeys.Count > 1
                        ? $"Delete {_selectedKeys.Count} Selected Keyframes (Del)"
                        : (row.Kind == TrackRowKind.Group ? "Delete Group Keyframes (Del)" : "Delete Keyframe (Del)");
                    menu.Item(delLabel, DeleteSelectedKeysOrEvent, danger: true);
                }
                else
                {
                    menu.Item("Add Keyframe Here (Ctrl+LMB)", () => AddKeyToRowAt(row, clickTime));
                }
                menu.Separator();
            }
            menu.Item(_selectedKeys.Count > 0 ? "Frame Selected Keys (F)" : "Frame All (F)", FrameSelectedOrAll);
        });
    }

    private static float GetKeyTime(TrackRow row, int keyIndex)
    {
        if (row.Kind == TrackRowKind.Group && row.SummaryTimes != null && keyIndex >= 0 && keyIndex < row.SummaryTimes.Count)
            return row.SummaryTimes[keyIndex];
        if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null && keyIndex >= 0 && keyIndex < row.CurveTrack.Curve.Count)
            return row.CurveTrack.Curve[keyIndex].Time;
        if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null && keyIndex >= 0 && keyIndex < row.ObjectTrack.Keyframes.Count)
            return row.ObjectTrack.Keyframes[keyIndex].Time;
        if (row.Kind == TrackRowKind.String && row.StringTrack != null && keyIndex >= 0 && keyIndex < row.StringTrack.Keyframes.Count)
            return row.StringTrack.Keyframes[keyIndex].Time;
        return 0f;
    }

    private static int MoveKeyTime(TrackRow row, int keyIndex, float newTime)
    {
        newTime = MathF.Max(0f, newTime);
        if (row.Kind == TrackRowKind.Group && row.GroupTracks != null && row.SummaryTimes != null && keyIndex >= 0 && keyIndex < row.SummaryTimes.Count)
        {
            float oldTime = row.SummaryTimes[keyIndex];
            if (MathF.Abs(newTime - oldTime) <= 1e-5f) return keyIndex;
            foreach (SceneCurveTrack ct in row.GroupTracks)
            {
                if (ct.Locked) continue;
                AnimationCurve curve = ct.Curve;
                for (int k = 0; k < curve.Count; k++)
                {
                    if (MathF.Abs(curve[k].Time - oldTime) <= 0.0005f)
                    {
                        Keyframe kf = curve[k];
                        curve.MoveKey(k, new Keyframe(newTime, kf.Value, kf.InTangent, kf.OutTangent, kf.Interpolation));
                        curve.SmoothTangents(CurveTangentMode.Auto);
                        break;
                    }
                }
            }
            row.SummaryTimes = ComputeGroupSummaryTimes(row.GroupTracks);
            for (int i = 0; i < row.SummaryTimes.Count; i++)
                if (MathF.Abs(row.SummaryTimes[i] - newTime) <= 0.0005f)
                    return i;
            return 0;
        }
        if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null && keyIndex >= 0 && keyIndex < row.CurveTrack.Curve.Count)
        {
            Keyframe k = row.CurveTrack.Curve[keyIndex];
            return row.CurveTrack.Curve.MoveKey(keyIndex, new Keyframe(newTime, k.Value, k.InTangent, k.OutTangent, k.Interpolation));
        }
        if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null && keyIndex >= 0 && keyIndex < row.ObjectTrack.Keyframes.Count)
        {
            SceneObjectKeyframe kf = row.ObjectTrack.Keyframes[keyIndex];
            kf.Time = newTime;
            row.ObjectTrack.SortKeyframes();
            return row.ObjectTrack.Keyframes.IndexOf(kf);
        }
        if (row.Kind == TrackRowKind.String && row.StringTrack != null && keyIndex >= 0 && keyIndex < row.StringTrack.Keyframes.Count)
        {
            SceneStringKeyframe kf = row.StringTrack.Keyframes[keyIndex];
            kf.Time = newTime;
            row.StringTrack.SortKeyframes();
            return row.StringTrack.Keyframes.IndexOf(kf);
        }
        return keyIndex;
    }

    private void DeleteKeyFromRow(TrackRow row, int keyIndex)
    {
        if (!IsClipValid || IsRowLocked(row)) return;
        Undo.RecordObject(ClipAsset!, "Delete Keyframe");

        if (row.Kind == TrackRowKind.Group && row.GroupTracks != null && row.SummaryTimes != null && keyIndex >= 0 && keyIndex < row.SummaryTimes.Count)
        {
            float targetTime = row.SummaryTimes[keyIndex];
            foreach (SceneCurveTrack ct in row.GroupTracks)
            {
                if (ct.Locked) continue;
                for (int k = ct.Curve.Count - 1; k >= 0; k--)
                {
                    if (MathF.Abs(ct.Curve[k].Time - targetTime) <= 0.0005f)
                        ct.Curve.RemoveKey(k);
                }
            }
        }
        else if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null && keyIndex >= 0 && keyIndex < row.CurveTrack.Curve.Count)
            row.CurveTrack.Curve.RemoveKey(keyIndex);
        else if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null && keyIndex >= 0 && keyIndex < row.ObjectTrack.Keyframes.Count)
            row.ObjectTrack.Keyframes.RemoveAt(keyIndex);
        else if (row.Kind == TrackRowKind.String && row.StringTrack != null && keyIndex >= 0 && keyIndex < row.StringTrack.Keyframes.Count)
            row.StringTrack.Keyframes.RemoveAt(keyIndex);

        _selectedKeys.Clear();
        _selectedKeyIndex = -1;
        _clip!.Invalidate();
        EditorUtility.SetDirty(ClipAsset!);
        if (_previewing) SampleAtPlayhead();
    }

    private void PaintDopeSheet(Canvas canvas, Rect rect)
    {
        float x = (float)rect.Min.X, y = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        float range = MathF.Max(0.05f, _viewMaxTime - _viewMinTime);

        canvas.SaveState();

        // 1. Alternating row stripes
        for (int i = 0; i < _rows.Count; i++)
        {
            float ry = y + i * TrackRowHeight;
            if (ry > y + h) break;

            if (i == _selectedRowIndex)
                canvas.RectFilled(x, ry, w, TrackRowHeight, EditorTheme.ToColor32(EditorTheme.Selected, 1f));
            else if (i % 2 == 1)
                canvas.RectFilled(x, ry, w, TrackRowHeight, Color32.FromArgb(14, 255, 255, 255));

            canvas.RectFilled(x, ry + TrackRowHeight - 1f, w, 1f, Color32.FromArgb(18, 255, 255, 255));
        }

        // 2. Vertical time grid lines
        float step = CurveRenderer.GetNiceStep(range, w, 65f);
        canvas.SetStrokeColor(Color32.FromArgb(22, 255, 255, 255));
        canvas.SetStrokeWidth(1f);
        for (float t = MathF.Ceiling(_viewMinTime / step) * step; t <= _viewMaxTime; t += step)
        {
            float gx = x + (t - _viewMinTime) / range * w;
            canvas.BeginPath();
            canvas.MoveTo(gx, y);
            canvas.LineTo(gx, y + h);
            canvas.Stroke();
        }

        // 3. Keyframe diamonds per row
        for (int i = 0; i < _rows.Count; i++)
        {
            float cy = y + i * TrackRowHeight + TrackRowHeight * 0.5f;
            if (cy < y - 10f || cy > y + h + 10f) continue;

            TrackRow row = _rows[i];

            if (row.Kind == TrackRowKind.Group && row.SummaryTimes != null)
            {
                for (int k = 0; k < row.SummaryTimes.Count; k++)
                {
                    float kx = x + (row.SummaryTimes[k] - _viewMinTime) / range * w;
                    if (kx < x - 8f || kx > x + w + 8f) continue;
                    bool keySel = IsKeySelected(_selectedKeys, i, k, _selectedRowIndex, _selectedKeyIndex);
                    PaintDiamond(canvas, kx, cy, keySel ? 6f : 5f, keySel ? EditorTheme.Amber400 : EditorTheme.Ink400, selected: true);
                }
            }
            else if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null)
            {
                AnimationCurve c = row.CurveTrack.Curve;
                for (int k = 0; k < c.Count; k++)
                {
                    float kx = x + (c[k].Time - _viewMinTime) / range * w;
                    if (kx < x - 8f || kx > x + w + 8f) continue;
                    bool keySel = IsKeySelected(_selectedKeys, i, k, _selectedRowIndex, _selectedKeyIndex);
                    PaintDiamond(canvas, kx, cy, keySel ? 5.5f : 4.5f, keySel ? EditorTheme.Amber400 : row.AccentColor, keySel);
                }
            }
            else if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null)
            {
                List<SceneObjectKeyframe> kfs = row.ObjectTrack.Keyframes;
                for (int k = 0; k < kfs.Count; k++)
                {
                    float kx = x + (kfs[k].Time - _viewMinTime) / range * w;
                    if (kx < x - 8f || kx > x + w + 8f) continue;
                    bool keySel = IsKeySelected(_selectedKeys, i, k, _selectedRowIndex, _selectedKeyIndex);
                    PaintDiamond(canvas, kx, cy, keySel ? 5.5f : 4.5f, keySel ? EditorTheme.Amber400 : EditorTheme.Purple400, keySel);
                }
            }
            else if (row.Kind == TrackRowKind.String && row.StringTrack != null)
            {
                List<SceneStringKeyframe> kfs = row.StringTrack.Keyframes;
                for (int k = 0; k < kfs.Count; k++)
                {
                    float kx = x + (kfs[k].Time - _viewMinTime) / range * w;
                    if (kx < x - 8f || kx > x + w + 8f) continue;
                    bool keySel = IsKeySelected(_selectedKeys, i, k, _selectedRowIndex, _selectedKeyIndex);
                    PaintDiamond(canvas, kx, cy, keySel ? 5.5f : 4.5f, keySel ? EditorTheme.Amber400 : EditorTheme.Blue400, keySel);
                }
            }
        }

        // 4. Playhead line
        float playX = x + (_currentTime - _viewMinTime) / range * w;
        if (playX >= x && playX <= x + w)
        {
            Color headCol = _recording ? EditorTheme.Red400 : EditorTheme.Accent;
            canvas.RectFilled(playX - 1f, y, 2f, h, EditorTheme.ToColor32(headCol, 0.95f));
        }

        // 5. Unreal-style Marquee Selection Box
        if (_dragPart == DragPart.MarqueeSelect)
            PaintMarqueeBox(canvas, x, y);

        canvas.RestoreState();
    }

    private void PaintMarqueeBox(Canvas canvas, float originX, float originY)
    {
        float minX = originX + MathF.Min(_marqueeStartLocal.X, _marqueeEndLocal.X);
        float maxX = originX + MathF.Max(_marqueeStartLocal.X, _marqueeEndLocal.X);
        float minY = originY + MathF.Min(_marqueeStartLocal.Y, _marqueeEndLocal.Y);
        float maxY = originY + MathF.Max(_marqueeStartLocal.Y, _marqueeEndLocal.Y);
        float mw = maxX - minX, mh = maxY - minY;
        if (mw < 2f && mh < 2f) return;

        canvas.RectFilled(minX, minY, mw, mh, EditorTheme.ToColor32(EditorTheme.Accent, 0.16f));
        canvas.SetStrokeColor(EditorTheme.ToColor32(EditorTheme.AccentBright, 0.85f));
        canvas.SetStrokeWidth(1f);
        canvas.BeginPath();
        canvas.Rect(minX, minY, mw, mh);
        canvas.Stroke();
    }

    private static void PaintDiamond(Canvas canvas, float cx, float cy, float radius, Color color, bool selected)
    {
        canvas.SetFillColor(EditorTheme.ToColor32(color, 1f));
        canvas.BeginPath();
        canvas.MoveTo(cx, cy - radius);
        canvas.LineTo(cx + radius, cy);
        canvas.LineTo(cx, cy + radius);
        canvas.LineTo(cx - radius, cy);
        canvas.ClosePath();
        canvas.Fill();

        if (selected)
        {
            canvas.SetStrokeColor(Color32.FromArgb(240, 255, 255, 255));
            canvas.SetStrokeWidth(1.5f);
            canvas.BeginPath();
            canvas.MoveTo(cx, cy - radius);
            canvas.LineTo(cx + radius, cy);
            canvas.LineTo(cx, cy + radius);
            canvas.LineTo(cx - radius, cy);
            canvas.ClosePath();
            canvas.Stroke();
        }
    }

    // ================================================================
    //  Curves Canvas View
    // ================================================================

    private bool IsRowVisibleInCurvesView(int rowIndex)
    {
        if (_selectedRowIndex < 0 || _selectedRowIndex >= _rows.Count) return true;
        if (rowIndex == _selectedRowIndex) return true;
        TrackRow sel = _rows[_selectedRowIndex];
        return sel.Kind == TrackRowKind.Group && !string.IsNullOrEmpty(sel.GroupKey) && _rows[rowIndex].GroupKey == sel.GroupKey;
    }

    private void EnsureCurveBounds()
    {
        if (_curveBoundsInitialized) return;
        _curveBoundsInitialized = true;

        float minV = float.MaxValue, maxV = float.MinValue;
        bool any = false;

        for (int i = 0; i < _rows.Count; i++)
        {
            if (!IsRowVisibleInCurvesView(i)) continue;
            if (_rows[i].CurveTrack is not { } ct || ct.Curve.Count == 0) continue;

            CurveRenderer.GetBounds(ct.Curve, out _, out _, out float cMinV, out float cMaxV);
            minV = MathF.Min(minV, cMinV);
            maxV = MathF.Max(maxV, cMaxV);
            any = true;
        }

        if (!any || maxV <= minV)
        {
            _curveMinVal = -1f;
            _curveMaxVal = 2f;
        }
        else
        {
            _curveMinVal = minV;
            _curveMaxVal = maxV;
        }
    }

    private void DrawCurvesCanvas(Paper paper, Scribe.FontFile font, float width, float height,
        Func<float, float> timeFromX, Func<float, float> xFromTime)
    {
        EnsureCurveBounds();
        float rangeV = MathF.Max(0.01f, _curveMaxVal - _curveMinVal);

        float ValueFromLocalY(float y) => _curveMinVal + (1f - y / MathF.Max(1f, _timelineHeight)) * rangeV;
        float LocalYFromValue(float v) => (1f - (v - _curveMinVal) / rangeV) * _timelineHeight;

        ElementBuilder curvesBox = paper.Box("anim_curves").Size(width, height).Clip()
            .BackgroundColor(EditorTheme.Neutral300)
            .OnPostLayout((_, rect) =>
            {
                _canvasScreenRect = rect;
                _timelineWidth = MathF.Max(1f, (float)rect.Size.X);
                _timelineHeight = MathF.Max(1f, (float)rect.Size.Y);
            })
            .OnPress(e => OnPressCurves(paper, e.RelativePosition, timeFromX, xFromTime, ValueFromLocalY, LocalYFromValue))
            .OnDragging(e => OnDragCurves(paper, e, timeFromX, xFromTime, ValueFromLocalY, LocalYFromValue))
            .OnDragEnd(_ => _dragPart = DragPart.None)
            .OnDoubleClick(e =>
            {
                int targetRow = FindTargetCurveRowForAdd();
                if (targetRow >= 0 && targetRow < _rows.Count && _rows[targetRow].CurveTrack != null && IsClipValid && !IsRowLocked(_rows[targetRow]))
                {
                    Undo.RecordObject(ClipAsset!, "Add Curve Keyframe");
                    float t = timeFromX(e.RelativePosition.X);
                    float v = ValueFromLocalY(e.RelativePosition.Y);
                    _clip!.SetCurveKey(_rows[targetRow].RelativePath, _rows[targetRow].ComponentType, _rows[targetRow].PropertyPath, t, v);
                    EditorUtility.SetDirty(ClipAsset!);
                }
            })
            .OnScroll(e =>
            {
                bool ctrl = IsCtrlDown(paper);
                bool shift = IsShiftDown(paper);
                // Unreal Curve Editor modifiers: Ctrl+Wheel = Vertical zoom only, Shift+Wheel = Horizontal zoom only, Wheel = Both
                if (!ctrl)
                    ZoomTimeline((float)e.Delta, (float)e.NormalizedPosition.X);
                if (!shift)
                {
                    float zf = (float)e.Delta > 0 ? 0.88f : 1.14f;
                    float nY = 1f - (float)e.NormalizedPosition.Y;
                    float cV = _curveMinVal + nY * rangeV;
                    float nrV = Math.Clamp(rangeV * zf, 0.02f, 100000f);
                    _curveMinVal = cV - nY * nrV;
                    _curveMaxVal = cV + (1f - nY) * nrV;
                }
            });

        using (curvesBox.Enter())
        {
            paper.Draw((canvas, rect) => PaintCurvesView(canvas, rect, font));
        }
    }

    private int FindTargetCurveRowForAdd()
    {
        if (_selectedRowIndex >= 0 && _selectedRowIndex < _rows.Count)
        {
            if (_rows[_selectedRowIndex].CurveTrack != null) return _selectedRowIndex;
            for (int i = _selectedRowIndex + 1; i < _rows.Count; i++)
                if (IsRowVisibleInCurvesView(i) && _rows[i].CurveTrack != null) return i;
        }
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i].CurveTrack != null) return i;
        return -1;
    }

    private void OnPressCurves(Paper paper, Float2 localPos, Func<float, float> timeFromX, Func<float, float> xFromTime,
        Func<float, float> valFromY, Func<float, float> yFromVal)
    {
        bool shift = IsShiftDown(paper);
        bool ctrl = IsCtrlDown(paper);
        _dragPressLocalPos = localPos;

        for (int i = 0; i < _rows.Count; i++)
        {
            if (!IsRowVisibleInCurvesView(i) || IsRowLocked(_rows[i])) continue;
            if (_rows[i].CurveTrack is not { } ct) continue;

            for (int k = 0; k < ct.Curve.Count; k++)
            {
                Keyframe kf = ct.Curve[k];
                float kx = xFromTime(kf.Time);
                float ky = yFromVal(kf.Value);
                if (MathF.Abs(localPos.X - kx) <= 8f && MathF.Abs(localPos.Y - ky) <= 8f)
                {
                    var pair = (i, k);
                    if (ctrl)
                    {
                        if (!_selectedKeys.Remove(pair))
                            _selectedKeys.Add(pair);
                    }
                    else if (shift)
                    {
                        _selectedKeys.Add(pair);
                    }
                    else if (!_selectedKeys.Contains(pair))
                    {
                        _selectedKeys.Clear();
                        _selectedKeys.Add(pair);
                    }

                    _selectedRowIndex = i;
                    _selectedKeyIndex = k;
                    _dragPart = DragPart.Keyframe;
                    _dragRow = i;
                    _dragKey = k;
                    _dragStartTime = kf.Time;
                    _dragStartValue = kf.Value;
                    CaptureMultiDragSnapshot();
                    if (IsClipValid) Undo.RecordObject(ClipAsset!, "Edit Curve Keyframes");
                    return;
                }
            }
        }

        // Unreal Ctrl + LMB Click on Curve Canvas: add keyframe at cursor (time, value)
        if (ctrl && IsClipValid)
        {
            int targetRow = FindTargetCurveRowForAdd();
            if (targetRow >= 0 && targetRow < _rows.Count && _rows[targetRow].CurveTrack != null && !IsRowLocked(_rows[targetRow]))
            {
                Undo.RecordObject(ClipAsset!, "Add Curve Keyframe");
                float t = timeFromX(localPos.X);
                float v = valFromY(localPos.Y);
                _clip!.SetCurveKey(_rows[targetRow].RelativePath, _rows[targetRow].ComponentType, _rows[targetRow].PropertyPath, t, v);
                EditorUtility.SetDirty(ClipAsset!);
                return;
            }
        }

        // LMB Drag on empty space -> Unreal Marquee Box Selection
        if (!shift && !ctrl)
        {
            _selectedKeys.Clear();
            _selectedKeyIndex = -1;
        }
        _dragPart = DragPart.MarqueeSelect;
        _marqueeStartLocal = localPos;
        _marqueeEndLocal = localPos;
    }

    private void OnDragCurves(Paper paper, DragEvent e, Func<float, float> timeFromX, Func<float, float> xFromTime,
        Func<float, float> valFromY, Func<float, float> yFromVal)
    {
        if (_dragPart == DragPart.Keyframe && IsClipValid && _dragRow >= 0 && _dragRow < _rows.Count)
        {
            float fps = MathF.Max(1f, _clip!.FrameRate);
            float rawTargetT = timeFromX(e.RelativePosition.X);
            float targetT = IsAltDown(paper) ? rawTargetT : MathF.Round(rawTargetT * fps) / fps;
            float dt = targetT - _dragStartTime;
            float dv = valFromY(e.RelativePosition.Y) - _dragStartValue;

            // Unreal Shift + Drag axis constraint in Curves view: lock to Time-only or Value-only
            if (IsShiftDown(paper))
            {
                float dxPx = MathF.Abs(e.RelativePosition.X - _dragPressLocalPos.X);
                float dyPx = MathF.Abs(e.RelativePosition.Y - _dragPressLocalPos.Y);
                if (dxPx >= dyPx) dv = 0f;
                else dt = 0f;
            }

            if (_multiDragSnapshot.Count <= 1)
            {
                TrackRow row = _rows[_dragRow];
                if (IsRowLocked(row) || row.CurveTrack == null || _dragKey < 0 || _dragKey >= row.CurveTrack.Curve.Count) return;

                Keyframe old = row.CurveTrack.Curve[_dragKey];
                _dragKey = row.CurveTrack.Curve.MoveKey(_dragKey, new Keyframe(MathF.Max(0f, _dragStartTime + dt), _dragStartValue + dv, old.InTangent, old.OutTangent, old.Interpolation));
                _selectedKeyIndex = _dragKey;
                _selectedKeys.Clear();
                _selectedKeys.Add((_dragRow, _dragKey));
                row.CurveTrack.Curve.SmoothTangents(CurveTangentMode.Auto);
            }
            else
            {
                _selectedKeys.Clear();
                for (int i = 0; i < _multiDragSnapshot.Count; i++)
                {
                    (int rIdx, int kIdx, float startT, float startV) = _multiDragSnapshot[i];
                    if (rIdx < 0 || rIdx >= _rows.Count) continue;
                    TrackRow r = _rows[rIdx];
                    if (IsRowLocked(r) || r.CurveTrack == null || kIdx < 0 || kIdx >= r.CurveTrack.Curve.Count) continue;

                    Keyframe old = r.CurveTrack.Curve[kIdx];
                    int newKeyIdx = r.CurveTrack.Curve.MoveKey(kIdx, new Keyframe(MathF.Max(0f, startT + dt), startV + dv, old.InTangent, old.OutTangent, old.Interpolation));
                    r.CurveTrack.Curve.SmoothTangents(CurveTangentMode.Auto);
                    _multiDragSnapshot[i] = (rIdx, newKeyIdx, startT, startV);
                    _selectedKeys.Add((rIdx, newKeyIdx));
                    if (rIdx == _dragRow)
                    {
                        _dragKey = newKeyIdx;
                        _selectedKeyIndex = newKeyIdx;
                    }
                }
            }

            _clip.Invalidate();
            EditorUtility.SetDirty(ClipAsset!);
            if (_previewing) SampleAtPlayhead();
        }
        else if (_dragPart == DragPart.MarqueeSelect)
        {
            _marqueeEndLocal = e.RelativePosition;
            UpdateCurvesMarqueeSelection(paper, xFromTime, yFromVal);
        }
    }

    private void UpdateCurvesMarqueeSelection(Paper paper, Func<float, float> xFromTime, Func<float, float> yFromVal)
    {
        float minX = MathF.Min(_marqueeStartLocal.X, _marqueeEndLocal.X);
        float maxX = MathF.Max(_marqueeStartLocal.X, _marqueeEndLocal.X);
        float minY = MathF.Min(_marqueeStartLocal.Y, _marqueeEndLocal.Y);
        float maxY = MathF.Max(_marqueeStartLocal.Y, _marqueeEndLocal.Y);

        if (!IsShiftDown(paper) && !IsCtrlDown(paper))
            _selectedKeys.Clear();

        for (int i = 0; i < _rows.Count; i++)
        {
            if (!IsRowVisibleInCurvesView(i)) continue;
            if (_rows[i].CurveTrack is not { } ct) continue;

            for (int k = 0; k < ct.Curve.Count; k++)
            {
                Keyframe kf = ct.Curve[k];
                float kx = xFromTime(kf.Time);
                float ky = yFromVal(kf.Value);
                if (kx >= minX && kx <= maxX && ky >= minY && ky <= maxY)
                {
                    _selectedKeys.Add((i, k));
                    _selectedRowIndex = i;
                    _selectedKeyIndex = k;
                }
            }
        }
    }

    private void OpenCurvesContextMenu(Paper paper, Float2 localPos, Func<float, float> timeFromX, Func<float, float> xFromTime)
    {
        float rangeV = MathF.Max(0.01f, _curveMaxVal - _curveMinVal);
        float clickTime = timeFromX(localPos.X);
        float clickVal = _curveMinVal + (1f - localPos.Y / MathF.Max(1f, _timelineHeight)) * rangeV;
        int targetRow = FindTargetCurveRowForAdd();

        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
        {
            if (targetRow >= 0 && targetRow < _rows.Count && _rows[targetRow].CurveTrack != null && !IsRowLocked(_rows[targetRow]))
            {
                TrackRow r = _rows[targetRow];
                menu.Item($"Add Keyframe to {r.Label} (Ctrl+LMB)", () =>
                {
                    Undo.RecordObject(ClipAsset!, "Add Curve Keyframe");
                    _clip!.SetCurveKey(r.RelativePath, r.ComponentType, r.PropertyPath, clickTime, clickVal);
                    EditorUtility.SetDirty(ClipAsset!);
                });
            }
            if (_selectedKeys.Count > 0 || (_selectedRowIndex >= 0 && _selectedKeyIndex >= 0))
            {
                menu.Item(_selectedKeys.Count > 1 ? $"Delete {_selectedKeys.Count} Selected Keyframes (Del)" : "Delete Selected Keyframe (Del)",
                    DeleteSelectedKeysOrEvent, danger: true);
            }
            menu.Separator();
            menu.Item(_selectedKeys.Count > 0 ? "Frame Selected Keys (F)" : "Frame All (F)", FrameSelectedOrAll);
        });
    }

    private void PaintCurvesView(Canvas canvas, Rect rect, Scribe.FontFile font)
    {
        float x = (float)rect.Min.X, y = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        float rangeT = MathF.Max(0.05f, _viewMaxTime - _viewMinTime);
        float rangeV = MathF.Max(0.01f, _curveMaxVal - _curveMinVal);

        canvas.SaveState();

        // Grid
        float stepT = CurveRenderer.GetNiceStep(rangeT, w, 65f);
        float stepV = CurveRenderer.GetNiceStep(rangeV, h, 45f);

        canvas.SetStrokeColor(Color32.FromArgb(20, 255, 255, 255));
        canvas.SetStrokeWidth(1f);
        for (float t = MathF.Ceiling(_viewMinTime / stepT) * stepT; t <= _viewMaxTime; t += stepT)
        {
            float gx = x + (t - _viewMinTime) / rangeT * w;
            canvas.BeginPath(); canvas.MoveTo(gx, y); canvas.LineTo(gx, y + h); canvas.Stroke();
        }
        for (float v = MathF.Ceiling(_curveMinVal / stepV) * stepV; v <= _curveMaxVal; v += stepV)
        {
            float gy = y + h - (v - _curveMinVal) / rangeV * h;
            canvas.BeginPath(); canvas.MoveTo(x, gy); canvas.LineTo(x + w, gy); canvas.Stroke();
            canvas.DrawText(v.ToString("G3"), x + 4f, gy - 12f, Color32.FromArgb(100, 200, 200, 210), 9f, font);
        }

        // Zero axis
        if (_curveMinVal < 0f && _curveMaxVal > 0f)
        {
            float zy = y + h - (-_curveMinVal) / rangeV * h;
            canvas.SetStrokeColor(Color32.FromArgb(55, 255, 255, 255));
            canvas.BeginPath(); canvas.MoveTo(x, zy); canvas.LineTo(x + w, zy); canvas.Stroke();
        }

        // Draw curves
        const int Steps = 140;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (!IsRowVisibleInCurvesView(i)) continue;
            TrackRow row = _rows[i];
            if (row.CurveTrack is not { } ct || ct.Curve.Count == 0) continue;

            AnimationCurve curve = ct.Curve;
            canvas.SetStrokeColor(EditorTheme.ToColor32(row.AccentColor, 1f));
            canvas.SetStrokeWidth(i == _selectedRowIndex ? 2f : 1.5f);
            canvas.BeginPath();
            for (int s = 0; s <= Steps; s++)
            {
                float t = _viewMinTime + rangeT * s / Steps;
                float v = curve.Evaluate(t);
                float px = x + (t - _viewMinTime) / rangeT * w;
                float py = y + h - (v - _curveMinVal) / rangeV * h;
                if (s == 0) canvas.MoveTo(px, py); else canvas.LineTo(px, py);
            }
            canvas.Stroke();

            // Keyframe dots
            for (int k = 0; k < curve.Count; k++)
            {
                Keyframe kf = curve[k];
                float kx = x + (kf.Time - _viewMinTime) / rangeT * w;
                float ky = y + h - (kf.Value - _curveMinVal) / rangeV * h;
                bool isSel = IsKeySelected(_selectedKeys, i, k, _selectedRowIndex, _selectedKeyIndex);
                PaintDiamond(canvas, kx, ky, isSel ? 5.5f : 4f, isSel ? EditorTheme.Amber400 : row.AccentColor, isSel);
            }
        }

        // Playhead
        float playX = x + (_currentTime - _viewMinTime) / rangeT * w;
        if (playX >= x && playX <= x + w)
        {
            Color headCol = _recording ? EditorTheme.Red400 : EditorTheme.Accent;
            canvas.RectFilled(playX - 1f, y, 2f, h, EditorTheme.ToColor32(headCol, 0.95f));
        }

        // Unreal-style Marquee Selection Box
        if (_dragPart == DragPart.MarqueeSelect)
            PaintMarqueeBox(canvas, x, y);

        canvas.RestoreState();
    }

    // ================================================================
    //  Bottom Bar: Dope Sheet / Curves Switcher + Selected Key/Event Inspector
    // ================================================================

    private void DrawBottomBar(Paper paper, Scribe.FontFile font, float width)
    {
        OrigamiMetrics m = Origami.Current.Metrics;

        using (paper.Row("anim_bottom").Width(width).Height(BottomBarHeight)
            .Padding(8, 8, 0, 0).Gap(8).AlignItems(LayoutAlignment.Center)
            .BackgroundColor(EditorTheme.Neutral200).Enter())
        {
            // View mode switcher: Dope Sheet vs Curves
            EditorGUI.Chip(paper, "anim_mode_dope", "Dope Sheet", !_curvesMode, () => _curvesMode = false, height: 20f);
            EditorGUI.Chip(paper, "anim_mode_curves", "Curves", _curvesMode, () => { _curvesMode = true; _curveBoundsInitialized = false; }, height: 20f);

            if (_curvesMode)
            {
                Origami.Button(paper, "anim_curve_fit", "Fit", () =>
                {
                    _curveBoundsInitialized = false;
                    FitTimelineRange();
                }).Small().Height(20).Show();
            }

            if (IsClipValid)
            {
                paper.Box("anim_bdiv1").Width(1).Height(16).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

                // Wrap Mode inline selector
                using (paper.Box("anim_wrap_w").Width(105).Height(20).Enter())
                {
                    Origami.EnumDropdown(paper, "anim_wrap_dd", _clip!.WrapMode, v =>
                    {
                        Undo.RecordObject(ClipAsset!, "Change Wrap Mode");
                        _clip.WrapMode = v;
                        _clip.Invalidate();
                        EditorUtility.SetDirty(ClipAsset!);
                    }).Show();
                }

                // Sequencer Completion Mode selector (KeepState vs RestoreState)
                using (paper.Box("anim_comp_w").Width(105).Height(20).Enter())
                {
                    Origami.EnumDropdown(paper, "anim_comp_dd", _clip!.CompletionMode, v =>
                    {
                        Undo.RecordObject(ClipAsset!, "Change Completion Mode");
                        _clip.CompletionMode = v;
                        _clip.Invalidate();
                        EditorUtility.SetDirty(ClipAsset!);
                    }).Show();
                }

                // Sequencer In / Out working loop range toggle + range editors
                EditorGUI.Chip(paper, "anim_work_range", _useWorkRange ? $"In/Out [{_workRangeIn:0.##}..{_workRangeOut:0.##}s]" : "In/Out", _useWorkRange, () =>
                {
                    _useWorkRange = !_useWorkRange;
                    if (_useWorkRange && _workRangeOut <= _workRangeIn + 1e-4f)
                    {
                        _workRangeIn = 0f;
                        _workRangeOut = MathF.Max(0.25f, _clip!.Duration > 0f ? _clip.Duration : 1f);
                    }
                }, height: 20f);

                // Selected Event inline editor
                if (_selectedEventIndex >= 0 && _selectedEventIndex < _clip.PropertyEvents.Count)
                {
                    DrawSelectedEventFooter(paper, font, _clip.PropertyEvents[_selectedEventIndex]);
                }
                else if (_selectedRowIndex >= 0 && _selectedRowIndex < _rows.Count && _selectedKeyIndex >= 0)
                {
                    DrawSelectedKeyFooter(paper, font, _rows[_selectedRowIndex], _selectedKeyIndex);
                }
            }

            paper.Box("anim_bot_sp").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();
        }
    }

    private void DrawSelectedEventFooter(Paper paper, Scribe.FontFile font, SceneAnimationEvent ev)
    {
        if (!IsClipValid) return;

        bool isRanged = ev.IsRange;

        // Instant vs Range mode chip
        EditorGUI.Chip(paper, "anim_ev_range_toggle", isRanged ? "Range (Start/End)" : "Instant", isRanged, () =>
        {
            Undo.RecordObject(ClipAsset!, "Toggle Event Range Mode");
            if (isRanged)
            {
                ev.EndTime = ev.Time;
            }
            else
            {
                float step = MathF.Max(0.25f, 1f / MathF.Max(1f, _clip!.FrameRate));
                ev.EndTime = ev.Time + step;
                if (string.IsNullOrEmpty(ev.ExitFunctionName))
                    ev.ExitFunctionName = "OnEventExit";
            }
            _clip!.Invalidate();
            EditorUtility.SetDirty(ClipAsset!);
        }, height: 20f);

        // Start Time
        using (paper.Box("anim_ev_t0_w").Width(58).Height(20).Enter())
        {
            Origami.NumericField<float>(paper, "anim_ev_t0", ev.Time, v =>
            {
                Undo.RecordObject(ClipAsset!, "Edit Event Start Time");
                float span = ev.Duration;
                ev.Time = MathF.Max(0f, v);
                if (isRanged) ev.EndTime = MathF.Max(ev.Time + 0.01f, ev.EndTime);
                else ev.EndTime = ev.Time;
                _clip!.Invalidate();
                EditorUtility.SetDirty(ClipAsset!);
            }).Format("0.##").Suffix("s").Height(20).Show();
        }

        if (isRanged)
        {
            paper.Box("anim_ev_to_lbl").Width(UnitValue.Auto).Height(20).IsNotInteractable()
                .Text("->", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

            using (paper.Box("anim_ev_t1_w").Width(58).Height(20).Enter())
            {
                Origami.NumericField<float>(paper, "anim_ev_t1", ev.EndTime, v =>
                {
                    Undo.RecordObject(ClipAsset!, "Edit Event End Time");
                    ev.EndTime = MathF.Max(ev.Time + 0.01f, v);
                    _clip!.Invalidate();
                    EditorUtility.SetDirty(ClipAsset!);
                }).Format("0.##").Suffix("s").Height(20).Show();
            }
        }

        using (paper.Box("anim_ev_fn_w").Width(isRanged ? 115 : 140).Height(20).Enter())
        {
            Origami.TextField(paper, "anim_ev_fn", ev.FunctionName, v =>
            {
                Undo.RecordObject(ClipAsset!, "Edit Animation Event");
                ev.FunctionName = v.Trim();
                EditorUtility.SetDirty(ClipAsset!);
            }).Placeholder(isRanged ? "EnterFunction" : "FunctionName").Height(20).CommitOnEnter().Show();
        }

        if (isRanged)
        {
            using (paper.Box("anim_ev_stayfn_w").Width(115).Height(20).Enter())
            {
                Origami.TextField(paper, "anim_ev_stayfn", ev.StayFunctionName, v =>
                {
                    Undo.RecordObject(ClipAsset!, "Edit Event Stay Function");
                    ev.StayFunctionName = v.Trim();
                    EditorUtility.SetDirty(ClipAsset!);
                }).Placeholder("StayFunction (0..1)").Height(20).CommitOnEnter().Show();
            }

            using (paper.Box("anim_ev_exitfn_w").Width(115).Height(20).Enter())
            {
                Origami.TextField(paper, "anim_ev_exitfn", ev.ExitFunctionName, v =>
                {
                    Undo.RecordObject(ClipAsset!, "Edit Event Exit Function");
                    ev.ExitFunctionName = v.Trim();
                    EditorUtility.SetDirty(ClipAsset!);
                }).Placeholder("ExitFunction").Height(20).CommitOnEnter().Show();
            }
        }

        using (paper.Box("anim_ev_argtype_w").Width(76).Height(20).Enter())
        {
            Origami.EnumDropdown(paper, "anim_ev_argtype", ev.ArgType, v =>
            {
                Undo.RecordObject(ClipAsset!, "Edit Event Arg Type");
                ev.ArgType = v;
                EditorUtility.SetDirty(ClipAsset!);
            }).Show();
        }

        switch (ev.ArgType)
        {
            case ProwlActionArgType.Float:
                using (paper.Box("anim_ev_f_w").Width(58).Height(20).Enter())
                    Origami.NumericField<float>(paper, "anim_ev_f", ev.FloatParam, v => { Undo.RecordObject(ClipAsset!, "Edit Event Param"); ev.FloatParam = v; EditorUtility.SetDirty(ClipAsset!); }).Height(20).Show();
                break;
            case ProwlActionArgType.Int:
                using (paper.Box("anim_ev_i_w").Width(58).Height(20).Enter())
                    Origami.NumericField<int>(paper, "anim_ev_i", ev.IntParam, v => { Undo.RecordObject(ClipAsset!, "Edit Event Param"); ev.IntParam = v; EditorUtility.SetDirty(ClipAsset!); }).Height(20).Show();
                break;
            case ProwlActionArgType.String:
                using (paper.Box("anim_ev_s_w").Width(86).Height(20).Enter())
                    Origami.TextField(paper, "anim_ev_s", ev.StringParam, v => { Undo.RecordObject(ClipAsset!, "Edit Event Param"); ev.StringParam = v; EditorUtility.SetDirty(ClipAsset!); }).Height(20).CommitOnEnter().Show();
                break;
            case ProwlActionArgType.Bool:
                Origami.Checkbox(paper, "anim_ev_b", ev.BoolParam, v => { Undo.RecordObject(ClipAsset!, "Edit Event Param"); ev.BoolParam = v; EditorUtility.SetDirty(ClipAsset!); }).NoLabel().Show();
                break;
            case ProwlActionArgType.Object:
            {
                string objLabel = ev.ObjectParam.IsValid() ? ev.ObjectParam!.Name : "None";
                Origami.Button(paper, "anim_ev_obj", objLabel, () =>
                {
                    SelectorModal.Open("Select Event Object Argument", typeof(EngineObject), SelectorTabs.Assets | SelectorTabs.Scene, picked =>
                    {
                        if (!IsClipValid) return;
                        Undo.RecordObject(ClipAsset!, "Edit Event Object Param");
                        ev.ObjectParam = picked as EngineObject;
                        EditorUtility.SetDirty(ClipAsset!);
                    });
                }).Small().Height(20).Show();
                break;
            }
        }

        using (paper.Box("anim_ev_path_w").Width(95).Height(20).Enter())
        {
            Origami.TextField(paper, "anim_ev_path", ev.RelativePath, v =>
            {
                Undo.RecordObject(ClipAsset!, "Edit Event Target Path");
                ev.RelativePath = v.Trim();
                EditorUtility.SetDirty(ClipAsset!);
            }).Placeholder("Target (Root)").Height(20).CommitOnEnter().Show();
        }

        Origami.Button(paper, "anim_ev_del", "Delete", () =>
        {
            Undo.RecordObject(ClipAsset!, "Delete Animation Event");
            _clip!.PropertyEvents.RemoveAt(_selectedEventIndex);
            _selectedEventIndex = -1;
            _clip.Invalidate();
            EditorUtility.SetDirty(ClipAsset!);
        }).Danger().Small().Height(20).Show();
    }

    private void DrawSelectedKeyFooter(Paper paper, Scribe.FontFile font, TrackRow row, int keyIndex)
    {
        if (!IsClipValid) return;

        if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null && keyIndex < row.CurveTrack.Curve.Count)
        {
            Keyframe kf = row.CurveTrack.Curve[keyIndex];
            paper.Box("anim_kf_lbl").Width(UnitValue.Auto).Height(20).IsNotInteractable()
                .Text($"Key @ {kf.Time:0.00}s =", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            using (paper.Box("anim_kf_val_w").Width(72).Height(20).Enter())
            {
                Origami.NumericField<float>(paper, "anim_kf_val", kf.Value, newV =>
                {
                    Undo.RecordObject(ClipAsset!, "Edit Keyframe Value");
                    row.CurveTrack.Curve.MoveKey(keyIndex, new Keyframe(kf.Time, newV, kf.InTangent, kf.OutTangent, kf.Interpolation));
                    _clip!.Invalidate();
                    EditorUtility.SetDirty(ClipAsset!);
                    if (_previewing) SampleAtPlayhead();
                }).Format("0.###").Height(20).Show();
            }

            Origami.Button(paper, "anim_kf_del", "Delete Key", () => DeleteKeyFromRow(row, keyIndex)).Danger().Small().Height(20).Show();
        }
        else if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null && keyIndex < row.ObjectTrack.Keyframes.Count)
        {
            SceneObjectKeyframe kf = row.ObjectTrack.Keyframes[keyIndex];
            string objName = kf.Value.IsValid() ? kf.Value!.Name : "None";
            Origami.Button(paper, "anim_okf_pick", $"Object @ {kf.Time:0.00}s: {objName}", () => OpenObjectKeyPicker(row, kf.Time))
                .Small().Height(20).Show();
            Origami.Button(paper, "anim_okf_del", "Delete Key", () => DeleteKeyFromRow(row, keyIndex)).Danger().Small().Height(20).Show();
        }
        else if (row.Kind == TrackRowKind.String && row.StringTrack != null && keyIndex < row.StringTrack.Keyframes.Count)
        {
            SceneStringKeyframe kf = row.StringTrack.Keyframes[keyIndex];
            paper.Box("anim_skf_lbl").Width(UnitValue.Auto).Height(20).IsNotInteractable()
                .Text($"String @ {kf.Time:0.00}s =", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            using (paper.Box("anim_skf_val_w").Width(120).Height(20).Enter())
            {
                Origami.TextField(paper, "anim_skf_val", kf.Value, newV =>
                {
                    Undo.RecordObject(ClipAsset!, "Edit String Keyframe");
                    kf.Value = newV ?? string.Empty;
                    _clip!.Invalidate();
                    EditorUtility.SetDirty(ClipAsset!);
                    if (_previewing) SampleAtPlayhead();
                }).Height(20).CommitOnEnter().Show();
            }

            Origami.Button(paper, "anim_skf_del", "Delete Key", () => DeleteKeyFromRow(row, keyIndex)).Danger().Small().Height(20).Show();
        }
    }
}
