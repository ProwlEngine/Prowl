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
using Prowl.Editor.Prefabs;
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

    /// <summary>Opens the Animation panel focused on a specific event inside <paramref name="clip"/>.</summary>
    public static void OpenClipEvent(AnimationClip clip, int eventIndex)
    {
        AnimationPanel? panel = EditorApplication.Instance?.OpenPanel<AnimationPanel>();
        if (panel == null) return;
        panel.SelectClip(clip);
        panel.SelectEvent(eventIndex);
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
    //  Editor Preferences Shortcuts (Preferences -> Shortcuts -> Animation)
    // ================================================================

    public const string ShortcutPlayPause = "Animation/PlayPause";
    public const string ShortcutFirstFrame = "Animation/FirstFrame";
    public const string ShortcutPreviousFrame = "Animation/PreviousFrame";
    public const string ShortcutNextFrame = "Animation/NextFrame";
    public const string ShortcutLastFrame = "Animation/LastFrame";
    public const string ShortcutTogglePreview = "Animation/TogglePreview";
    public const string ShortcutToggleRecord = "Animation/ToggleRecord";
    public const string ShortcutKeyAtPlayhead = "Animation/KeyAtPlayhead";
    public const string ShortcutAddEvent = "Animation/AddEvent";
    public const string ShortcutAddRangedEvent = "Animation/AddRangedEvent";
    public const string ShortcutDelete = "Animation/Delete";
    public const string ShortcutSelectAll = "Animation/SelectAll";
    public const string ShortcutCopy = "Animation/Copy";
    public const string ShortcutPaste = "Animation/Paste";
    public const string ShortcutDuplicate = "Animation/Duplicate";
    public const string ShortcutFrameSelection = "Animation/FrameSelection";
    public const string ShortcutToggleCurvesView = "Animation/ToggleCurvesView";
    public const string ShortcutSetWorkRangeIn = "Animation/SetWorkRangeIn";
    public const string ShortcutSetWorkRangeOut = "Animation/SetWorkRangeOut";
    public const string ShortcutToggleWorkRange = "Animation/ToggleWorkRange";

    [InitializeOnLoad]
    internal static void RegisterShortcuts()
    {
        ShortcutManager.LoadOverrides();

        ShortcutManager.Register(ShortcutPlayPause, "Play / Pause", KeyCode.Space);
        ShortcutManager.Register(ShortcutFirstFrame, "First Frame", KeyCode.Home);
        ShortcutManager.Register(ShortcutPreviousFrame, "Previous Frame", KeyCode.Comma);
        ShortcutManager.Register(ShortcutNextFrame, "Next Frame", KeyCode.Period);
        ShortcutManager.Register(ShortcutLastFrame, "Last Frame", KeyCode.End);
        ShortcutManager.Register(ShortcutTogglePreview, "Toggle Preview", KeyCode.P);
        ShortcutManager.Register(ShortcutToggleRecord, "Toggle Auto-Key Record", KeyCode.R);
        ShortcutManager.Register(ShortcutKeyAtPlayhead, "Key Selected / All at Playhead", KeyCode.K);
        ShortcutManager.Register(ShortcutAddEvent, "Add Animation Event", KeyCode.E);
        ShortcutManager.Register(ShortcutAddRangedEvent, "Add Ranged Animation Event", KeyCode.E, shift: true);
        ShortcutManager.Register(ShortcutDelete, "Delete Selected", KeyCode.Delete);
        ShortcutManager.Register(ShortcutSelectAll, "Select All Keys", KeyCode.A, ctrl: true);
        ShortcutManager.Register(ShortcutCopy, "Copy Selected Keys", KeyCode.C, ctrl: true);
        ShortcutManager.Register(ShortcutPaste, "Paste Keys", KeyCode.V, ctrl: true);
        ShortcutManager.Register(ShortcutDuplicate, "Duplicate Selected Keys", KeyCode.D, ctrl: true);
        ShortcutManager.Register(ShortcutFrameSelection, "Frame Selected / All", KeyCode.F);
        ShortcutManager.Register(ShortcutToggleCurvesView, "Toggle Dope Sheet / Curves", KeyCode.C);
        ShortcutManager.Register(ShortcutSetWorkRangeIn, "Set Work Range In", KeyCode.LeftBracket);
        ShortcutManager.Register(ShortcutSetWorkRangeOut, "Set Work Range Out", KeyCode.RightBracket);
        ShortcutManager.Register(ShortcutToggleWorkRange, "Toggle Work Range Loop", KeyCode.BackSlash);

        EnsureGlobalHooksRegistered();
    }

    private static readonly List<AnimationPanel> s_openPanels = [];
    private static bool s_globalHooksRegistered;

    private static void EnsureGlobalHooksRegistered()
    {
        if (s_globalHooksRegistered) return;
        s_globalHooksRegistered = true;

        // Registered before scene/prefab save handlers so temporary preview poses are restored
        // before PrefabEditingMode.Save() or EditorSceneManager.Save() / PrefabUtility.ReconcileSection() run.
        SaveManager.OnSave += OnBeforeEditorSave;
        EditorApplication.PlayModeRequested += OnPlayModeRequested;
    }

    private static string? OnBeforeEditorSave()
    {
        if (!Application.IsPlaying)
            SuspendAllOpenPanelPreviewsForSave();
        return null;
    }

    private static void OnPlayModeRequested(PlayModeRequest request)
    {
        if (!request.Entering) return;
        for (int i = 0; i < s_openPanels.Count; i++)
        {
            AnimationPanel panel = s_openPanels[i];
            if (panel._previewing)
                panel.StopPreviewAndRestore();
        }
    }

    internal static void SuspendAllOpenPanelPreviewsForSave()
    {
        for (int i = 0; i < s_openPanels.Count; i++)
            s_openPanels[i].SuspendPreviewForSave();
    }

    private static string ShortcutKeys(string id)
        => ShortcutManager.GetBinding(id) is { } binding ? ShortcutManager.GetDisplayString(binding) : "";

    // ================================================================
    //  Target & Clip State
    // ================================================================

    private GameObject? _targetRoot;
    private Animator? _animator;
    private AnimationClip? _clip;
    private bool _lockTarget;

    private AnimationClip? ClipAsset => _clip;
    private bool IsClipValid => _clip.IsValid();

    internal GameObject? TargetRoot => _targetRoot;
    internal Animator? BoundAnimator => _animator;
    internal AnimationClip? ActiveClip => _clip;
    internal bool NeedsAnimatorCreation => _targetRoot.IsValid() && _animator.IsNotValid();
    internal bool IsRecording => _recording;
    internal bool HasUnsavedClipChanges => IsClipValid &&
        (_unsavedClips.Contains(ClipAsset!) || EditorUtility.IsDirty(ClipAsset!));

    // Playback / Preview / Record state
    private bool _previewing;
    private bool _recording;
    private bool _playing;
    private bool _wasPlayingLastFrame;
    private bool _resumePreviewNextFrame;
    private bool _resumeRecordingNextFrame;
    private bool _resumePlayingNextFrame;
    private float _currentTime;
    private float _previewElapsedTime;
    private readonly HashSet<Guid> _savedDuringPlayMode = [];
    private readonly HashSet<AnimationClip> _unsavedClips = new(ReferenceEqualityComparer.Instance);

    // View mode: false = Dope Sheet, true = Curves
    private bool _curvesMode;

    // Left panel width
    private const float LeftPaneWidth = 340f;
    private const float HeaderBarHeight = 32f;
    private const float TimeRulerHeight = 26f;
    private const float EventBarHeight = 22f;
    private const float TrackRowHeight = 24f;
    private const float BottomBarHeight = 26f;
    private const float InstantEventDrawerHeight = 92f;
    private const float RangedEventDrawerHeight = 148f;

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

    // Synchronized vertical scroll state shared between the Left Property List and the Right Dope Sheet Timeline
    private float _trackScrollY;
    private float _trackListViewportHeight = 200f;
    private float _scrollbarDragStartPointerY;
    private float _scrollbarDragStartScrollY;

    private float MaxTrackScrollY => MathF.Max(0f, _rows.Count * TrackRowHeight - _trackListViewportHeight);

    private void ClampTrackScrollY() => _trackScrollY = Math.Clamp(_trackScrollY, 0f, MaxTrackScrollY);

    private void ScrollTrackListBy(float deltaPixels)
        => _trackScrollY = Math.Clamp(_trackScrollY + deltaPixels, 0f, MaxTrackScrollY);

    // Selected track and keyframe (plus multi-keyframe selection set for Unreal-style marquee & group dragging)
    private int _selectedRowIndex = -1;
    private readonly HashSet<int> _selectedTrackRows = [];
    private int _selectedKeyIndex = -1;
    private int _selectedEventIndex = -1;
    private readonly HashSet<(int RowIndex, int KeyIndex)> _selectedKeys = [];
    private readonly Dictionary<SceneCurveTrack, HashSet<int>> _selectedDirectCurveKeys = new(ReferenceEqualityComparer.Instance);
    private SceneCurveTrack? _dragTangentTrack;

    internal readonly record struct VisibleCurveChannel(
        SceneCurveTrack Track,
        string Label,
        Color Color,
        int RowIndex,
        bool IsLocked);

    // Collision-free per-track snapshots captured at the start of a multi-key drag
    private readonly List<(SceneCurveTrack Track, Keyframe[] OriginalKeys, HashSet<int> DraggedIndices)> _dragCurveSnapshots = [];
    private readonly List<(SceneObjectTrack Track, SceneObjectKeyframe[] OriginalKeys, HashSet<int> DraggedIndices)> _dragObjectSnapshots = [];
    private readonly List<(SceneStringTrack Track, SceneStringKeyframe[] OriginalKeys, HashSet<int> DraggedIndices)> _dragStringSnapshots = [];

    // Multi-key clipboard (times stored as relative offsets from the earliest copied key)
    private readonly List<(string RelPath, string CompType, string PropPath, float RelTime, Keyframe Key)> _copiedCurveKeys = [];
    private readonly List<(string RelPath, string CompType, string PropPath, float RelTime, EngineObject? Value)> _copiedObjectKeys = [];
    private readonly List<(string RelPath, string CompType, string PropPath, float RelTime, string Value)> _copiedStringKeys = [];
    private float _batchTimeOffsetInput;
    private float _batchValueOffsetInput;

    // Keyframe / Playhead / Marquee / Tangent / Time-Stretch dragging state
    private enum DragPart { None, Playhead, Keyframe, TangentIn, TangentOut, StretchLeft, StretchRight, MarqueeSelect, Event, EventStart, EventEnd, WorkRangeIn, WorkRangeOut }
    private DragPart _dragPart;
    private int _dragRow = -1;
    private int _dragKey = -1;
    private float _dragStartTime;
    private float _dragEndTime;
    private float _dragClickOffset;
    private float _dragStartValue;
    private float _stretchAnchorTime;
    private float _stretchOrigSpan;
    private Float2 _dragPressLocalPos;
    private Float2 _marqueeStartLocal;
    private Float2 _marqueeEndLocal;
    private float _timelineWidth = 400f;
    private float _timelineHeight = 240f;

    // Unreal Engine Sequencer RMB / MMB navigation state (RMB drag = Pan, Alt+RMB drag = Zoom, RMB click = Context Menu, MMB drag = Pan / Scrub)
    private Rect _panelScreenRect;
    private Rect _canvasScreenRect;
    private Rect _rulerScreenRect;
    private bool _rmbPanActive;
    private bool _rmbZoomActive;
    private bool _rmbMoved;
    private bool _mmbPanActive;
    private Float2 _rmbPressScreenPos;
    private Float2 _rmbLastScreenPos;
    private Float2 _rmbPivotNorm;

    // Add Property popup state (split Hierarchy Tree + Component Properties view + multi-token search)
    private bool _addPropertyOpen;
    private string _addPropertySearch = "";
    private int _addPropertySearchHighlight;
    private string _addPropertyLastHighlightSearch = "";
    private int _addPropertySelectedNodeIndex;
    private readonly HashSet<int> _addPropertyCollapsedNodes = [];
    private readonly HashSet<string> _addPropertyCollapsedComponents = new(StringComparer.Ordinal);
    private readonly HashSet<(string RelPath, string CompType, string BaseProp)> _addPropertyAnimatedSet = [];
    private List<AddPropertyNode> _addPropertyNodes = [];

    internal sealed class AddPropertyNode
    {
        public required int Index;
        public required int ParentIndex;
        public required GameObject? TargetObject;
        public required string RelativePath;
        public required string DisplayName;
        public required string Breadcrumb;
        public required int Depth;
        public required bool IsExternalSlot;
        public bool HasChildren;
        public required List<AddPropertyItem> Properties;
    }

    internal readonly record struct AddPropertyItem(
        string ComponentGroup,
        string PropertyLabel,
        string Icon,
        AnimatablePropertyDescriptor Descriptor);

    // Baseline snapshots taken when Preview / Record mode starts so stopping Preview restores the scene
    private readonly Dictionary<(GameObject Go, string ComponentType, string BasePath), (object? Value, Type? MemberType)> _previewBaseline = [];
    private SceneClipBinding? _cachedPreviewBinding;
    private GameObject? _cachedPreviewRoot;
    private AnimationClip? _cachedPreviewClip;
    private int _cachedPreviewVersion = -1;

    // Cached skeletal rig binding & pose so previewing imported clips is allocation-free per frame
    private AnimatorBinding? _cachedRigBinding;
    private Motion.Pose? _cachedRigPose;
    private GameObject? _cachedRigRoot;
    private Motion.Skeleton? _cachedRigSkeleton;

    // Observed clip version for syncing Undo/Redo and Inspector edits back to the live preview & selection
    private AnimationClip? _lastObservedClip;
    private int _lastObservedClipVersion = -1;

    // Cached component event method reflection for the Event Setup Drawer
    private List<AnimatableEventMethodDescriptor> _cachedEventMethods = [];
    private GameObject? _cachedEventMethodsTarget;
    private long _cachedEventMethodsTick;

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
        public bool IsMissing;
        public string GroupKey = string.Empty;
        public List<SceneCurveTrack>? GroupTracks;
        public List<float>? SummaryTimes;
        public SceneCurveTrack? CurveTrack;
        public SceneObjectTrack? ObjectTrack;
        public SceneStringTrack? StringTrack;
    }

    private readonly List<TrackRow> _rows = [];
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<(string RelPath, string CompType, string PropPath), bool> _missingCache = [];
    private GameObject? _missingCacheRoot;
    private long _missingCacheTick;

    public override void OnClosed()
    {
        StopPreviewAndRestore();
        UnsubscribeUndoHook();
    }

    internal void EnsureUndoHook()
    {
        EnsureGlobalHooksRegistered();
        if (!s_openPanels.Contains(this))
            s_openPanels.Add(this);
        if (_subscribedUndo) return;
        Undo.postprocessModifications += OnPostprocessUndoModifications;
        _subscribedUndo = true;
    }

    private void UnsubscribeUndoHook()
    {
        s_openPanels.Remove(this);
        if (!_subscribedUndo) return;
        Undo.postprocessModifications -= OnPostprocessUndoModifications;
        _subscribedUndo = false;
    }

    private void ResetSelectionAndDragState()
    {
        _dragPart = DragPart.None;
        _dragTangentTrack = null;
        _dragCurveSnapshots.Clear();
        _dragObjectSnapshots.Clear();
        _dragStringSnapshots.Clear();
        _selectedRowIndex = -1;
        _selectedTrackRows.Clear();
        _selectedKeyIndex = -1;
        _selectedEventIndex = -1;
        _selectedKeys.Clear();
        _selectedDirectCurveKeys.Clear();
        _curveBoundsInitialized = false;
        _trackScrollY = 0f;
    }

    private void ClearPreviewBindingCaches()
    {
        _cachedPreviewBinding = null;
        _cachedPreviewRoot = null;
        _cachedPreviewClip = null;
        _cachedPreviewVersion = -1;
        _cachedRigBinding = null;
        _cachedRigPose = null;
        _cachedRigRoot = null;
        _cachedRigSkeleton = null;
    }

    /// <summary>Selects an explicit clip to edit in the window.</summary>
    public void SelectClip(AnimationClip clip)
    {
        if (ReferenceEquals(_clip, clip)) return;
        StopPreviewAndRestore();
        _clip = clip;
        _currentTime = 0f;
        _previewElapsedTime = 0f;
        ResetSelectionAndDragState();
        _lastObservedClip = clip;
        _lastObservedClipVersion = clip.IsValid() ? clip.Version : -1;
        FitTimelineRange();
    }

    private void ClearSelectedClip()
    {
        if (!IsClipValid && ReferenceEquals(_clip, null)) return;
        StopPreviewAndRestore();
        _clip = null;
        _currentTime = 0f;
        _previewElapsedTime = 0f;
        _lastObservedClip = null;
        _lastObservedClipVersion = -1;
        ResetSelectionAndDragState();
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
        HandlePlayModeTransitions();
        SyncTargetFromSelection();
        ResumePreviewAfterSaveIfNeeded();
        TickPlayback((float)Time.UnscaledDeltaTime);

        if (_recording && _targetRoot.IsValid() && IsClipValid)
            PollAutoKeyChanges();

        RebuildRows();
        SyncExternalClipVersionChanges();
        HandlePanelShortcuts(paper);

        using (paper.Column("anim_root").Size(width, height).Clip()
            .OnPostLayout((_, rect) => _panelScreenRect = rect).Enter())
        {
            DrawTopTransportBar(paper, font, width);
            EditorGUI.Divider(paper, "anim_top_div");

            if (!IsClipValid || NeedsAnimatorCreation)
            {
                DrawEmptyOrSetupState(paper, font, width, height - HeaderBarHeight - 1f);
                return;
            }

            bool hasSelectedEvent = _selectedEventIndex >= 0 && _selectedEventIndex < _clip!.PropertyEvents.Count;
            float desiredDrawerHeight = hasSelectedEvent
                ? (_clip!.PropertyEvents[_selectedEventIndex].IsRange ? RangedEventDrawerHeight : InstantEventDrawerHeight)
                : 0f;

            float availMiddle = MathF.Max(80f, height - HeaderBarHeight - BottomBarHeight - 2f);
            float minBodyHeight = TimeRulerHeight + EventBarHeight + 1f;
            float eventDrawerHeight = hasSelectedEvent
                ? MathF.Min(desiredDrawerHeight, MathF.Max(68f, availMiddle - minBodyHeight - 1f))
                : 0f;

            float bodyHeight = MathF.Max(minBodyHeight, availMiddle - (hasSelectedEvent ? eventDrawerHeight + 1f : 0f));
            using (paper.Row("anim_body").Width(width).Height(bodyHeight).Clip().Enter())
            {
                DrawLeftTrackList(paper, font, LeftPaneWidth, bodyHeight);
                paper.Box("anim_vdiv").Width(1).Height(bodyHeight).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
                DrawRightTimelineArea(paper, font, MathF.Max(100f, width - LeftPaneWidth - 1f), bodyHeight);
            }

            if (hasSelectedEvent)
            {
                EditorGUI.Divider(paper, "anim_ev_drawer_div");
                DrawSelectedEventInspector(paper, font, width, eventDrawerHeight, desiredDrawerHeight, _clip!.PropertyEvents[_selectedEventIndex]);
            }

            EditorGUI.Divider(paper, "anim_bot_div");
            DrawBottomBar(paper, font, width);
        }
    }

    // ================================================================
    //  Selection & Context Synchronization
    // ================================================================

    private void SyncExternalClipVersionChanges()
    {
        if (!IsClipValid)
        {
            _lastObservedClip = null;
            _lastObservedClipVersion = -1;
            return;
        }

        if (!ReferenceEquals(_lastObservedClip, _clip))
        {
            _lastObservedClip = _clip;
            _lastObservedClipVersion = _clip!.Version;
            return;
        }

        if (_lastObservedClipVersion == _clip!.Version)
            return;

        _lastObservedClipVersion = _clip.Version;

        // Prune any selection indices that became invalid after Undo/Redo or Inspector edits
        if (_selectedEventIndex >= _clip.PropertyEvents.Count)
            _selectedEventIndex = -1;

        _selectedKeys.RemoveWhere(pair =>
            pair.RowIndex < 0 || pair.RowIndex >= _rows.Count ||
            pair.KeyIndex < 0 || pair.KeyIndex >= GetRowKeyCount(_rows[pair.RowIndex]));

        if (_selectedRowIndex >= 0 && _selectedRowIndex < _rows.Count &&
            _selectedKeyIndex >= GetRowKeyCount(_rows[_selectedRowIndex]))
        {
            _selectedKeyIndex = -1;
        }

        if (_selectedDirectCurveKeys.Count > 0)
        {
            var validTracks = new HashSet<SceneCurveTrack>(_clip.CurveTracks, ReferenceEqualityComparer.Instance);
            List<SceneCurveTrack>? deadTracks = null;
            foreach ((SceneCurveTrack ct, HashSet<int> indices) in _selectedDirectCurveKeys)
            {
                if (!validTracks.Contains(ct))
                {
                    deadTracks ??= [];
                    deadTracks.Add(ct);
                    continue;
                }
                indices.RemoveWhere(k => k < 0 || k >= ct.Curve.Count);
                if (indices.Count == 0)
                {
                    deadTracks ??= [];
                    deadTracks.Add(ct);
                }
            }
            if (deadTracks != null)
            {
                foreach (SceneCurveTrack dt in deadTracks)
                    _selectedDirectCurveKeys.Remove(dt);
            }
        }

        // Re-sample live preview if paused so Undo/Redo reflects in the Scene View immediately
        if (_dragPart == DragPart.None && !_playing)
            RefreshLiveSample();
    }

    private void HandlePlayModeTransitions()
    {
        bool isPlayingNow = Application.IsPlaying;
        if (!_wasPlayingLastFrame && isPlayingNow)
        {
            // Entering Play Mode: restore any Edit-Mode preview state cleanly before binding to the Play world
            if (_previewing)
                StopPreviewAndRestore();
            ClearPreviewBindingCaches();
        }
        else if (_wasPlayingLastFrame && !isPlayingNow)
        {
            // Exiting Play Mode: stop Play-Mode recording and ensure any clips explicitly saved during Play Mode
            // reload their saved disk state (even if they had an older pre-play dirty snapshot).
            _recording = false;
            _playing = false;
            _unsavedClips.Clear();
            _recordedScalars.Clear();
            _recordedObjects.Clear();
            _recordedStrings.Clear();
            _watchedTransforms.Clear();
            ClearPreviewBindingCaches();

            if (_savedDuringPlayMode.Count > 0)
            {
                foreach (Guid savedId in _savedDuringPlayMode)
                {
                    if (AssetDatabase.TryGetExisting(savedId, out Asset savedAsset))
                    {
                        AssetDatabase.Reload(savedAsset);
                        EditorUtility.ClearAssetDirty(savedId);
                    }
                }
                _savedDuringPlayMode.Clear();
            }
        }
        _wasPlayingLastFrame = isPlayingNow;
    }

    internal void SyncTargetFromSelection()
    {
        if (_targetRoot.IsNotValid())
        {
            if (_previewing)
                StopPreviewAndRestore();
            else if (_recording && !Application.IsPlaying)
                _recording = false;
            _targetRoot = null;
            _animator = null;
        }
        else if (!Application.IsPlaying && _targetRoot!.Scene.IsValid() &&
                 (_targetRoot.Scene.State == SceneState.Dormant ||
                  (PrefabEditingMode.IsEditing && PrefabEditingMode.World.IsValid() && !ReferenceEquals(_targetRoot.Scene, PrefabEditingMode.World))))
        {
            // Entering PrefabEditingMode parks the edit world as Dormant; restore preview and unbind from inactive worlds even if locked.
            StopPreviewAndRestore();
            _targetRoot = null;
            _animator = null;
            ClearSelectedClip();
        }

        // When Window Lock (Padlock) is active, keep the current target root & clip even when selecting other objects
        if (_lockTarget && (_targetRoot.IsValid() || IsClipValid))
            return;

        // If user selected a GameObject in the Hierarchy / Scene (or default to the Prefab root while in PrefabEditingMode)
        GameObject? selectedGo = Selection.GetActiveAs<GameObject>();
        if (selectedGo.IsNotValid() && !Application.IsPlaying && PrefabEditingMode.IsEditing && PrefabEditingMode.World.IsValid() &&
            Selection.ActiveObject is not AnimationClip &&
            !(Selection.ActiveObject is ContentItem activeCi && !activeCi.IsFolder && activeCi.Guid != Guid.Empty && AssetDatabase.Get(activeCi.Guid) is AnimationClip))
        {
            selectedGo = PrefabEditingMode.World!.Roots.FirstOrDefault(go => go.IsValid() && !go.HideFlags.HasFlag(HideFlags.HideAndDontSave));
        }
        if (selectedGo.IsValid())
        {
            Animator? foundAnimator = selectedGo!.GetComponent<Animator>();
            if (foundAnimator.IsNotValid())
                foundAnimator = selectedGo.GetComponentInParent<Animator>();

            GameObject newRoot = foundAnimator.IsValid() ? foundAnimator!.GameObject : selectedGo;

            if (!ReferenceEquals(_targetRoot, newRoot) || !ReferenceEquals(_animator, foundAnimator) || (foundAnimator.IsNotValid() && IsClipValid))
            {
                StopPreviewAndRestore();
                _targetRoot = newRoot;
                _animator = foundAnimator;

                if (foundAnimator.IsNotValid())
                {
                    // Selected GameObject has no Animator in its parent hierarchy -> show Unity-style Create button
                    ClearSelectedClip();
                }
                else
                {
                    List<AnimationClip> clips = GetAvailableClips();
                    if (foundAnimator!.CurrentClip.IsValid())
                        SelectClip(foundAnimator.CurrentClip!);
                    else if (clips.Count > 0)
                    {
                        if (!IsClipValid || !clips.Contains(_clip!))
                            SelectClip(clips[0]);
                    }
                    else
                    {
                        ClearSelectedClip();
                    }
                }
            }
            return;
        }

        // If user selected an AnimationClip asset directly in the Project panel
        if (Selection.ActiveObject is ContentItem ci && !ci.IsFolder && ci.Guid != Guid.Empty)
        {
            if (AssetDatabase.Get(ci.Guid) is AnimationClip selectedAnimClip && selectedAnimClip.IsValid())
            {
                _targetRoot = null;
                _animator = null;
                if (!ReferenceEquals(_clip, selectedAnimClip))
                    SelectClip(selectedAnimClip);
            }
        }
        else if (Selection.ActiveObject is AnimationClip directAnimClip && directAnimClip.IsValid())
        {
            _targetRoot = null;
            _animator = null;
            if (!ReferenceEquals(_clip, directAnimClip))
                SelectClip(directAnimClip);
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
                bool clipSwitched = !ReferenceEquals(_clip, _animator.CurrentClip);
                _clip = _animator.CurrentClip;
                if (clipSwitched)
                {
                    ResetSelectionAndDragState();
                    FitTimelineRange();
                }
                bool animatorAdvancing = !_animator.IsPaused && !EditorApplication.IsPlayPaused;
                _playing = animatorAdvancing;
                if (clipSwitched || (animatorAdvancing && MathF.Abs(_currentTime - _animator.Time) > 1e-6f))
                {
                    _currentTime = _animator.Time;
                    _previewElapsedTime = _currentTime;
                    if (_recording)
                        SnapshotCurrentValuesForRecording();
                }
            }
            else
            {
                _playing = false;
            }
            return;
        }

        if (!_playing || !IsClipValid) return;

        float duration = _clip!.Duration;
        if (duration <= 1e-4f)
        {
            _currentTime = 0f;
            _previewElapsedTime = 0f;
            _playing = false;
            return;
        }

        if (_useWorkRange && _workRangeOut > _workRangeIn + 1e-4f)
        {
            float rangeStart = MathF.Max(0f, _workRangeIn);
            float rangeEnd = MathF.Min(duration, _workRangeOut);
            if (rangeEnd > rangeStart + 1e-4f)
            {
                _currentTime += unscaledDt;
                if (_currentTime >= rangeEnd || _currentTime < rangeStart)
                    _currentTime = rangeStart + ((_currentTime - rangeStart) % (rangeEnd - rangeStart));
                _previewElapsedTime = _currentTime;
                SampleAtPlayhead();
                return;
            }
        }

        _previewElapsedTime += unscaledDt;
        SceneClipWrapMode wrap = _clip.EffectiveWrapMode;
        if (wrap == SceneClipWrapMode.Once && _previewElapsedTime >= duration)
        {
            _previewElapsedTime = duration;
            _currentTime = duration;
            _playing = false;
        }
        else
        {
            _currentTime = _clip.WrapTime(_previewElapsedTime);
        }

        SampleAtPlayhead();
    }

    private void TogglePlayPause()
    {
        if (!IsClipValid) return;
        if (Application.IsPlaying)
        {
            if (_animator.IsValid())
            {
                if (_animator!.IsPaused)
                {
                    _animator.Resume();
                    _playing = !EditorApplication.IsPlayPaused;
                }
                else
                {
                    _animator.Pause();
                    _playing = false;
                    if (_recording)
                        SnapshotCurrentValuesForRecording();
                }
            }
            return;
        }

        _playing = !_playing;
        if (_playing && !_previewing)
            EnterPreviewMode();
    }

    internal void ToggleRecording()
    {
        if (!IsClipValid || _targetRoot.IsNotValid()) return;
        _recording = !_recording;
        if (_recording)
        {
            if (Application.IsPlaying)
            {
                SnapshotCurrentValuesForRecording();
            }
            else if (!_previewing)
            {
                EnterPreviewMode();
                SampleAtPlayhead();
            }
        }
    }

    private void RefreshLiveSample(bool enterPreviewInEditMode = false)
    {
        if (_targetRoot.IsNotValid() || !IsClipValid) return;
        if (Application.IsPlaying)
        {
            SampleInPlayMode();
            return;
        }
        if (enterPreviewInEditMode && !_previewing)
            EnterPreviewMode();
        if (_previewing)
            SampleAtPlayhead();
    }

    private void SampleInPlayMode()
    {
        if (_targetRoot.IsNotValid() || !IsClipValid) return;
        _suppressAutoRecord = true;
        try
        {
            if (_animator.IsValid())
            {
                _animator!.Sample(_clip!, _currentTime);
            }
            else
            {
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
        }
        finally
        {
            _suppressAutoRecord = false;
        }
        if (_recording)
            SnapshotCurrentValuesForRecording();
    }

    internal void SetPlayhead(float time, bool sampleScene = true, bool snapToFrame = true)
    {
        float fps = IsClipValid ? MathF.Max(1f, _clip!.FrameRate) : 60f;
        float clamped = MathF.Max(0f, time);
        _currentTime = snapToFrame ? MathF.Round(clamped * fps) / fps : clamped;
        _previewElapsedTime = _currentTime;

        if (sampleScene && IsClipValid && _targetRoot.IsValid())
        {
            if (Application.IsPlaying)
            {
                if (_animator.IsValid())
                {
                    _animator!.Pause();
                    _playing = false;
                }
                SampleInPlayMode();
            }
            else
            {
                if (!_previewing)
                    EnterPreviewMode();
                SampleAtPlayhead();
            }
        }
    }

    internal void EnterPreviewMode()
    {
        if (_previewing) return;
        _previewing = true;
        CapturePreviewBaseline();
        SnapshotCurrentValuesForRecording();
    }

    internal void SuspendPreviewForSave()
    {
        if (!_previewing || Application.IsPlaying) return;
        bool wasRecording = _recording;
        bool wasPlaying = _playing;
        StopPreviewAndRestore();
        _resumePreviewNextFrame = true;
        _resumeRecordingNextFrame = wasRecording;
        _resumePlayingNextFrame = wasPlaying;
    }

    internal void ResumePreviewAfterSaveIfNeeded()
    {
        if (!_resumePreviewNextFrame) return;
        bool resumeRecording = _resumeRecordingNextFrame;
        bool resumePlaying = _resumePlayingNextFrame;
        _resumePreviewNextFrame = false;
        _resumeRecordingNextFrame = false;
        _resumePlayingNextFrame = false;

        if (!Application.IsPlaying && _targetRoot.IsValid() && IsClipValid)
        {
            EnterPreviewMode();
            SampleAtPlayhead();
            _recording = resumeRecording;
            _playing = resumePlaying;
        }
    }

    internal void StopPreviewAndRestore()
    {
        _resumePreviewNextFrame = false;
        _resumeRecordingNextFrame = false;
        _resumePlayingNextFrame = false;
        _playing = false;
        _recording = false;
        if (!_previewing) return;
        _previewing = false;

        _suppressAutoRecord = true;
        try
        {
            // The binding captured the pristine values when it was built (before any sampling), so this also
            // reverts material uniforms and additive tracks, which the plain property baseline cannot see.
            _cachedPreviewBinding?.RestoreBaseline();

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
            ClearPreviewBindingCaches();
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
        if (Application.IsPlaying || _targetRoot.IsNotValid()) return;
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
                            if (_cachedRigBinding == null || _cachedRigPose == null ||
                                !ReferenceEquals(_cachedRigRoot, _targetRoot) ||
                                !ReferenceEquals(_cachedRigSkeleton, avatar.Skeleton))
                            {
                                _cachedRigBinding = new AnimatorBinding(_targetRoot!.Transform, avatar.Skeleton);
                                _cachedRigPose = new Motion.Pose(avatar.Skeleton);
                                _cachedRigRoot = _targetRoot;
                                _cachedRigSkeleton = avatar.Skeleton;
                            }

                            _cachedRigPose.SetToReferencePose(false);
                            float norm = animClip.Duration > 1e-6f ? Math.Clamp(_currentTime / animClip.Duration, 0f, 1f) : 0f;
                            runtime.GetPose(norm, _cachedRigPose);
                            for (int b = 0; b < _cachedRigPose.BoneCount; b++)
                                _cachedRigBinding.ApplyBone(b, _cachedRigPose.GetTransform(b));
                            for (int c = 0; c < _cachedRigPose.FloatChannelCount; c++)
                                _cachedRigBinding.ApplyChannel(c, _cachedRigPose.GetFloat(c));
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
                // Put the scene back before rebuilding, so the new binding captures pristine baselines
                // (otherwise additive tracks and material uniforms would capture already-animated values).
                _cachedPreviewBinding?.RestoreBaseline();
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
        if (_animator.IsValid())
        {
            for (int i = 0; i < _animator!.ObjectBindings.Count; i++)
            {
                AnimatorObjectBinding binding = _animator.ObjectBindings[i];
                if (!string.IsNullOrWhiteSpace(binding.SlotName) && binding.Target.IsValid())
                    SnapshotHierarchyTransforms(binding.Target!);
            }
        }
    }

    private void SnapshotHierarchyTransforms(GameObject go)
    {
        if (go.IsNotValid()) return;
        _watchedTransforms[go] = (go.Transform.LocalPosition, go.Transform.LocalEulerAngles, go.Transform.LocalScale, go.Enabled);
        for (int i = 0; i < go.Children.Count; i++)
            SnapshotHierarchyTransforms(go.Children[i]);
    }

    internal void PollAutoKeyChanges()
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

        // 2. Check Transform / GameObject changes across the hierarchy (and bound @Slot targets)
        PollHierarchyTransforms(_targetRoot!, _targetRoot!, string.Empty, ref changedAny);
        if (_animator.IsValid())
        {
            for (int i = 0; i < _animator!.ObjectBindings.Count; i++)
            {
                AnimatorObjectBinding binding = _animator.ObjectBindings[i];
                if (!string.IsNullOrWhiteSpace(binding.SlotName) && binding.Target.IsValid())
                    PollHierarchyTransforms(binding.Target!, binding.Target!, $"@{binding.SlotName}", ref changedAny);
            }
        }

        if (changedAny)
            MarkClipDirty();
    }

    private void PollHierarchyTransforms(GameObject go, GameObject root, string pathPrefix, ref bool changedAny)
    {
        if (go.IsNotValid() || root.IsNotValid() || _targetRoot.IsNotValid() || !IsClipValid) return;

        Float3 pos = go.Transform.LocalPosition;
        Float3 euler = go.Transform.LocalEulerAngles;
        Float3 scale = go.Transform.LocalScale;
        bool enabled = go.Enabled;

        if (_watchedTransforms.TryGetValue(go, out (Float3 Pos, Float3 Euler, Float3 Scale, bool Enabled) prev))
        {
            string localRel = Transform.GetRelativePath(go.Transform, root.Transform);
            string relPath = string.IsNullOrEmpty(pathPrefix)
                ? localRel
                : (string.IsNullOrEmpty(localRel) ? pathPrefix : $"{pathPrefix}/{localRel}");

            RecordTransformDelta(relPath, pos, euler, scale, enabled, prev, ref changedAny);
        }

        _watchedTransforms[go] = (pos, euler, scale, enabled);

        for (int i = 0; i < go.Children.Count; i++)
            PollHierarchyTransforms(go.Children[i], root, pathPrefix, ref changedAny);
    }

    private void RecordTransformDelta(
        string relPath,
        Float3 pos,
        Float3 euler,
        Float3 scale,
        bool enabled,
        (Float3 Pos, Float3 Euler, Float3 Scale, bool Enabled) prev,
        ref bool changedAny)
    {
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

    private bool TryResolveTrackRelativePath(GameObject go, out string relPath)
    {
        relPath = string.Empty;
        if (go.IsNotValid() || _targetRoot.IsNotValid()) return false;

        if (GameObject.IsChildOrSameTransform(go, _targetRoot!))
        {
            relPath = Transform.GetRelativePath(go.Transform, _targetRoot!.Transform);
            return true;
        }

        if (_animator.IsValid())
        {
            for (int i = 0; i < _animator!.ObjectBindings.Count; i++)
            {
                AnimatorObjectBinding binding = _animator.ObjectBindings[i];
                if (string.IsNullOrWhiteSpace(binding.SlotName) || binding.Target.IsNotValid()) continue;
                if (GameObject.IsChildOrSameTransform(go, binding.Target!))
                {
                    string sub = Transform.GetRelativePath(go.Transform, binding.Target!.Transform);
                    relPath = string.IsNullOrEmpty(sub) ? $"@{binding.SlotName}" : $"@{binding.SlotName}/{sub}";
                    return true;
                }
            }
        }

        return false;
    }

    // Intercepts any Inspector / Scene View gizmo modification on a GameObject or Component under _targetRoot (or bound @Slot targets) while _recording is active (both Edit Mode and Play Mode)
    private UndoPropertyModification[] OnPostprocessUndoModifications(UndoPropertyModification[] modifications)
    {
        if (!_recording || _suppressAutoRecord || _targetRoot.IsNotValid() || !IsClipValid)
            return modifications;

        bool recorded = false;
        foreach (UndoPropertyModification mod in modifications)
        {
            if (mod.Target is GameObject targetGo && targetGo.IsValid())
            {
                if (!TryResolveTrackRelativePath(targetGo, out string goRelPath))
                    continue;

                Float3 pos = targetGo.Transform.LocalPosition;
                Float3 euler = targetGo.Transform.LocalEulerAngles;
                Float3 scale = targetGo.Transform.LocalScale;
                bool enabled = targetGo.Enabled;

                if (_watchedTransforms.TryGetValue(targetGo, out (Float3 Pos, Float3 Euler, Float3 Scale, bool Enabled) prev))
                    RecordTransformDelta(goRelPath, pos, euler, scale, enabled, prev, ref recorded);
                _watchedTransforms[targetGo] = (pos, euler, scale, enabled);
                continue;
            }

            if (mod.Target is not Component comp || comp.IsNotValid() || comp.GameObject.IsNotValid())
                continue;

            if (!TryResolveTrackRelativePath(comp.GameObject, out string relPath))
                continue;

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
            MarkClipDirty();

        return modifications;
    }

    // ================================================================
    //  Row Rebuilding
    // ================================================================

    internal void RebuildRows()
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
                AccentColor = EditorTheme.Green400,
                StringTrack = t,
            });
        }

        if (_targetRoot.IsValid())
        {
            // Resolving every row's path and member chain each frame is costly for rig clips with hundreds of
            // tracks, so results are cached and refreshed a few times a second (or when the target changes).
            long now = Environment.TickCount64;
            if (!ReferenceEquals(_missingCacheRoot, _targetRoot) || now - _missingCacheTick > 400)
            {
                _missingCache.Clear();
                _missingCacheRoot = _targetRoot;
                _missingCacheTick = now;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                TrackRow r = _rows[i];
                string checkProp = r.Kind == TrackRowKind.Group ? string.Empty : r.PropertyPath;
                var key = (r.RelativePath, r.ComponentType, checkProp);
                if (!_missingCache.TryGetValue(key, out bool valid))
                {
                    valid = SceneClipBinding.IsTrackTargetValid(_targetRoot!, r.RelativePath, r.ComponentType, checkProp);
                    _missingCache[key] = valid;
                }
                r.IsMissing = !valid;
            }
        }

        if (_selectedRowIndex >= _rows.Count)
        {
            _selectedRowIndex = -1;
            _selectedKeyIndex = -1;
        }
        _selectedTrackRows.RemoveWhere(idx => idx < 0 || idx >= _rows.Count);
    }

    internal void SelectTrackRow(int rowIndex, bool additive = false)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Count)
        {
            _selectedRowIndex = -1;
            _selectedTrackRows.Clear();
            _curveBoundsInitialized = false;
            return;
        }

        if (additive)
        {
            if (!_selectedTrackRows.Remove(rowIndex))
            {
                _selectedTrackRows.Add(rowIndex);
                _selectedRowIndex = rowIndex;
            }
            else
            {
                _selectedRowIndex = _selectedTrackRows.Count > 0 ? _selectedTrackRows.First() : -1;
            }
        }
        else
        {
            _selectedTrackRows.Clear();
            _selectedTrackRows.Add(rowIndex);
            _selectedRowIndex = rowIndex;
        }

        _selectedKeyIndex = -1;
        _selectedEventIndex = -1;
        _selectedKeys.Clear();
        _selectedDirectCurveKeys.Clear();
        _curveBoundsInitialized = false;
    }

    internal void SetGroupCollapsed(int groupRowIndex, bool collapsed)
    {
        if (groupRowIndex < 0 || groupRowIndex >= _rows.Count) return;
        string gk = _rows[groupRowIndex].GroupKey;
        if (string.IsNullOrEmpty(gk)) return;
        if (collapsed) _collapsedGroups.Add(gk);
        else _collapsedGroups.Remove(gk);
        RebuildRows();
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
        MarkClipDirty();
        RefreshLiveSample();
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
        RefreshLiveSample();
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
        MarkClipDirty();
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
        MarkClipDirty();
        RefreshLiveSample();
    }

    private static string FormatRowLabel(string relPath, string compType, string propPath)
    {
        string propDisplay = compType == "GameObject" && propPath == "Enabled"
            ? "Is Active"
            : $"{compType}.{propPath}";
        if (string.IsNullOrEmpty(relPath))
            return propDisplay;

        string compactObj = FormatCompactHierarchyPrefix(relPath);
        return $"{compactObj} : {propDisplay}";
    }

    internal static string FormatCompactHierarchyPrefix(string relPath)
    {
        if (string.IsNullOrEmpty(relPath))
            return string.Empty;

        int lastSlash = relPath.LastIndexOf('/');
        if (lastSlash < 0)
            return relPath;

        int prevSlash = relPath.LastIndexOf('/', lastSlash - 1);
        if (prevSlash < 0)
        {
            // 2 segments: "Arm/Hand" -> "Arm › Hand"
            return $"{relPath[..lastSlash]} › {relPath[(lastSlash + 1)..]}";
        }

        // 3+ segments: keep immediate parent + leaf ("… › RightArm › Hand") so deep paths don't push
        // the object leaf name and property name off-screen while sibling branches stay distinct.
        string parentSeg = relPath[(prevSlash + 1)..lastSlash];
        string leafSeg = relPath[(lastSlash + 1)..];
        return $"… › {parentSeg} › {leafSeg}";
    }

    private string FormatTrackRowTooltip(TrackRow row)
    {
        string rootName = _targetRoot.IsValid() ? _targetRoot!.Name : "(Root)";
        string propDisplay = row.ComponentType == "GameObject" && row.PropertyPath == "Enabled"
            ? "GameObject.Enabled (Is Active)"
            : $"{row.ComponentType}.{row.PropertyPath}";

        if (string.IsNullOrEmpty(row.RelativePath))
            return $"{rootName} (Root) — {propDisplay}";

        string fullBreadcrumb = row.RelativePath.StartsWith('@')
            ? row.RelativePath.Replace("/", " › ")
            : $"{rootName} › {row.RelativePath.Replace("/", " › ")}";
        return $"{fullBreadcrumb} — {propDisplay}";
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
            // Preview toggle button (Edit Mode)
            if (!Application.IsPlaying)
            {
                EditorGUI.Chip(paper, "anim_btn_preview", "Preview", _previewing, () =>
                {
                    if (_previewing) StopPreviewAndRestore();
                    else { EnterPreviewMode(); SampleAtPlayhead(); }
                }, height: 22f);
            }

            // Record toggle button (red circle + Record — works in both Edit Mode and Play Mode)
            bool canRecord = IsClipValid && _targetRoot.IsValid() && !NeedsAnimatorCreation;
            using (paper.Row("anim_btn_rec").Width(UnitValue.Auto).Height(22).Padding(8, 8, 0, 0).Gap(5)
                .AlignItems(LayoutAlignment.Center)
                .Rounded(m.SmallRounding)
                .BackgroundColor(_recording ? EditorTheme.Red400 : EditorTheme.Neutral300)
                .Hovered.BackgroundColor(_recording ? EditorTheme.Red500 : EditorTheme.Neutral400).End()
                .Cursor(canRecord ? PaperCursor.Pointer : PaperCursor.Default)
                .Tooltip(ShortcutManager.GetTooltip("Toggle Auto-Key Recording mode (Edit & Play Mode): any change made to the target hierarchy in the Inspector or Scene View records a keyframe at the playhead", ShortcutToggleRecord))
                .OnClick(0, (_, _) =>
                {
                    if (canRecord) ToggleRecording();
                }).Enter())
            {
                paper.Box("anim_rec_dot").Width(8).Height(8).Rounded(4)
                    .BackgroundColor(_recording ? Color.White : EditorTheme.Red400).IsNotInteractable();
                paper.Box("anim_rec_lbl").Width(UnitValue.Auto).Height(22).IsNotInteractable()
                    .Text("Record", font).TextColor(_recording ? Color.White : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            }

            paper.Box("anim_tdiv1").Width(1).Height(18).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            // Transport controls: First Frame, Prev Frame, Play/Pause, Next Frame, Last Frame
            EditorGUI.ToolbarIconBtn(paper, "anim_first", EditorIcons.BackwardStep, ShortcutManager.GetTooltip("First Frame", ShortcutFirstFrame), false, () =>
            {
                _playing = false;
                SetPlayhead(0f);
            });
            EditorGUI.ToolbarIconBtn(paper, "anim_prev", EditorIcons.CaretLeft, ShortcutManager.GetTooltip("Previous Frame", ShortcutPreviousFrame), false, () =>
            {
                _playing = false;
                SetPlayhead(MathF.Max(0f, _currentTime - 1f / fps));
            });
            EditorGUI.ToolbarIconBtn(paper, "anim_play", _playing ? EditorIcons.Pause : EditorIcons.Play, ShortcutManager.GetTooltip(_playing ? "Pause" : "Play", ShortcutPlayPause), _playing, TogglePlayPause);
            EditorGUI.ToolbarIconBtn(paper, "anim_next", EditorIcons.CaretRight, ShortcutManager.GetTooltip("Next Frame", ShortcutNextFrame), false, () =>
            {
                _playing = false;
                SetPlayhead(_currentTime + 1f / fps);
            });
            EditorGUI.ToolbarIconBtn(paper, "anim_last", EditorIcons.ForwardStep, ShortcutManager.GetTooltip("Last Frame", ShortcutLastFrame), false, () =>
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
                paper.Box("anim_tdiv3").Width(1).Height(18).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

                // Add Keyframe at Playhead button
                EditorGUI.ToolbarIconBtn(paper, "anim_add_key", EditorIcons.Diamond, ShortcutManager.GetTooltip("Key Selected / All Tracks at Playhead", ShortcutKeyAtPlayhead), false, KeyAtPlayhead);

                // Add Instant Animation Event at Playhead button
                EditorGUI.ToolbarIconBtn(paper, "anim_add_ev", EditorIcons.Bookmark, ShortcutManager.GetTooltip("Add Instant Animation Event at Playhead", ShortcutAddEvent), false, () => AddEventAtPlayhead(ranged: false));

                // Add Ranged (Start/End) Animation Event at Playhead button
                EditorGUI.ToolbarIconBtn(paper, "anim_add_rev", EditorIcons.ArrowsLeftRight, ShortcutManager.GetTooltip("Add Ranged (Start/End) Animation Event at Playhead", ShortcutAddRangedEvent), false, () => AddEventAtPlayhead(ranged: true));

                // Unpack Rig Curves button when editing an imported skeletal/humanoid AnimationClip
                if (_clip is AnimationClip { HasMotionPayload: true } importedClip)
                {
                    Origami.Button(paper, "anim_unpack_rig", $"{EditorIcons.Sliders} Unpack Rig Curves", () =>
                    {
                        Undo.RecordObject(importedClip, "Unpack Rig Curves");
                        importedClip.UnpackMotionToCurveTracks(_targetRoot);
                        MarkClipDirty(importedClip);
                    }).Small().Height(22).Show();
                }

                // Reduce Keys button to simplify dense curve tracks (e.g. after Unpack Rig Curves)
                if (_clip!.CurveTracks.Count > 0)
                {
                    Origami.Button(paper, "anim_reduce_keys", "Reduce Keys", () =>
                    {
                        Undo.RecordObject(ClipAsset!, "Reduce Redundant Keyframes");
                        int removed = _clip.ReduceKeyframes(0.005f);
                        if (removed > 0)
                        {
                            _selectedKeys.Clear();
                            _selectedKeyIndex = -1;
                            MarkClipDirty();
                        }
                    }).Small().Height(22).Show();
                }

                if (IsCurrentClipImportedSubAsset(out AssetEntry? parentModel) && parentModel != null)
                {
                    paper.Box("anim_imp_badge").Width(UnitValue.Auto).Height(20)
                        .Padding(6, 6, 0, 0).Rounded(m.SmallRounding)
                        .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Amber400, 36))
                        .BorderColor(EditorTheme.WithAlpha(EditorTheme.Amber400, 110)).BorderWidth(1)
                        .Text("Imported", EditorTheme.FontSemiBold ?? font)
                        .TextColor(EditorTheme.Amber400).FontSize(EditorTheme.FontSizeSmall * 0.9f)
                        .Alignment(TextAlignment.MiddleCenter)
                        .Tooltip($"Imported sub-asset of '{parentModel.Path}'.\nEvents & Loop save to .meta and survive reimports.\nCurve/track edits apply until the model is reimported.");

                    Origami.Button(paper, "anim_extract_clip", $"{EditorIcons.Clone} Extract .animclip", () => ExtractCurrentImportedClip())
                        .Small().Height(22)
                        .Tooltip("Duplicate as a standalone .animclip asset so curve edits are never overwritten by model reimports")
                        .Show();
                }
            }

            paper.Box("anim_top_sp").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();

            // Window Lock (Padlock) button: Unity-style lock to current target GameObject / Clip
            EditorGUI.ToolbarIconBtn(paper, "anim_lock_win",
                _lockTarget ? EditorIcons.Lock : EditorIcons.LockOpen,
                _lockTarget ? "Unlock Animation Window (Follow Scene Selection)" : "Lock Animation Window to Current Target",
                _lockTarget, () => _lockTarget = !_lockTarget);

            if (HasUnsavedClipChanges)
            {
                Origami.Button(paper, "anim_save_clip", $"{EditorIcons.FloppyDisk} Save", () => SaveCurrentClip())
                    .Primary().Small().Height(22).Show();
            }
        }
    }

    private static void MarkAnimatorEdited(Animator animator)
    {
        EditorUtility.SetDirty(animator);
        if (!Application.IsPlaying)
            PrefabUtility.NotifyEdited(animator);
    }

    private void MarkClipDirty(AnimationClip? clip = null)
    {
        AnimationClip? target = clip.IsValid() ? clip : ClipAsset;
        if (target.IsNotValid()) return;
        // EditorUtility.SetDirty is a no-op while Application.IsPlaying is true, so _unsavedClips tracks
        // Play Mode (and unsaved in-memory) clip modifications so the Save button appears immediately.
        if (Application.IsPlaying || !target!.IsFromDatabase)
            _unsavedClips.Add(target!);
        EditorUtility.SetDirty(target!);
    }

    private bool IsCurrentClipImportedSubAsset(out AssetEntry? parentModelEntry)
    {
        parentModelEntry = null;
        return IsClipValid
            && _clip!.AssetID != Guid.Empty
            && EditorAssetBackend.Instance is { } db
            && db.IsImportedAnimationSubAsset(_clip.AssetID, out parentModelEntry);
    }

    internal AnimationClip? ExtractCurrentImportedClip()
    {
        if (!IsCurrentClipImportedSubAsset(out AssetEntry? parentModel) || parentModel == null || Project.Current == null || EditorAssetBackend.Instance is not { } db)
            return null;

        string parentDir = Path.GetDirectoryName(parentModel.Path)?.Replace('\\', '/') ?? "";
        string absFolder = Path.Combine(Project.Current.AssetsPath, parentDir);
        Directory.CreateDirectory(absFolder);

        string fileName = Utils.UniqueNames.ForCopy(
            absFolder,
            Utils.EditorUtils.SafeFileName(_clip!.Name, "Animation"),
            ".animclip");
        string relPath = string.IsNullOrEmpty(parentDir) ? fileName : $"{parentDir}/{fileName}";

        AnimationClip copy = _clip.Duplicate(_targetRoot, unpackMotion: false);
        copy.SourceName = string.Empty;
        copy.TakeStart = 0f;
        copy.Name = Path.GetFileNameWithoutExtension(fileName);
        db.CreateAsset(copy, relPath);

        if (_animator.IsValid())
        {
            Undo.RecordObject(_animator!, "Assign Extracted Animation Clip");
            _animator!.AddClip(copy);
            MarkAnimatorEdited(_animator);
        }

        SelectClip(copy);
        return copy;
    }

    internal bool SaveCurrentClip()
    {
        if (!IsClipValid) return false;
        AnimationClip clip = ClipAsset!;

        if (EditorAssetBackend.Instance is { } db)
        {
            if (clip.AssetID != Guid.Empty && db.SaveAsset(clip))
            {
                _unsavedClips.Remove(clip);
                EditorUtility.ClearAssetDirty(clip.AssetID);
                if (Application.IsPlaying)
                    _savedDuringPlayMode.Add(clip.AssetID);
                return true;
            }

            if (clip.AssetID == Guid.Empty && Project.Current != null)
            {
                string startDir = GetDefaultClipSaveDirectory();
                EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
                {
                    if (path == null || !clip.IsValid() || Project.Current == null || EditorAssetBackend.Instance is not { } backend) return;
                    if (!TryNormalizeProjectClipPath(path, out string rel)) return;
                    backend.CreateAsset(clip, rel);
                    _unsavedClips.Remove(clip);
                    EditorUtility.ClearAssetDirty(clip.AssetID);
                    if (Application.IsPlaying && clip.AssetID != Guid.Empty)
                        _savedDuringPlayMode.Add(clip.AssetID);
                }, startDir, ["*.animclip"], ["Animation Clip"]);
                return true;
            }
        }

        _unsavedClips.Remove(clip);
        EditorUtility.ClearAssetDirty(clip.AssetID);
        return false;
    }

    private void DrawClipPickerInToolbar(Paper paper, Scribe.FontFile font)
    {
        if (NeedsAnimatorCreation)
        {
            paper.Box("anim_no_anim_lbl").Width(150).Height(22).IsNotInteractable()
                .Text("(No Animator)", font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            return;
        }

        List<AnimationClip> available = GetAvailableClips();
        if (IsClipValid && !available.Contains(_clip!))
            available.Insert(0, _clip!);

        if (available.Count > 0)
        {
            string[] names = [.. available.Select(c => c.Name)];
            int currentIdx = IsClipValid ? available.IndexOf(_clip!) : 0;
            using (paper.Box("anim_clip_dd_w").Width(150).Height(22).Enter())
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
                        MarkAnimatorEdited(_animator);
                    }
                    SelectClip(ac);
                }
            });
        }).Small().Height(22).Show();

        Origami.Button(paper, "anim_new_clip", "+ New Clip", PromptCreateClipForTarget).Small().Height(22).Show();
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
                string prompt = _animator.IsNotValid()
                    ? $"To begin animating {_targetRoot!.Name}, create an Animator and an Animation Clip."
                    : $"To begin animating {_targetRoot!.Name}, create an Animation Clip.";

                paper.Box("anim_emp_msg").Width(500).Height(32).IsNotInteractable()
                    .Text(prompt, font)
                    .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);

                paper.Box("anim_emp_gap").Height(10).IsNotInteractable();

                Origami.Button(paper, "anim_emp_create", "Create", PromptCreateClipForTarget)
                    .Primary().Width(160).Height(32).Show();
            }

            paper.Box("anim_emp_bot_sp").Height(UnitValue.StretchOne).IsNotInteractable();
        }
    }

    private static string GetDefaultClipSaveDirectory()
    {
        if (Project.Current == null) return string.Empty;
        string currentFolder = AssetCreateMenu.GetCurrentFolder();
        string absFolder = AssetCreateMenu.GetAbsoluteFolder(currentFolder);
        return Directory.Exists(absFolder) ? absFolder : Project.Current.AssetsPath;
    }

    private static bool TryNormalizeProjectClipPath(string fullPath, out string relativeAssetPath)
    {
        relativeAssetPath = string.Empty;
        if (Project.Current == null || string.IsNullOrWhiteSpace(fullPath)) return false;

        string rel = EditorAssetBackend.NormalizePath(Path.GetRelativePath(Project.Current.AssetsPath, fullPath));
        if (Path.IsPathRooted(rel) || rel.StartsWith("..", StringComparison.Ordinal))
        {
            Toasts.Warning("Invalid Save Location", "Animation clips must be saved inside the project's Assets folder.");
            return false;
        }

        if (!rel.EndsWith(".animclip", StringComparison.OrdinalIgnoreCase))
            rel += ".animclip";

        relativeAssetPath = rel;
        return true;
    }

    private void PromptCreateClipForTarget()
    {
        GameObject? targetGo = _targetRoot;
        if (Project.Current != null && EditorAssetBackend.Instance != null)
        {
            string startDir = GetDefaultClipSaveDirectory();
            EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
            {
                if (path == null || !TryNormalizeProjectClipPath(path, out string rel)) return;
                CreateClipForTargetAtPath(rel, targetGo);
            }, startDir, ["*.animclip"], ["Animation Clip"]);
            return;
        }

        CreateClipForTargetAtPath(null, targetGo);
    }

    internal AnimationClip CreateClipForTargetAtPath(string? relativeAssetPath, GameObject? targetOverride = null)
    {
        GameObject? targetGo = targetOverride.IsValid() ? targetOverride : _targetRoot;
        string fallbackName = targetGo.IsValid() ? $"{targetGo!.Name}_Anim" : "New Animation Clip";
        string clipName = !string.IsNullOrWhiteSpace(relativeAssetPath)
            ? Path.GetFileNameWithoutExtension(relativeAssetPath)
            : fallbackName;
        if (string.IsNullOrWhiteSpace(clipName))
            clipName = fallbackName;

        var newClip = new AnimationClip
        {
            Name = clipName,
            FrameRate = 60f,
            WrapMode = SceneClipWrapMode.Loop,
            Loop = true,
        };

        if (!string.IsNullOrWhiteSpace(relativeAssetPath) && Project.Current != null && EditorAssetBackend.Instance is { } db)
        {
            db.CreateAsset(newClip, relativeAssetPath);
            if (Application.IsPlaying && newClip.AssetID != Guid.Empty)
                _savedDuringPlayMode.Add(newClip.AssetID);
        }

        if (targetGo.IsValid())
        {
            Animator? anim = targetGo!.GetComponent<Animator>();
            if (anim.IsNotValid())
                anim = Undo.AddComponent<Animator>(targetGo);

            if (anim.IsValid())
            {
                Undo.RecordObject(anim!, "Assign Animation Clip");
                anim!.AddClip(newClip);
                MarkAnimatorEdited(anim);
                _targetRoot = anim.GameObject;
                _animator = anim;
            }
        }

        SelectClip(newClip);
        return newClip;
    }

    // ================================================================
    //  Left Pane: Track List & Add Property Button
    // ================================================================

    private void DrawLeftTrackList(Paper paper, Scribe.FontFile font, float width, float height)
    {
        OrigamiMetrics m = Origami.Current.Metrics;

        using (paper.Column("anim_left").Width(width).Height(height).Enter())
        {
            // Row 1: Target header aligned with right Time Ruler
            using (paper.Row("anim_left_hdr").Width(width).Height(TimeRulerHeight)
                .Padding(8, 8, 0, 0).Gap(6).AlignItems(LayoutAlignment.Center)
                .BackgroundColor(EditorTheme.Neutral200).Enter())
            {
                string targetText = _targetRoot.IsValid() ? _targetRoot!.Name : "(No Target GameObject)";
                paper.Box("anim_target_lbl").Width(UnitValue.Stretch()).Height(TimeRulerHeight).IsNotInteractable()
                    .Text(targetText, EditorTheme.FontSemiBold ?? font)
                    .TextColor(_targetRoot.IsValid() ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                if (_animator.IsValid())
                {
                    EditorGUI.ToolbarIconBtn(paper, "anim_bind_ext_btn", EditorIcons.Link,
                        "Bind External Scene GameObject (@Slot) to this Animator", false, OpenExternalObjectBindPicker);
                }

                ElementBuilder addBtn = paper.Box("anim_add_prop_btn")
                    .Width(104).Height(20).Rounded(m.SmallRounding)
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
                        DrawAddPropertyPopover(paper, font, paper.CurrentParent);
                    }
                }
            }

            // Row 2: Dedicated Events Lane Header aligned with right Event Bar
            int evCount = IsClipValid ? _clip!.PropertyEvents.Count : 0;
            using (paper.Row("anim_left_ev_hdr").Width(width).Height(EventBarHeight)
                .Padding(8, 8, 0, 0).Gap(4).AlignItems(LayoutAlignment.Center)
                .BackgroundColor(EditorTheme.Neutral300).Enter())
            {
                paper.Box("anim_left_ev_ic").Width(14).Height(EventBarHeight).IsNotInteractable()
                    .Text(EditorIcons.Bookmark, font).TextColor(EditorTheme.Blue400)
                    .FontSize(EditorTheme.FontSizeSmall * 0.85f).Alignment(TextAlignment.MiddleCenter);

                paper.Box("anim_left_ev_lbl").Width(UnitValue.Stretch()).Height(EventBarHeight).IsNotInteractable()
                    .Text(evCount > 0 ? $"Events ({evCount})" : "Events", font)
                    .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft);

                Origami.Button(paper, "anim_left_add_ev", "+ Event", () => AddEventAtPlayhead(ranged: false))
                    .Small().Height(18).Show();

                Origami.Button(paper, "anim_left_add_rev", "+ Range", () => AddEventAtPlayhead(ranged: true))
                    .Small().Height(18).Show();
            }

            EditorGUI.Divider(paper, "anim_left_hdiv");

            float topHeaderH = TimeRulerHeight + EventBarHeight;
            float listH = MathF.Max(20f, height - topHeaderH - 1f);
            _trackListViewportHeight = listH;
            ClampTrackScrollY();

            float totalContentH = _rows.Count * TrackRowHeight;
            float maxScroll = MaxTrackScrollY;
            bool showScrollbar = maxScroll > 0.5f;
            float rowWidth = showScrollbar ? width - 8f : width;

            using (paper.Box("anim_left_list").Width(width).Height(listH).Clip()
                .OnScroll(e => ScrollTrackListBy(-(float)e.Delta * TrackRowHeight * 2.5f))
                .Enter())
            {
                if (_rows.Count == 0)
                {
                    if (_clip!.HasMotionPayload)
                    {
                        bool isImported = IsCurrentClipImportedSubAsset(out AssetEntry? parentModel) && parentModel != null;
                        using (paper.Column("anim_imp_empty").Size(width, listH).Padding(16, 16, 18, 16).Gap(8)
                            .AlignItems(LayoutAlignment.Center).Enter())
                        {
                            paper.Box("anim_imp_emp_t").Width(width - 32f).Height(20f).IsNotInteractable()
                                .Text($"Skeletal {(_clip.Kind == AnimationClipKind.Humanoid ? "Humanoid" : "Generic")} Clip ({_clip.FrameCount} frames, {_clip.Duration:0.00}s)", EditorTheme.FontSemiBold ?? font)
                                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

                            string hint = isImported
                                ? "Add Events above (saved to .meta & preserved on reimport), '+ Add Property' for custom tracks, or Unpack Rig Curves to edit bone curves."
                                : "Use '+ Event' above, '+ Add Property' for extra tracks, or Unpack Rig Curves to edit individual bone curves.";
                            paper.Box("anim_imp_emp_sub").Width(width - 32f).Height(38f).IsNotInteractable()
                                .Wrap(Prowl.Scribe.TextWrapMode.Wrap)
                                .Text(hint, font).TextColor(EditorTheme.Ink300)
                                .FontSize(EditorTheme.FontSizeSmall * 0.92f).Alignment(TextAlignment.MiddleCenter);

                            using (paper.Row("anim_imp_emp_btns").Height(26f).Gap(6f).AlignItems(LayoutAlignment.Center).Enter())
                            {
                                Origami.Button(paper, "anim_imp_emp_unpack", $"{EditorIcons.Sliders} Unpack Rig Curves", () =>
                                {
                                    Undo.RecordObject(_clip, "Unpack Rig Curves");
                                    _clip.UnpackMotionToCurveTracks(_targetRoot);
                                    MarkClipDirty(_clip);
                                }).Small().Height(24f).Show();

                                if (isImported)
                                {
                                    Origami.Button(paper, "anim_imp_emp_extract", $"{EditorIcons.Clone} Extract .animclip", () => ExtractCurrentImportedClip())
                                        .Small().Height(24f).Show();
                                }
                            }
                        }
                    }
                    else
                    {
                        EditorGUI.EmptyState(paper, "anim_no_tracks", "Click '+ Add Property' or enable Record to animate properties.", font);
                    }
                }
                else
                {
                    int firstRow = Math.Max(0, (int)MathF.Floor(_trackScrollY / TrackRowHeight));
                    int lastRow = Math.Min(_rows.Count - 1, (int)MathF.Ceiling((_trackScrollY + listH) / TrackRowHeight));
                    float topOffset = firstRow * TrackRowHeight - _trackScrollY;

                    using (paper.Column("anim_left_rows").PositionType(PositionType.SelfDirected)
                        .Position(0f, topOffset).Width(rowWidth).Height(UnitValue.Auto).Enter())
                    {
                        for (int i = firstRow; i <= lastRow; i++)
                            DrawTrackListRow(paper, font, _rows[i], i, rowWidth);
                    }

                    if (showScrollbar)
                    {
                        float thumbH = MathF.Max(22f, listH * (listH / MathF.Max(1f, totalContentH)));
                        float trackSpan = MathF.Max(1f, listH - thumbH);
                        float thumbY = (maxScroll > 0f ? (_trackScrollY / maxScroll) : 0f) * trackSpan;

                        using (paper.Box("anim_left_sb").PositionType(PositionType.SelfDirected)
                            .Position(width - 8f, 0f).Size(8f, listH)
                            .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral200, 140))
                            .OnPress(e =>
                            {
                                float clickNorm = Math.Clamp((e.RelativePosition.Y - thumbH * 0.5f) / trackSpan, 0f, 1f);
                                _trackScrollY = clickNorm * maxScroll;
                                _scrollbarDragStartPointerY = (float)paper.PointerPos.Y;
                                _scrollbarDragStartScrollY = _trackScrollY;
                            })
                            .OnDragging(_ =>
                            {
                                float dy = (float)paper.PointerPos.Y - _scrollbarDragStartPointerY;
                                _trackScrollY = Math.Clamp(_scrollbarDragStartScrollY + (dy / trackSpan) * maxScroll, 0f, maxScroll);
                            })
                            .Enter())
                        {
                            paper.Box("anim_left_sb_thumb").PositionType(PositionType.SelfDirected)
                                .Position(1f, thumbY).Size(6f, thumbH).Rounded(3)
                                .BackgroundColor(EditorTheme.Neutral500)
                                .Hovered.BackgroundColor(EditorTheme.Ink300).End()
                                .IsNotInteractable();
                        }
                    }
                }
            }
        }
    }

    private void DrawTrackListRow(Paper paper, Scribe.FontFile font, TrackRow row, int index, float width)
    {
        bool selected = index == _selectedRowIndex || _selectedTrackRows.Contains(index);
        bool muted = IsRowMuted(row);
        bool solo = IsRowSolo(row);
        bool locked = IsRowLocked(row);
        bool additive = IsRowAdditive(row);
        OrigamiMetrics m = Origami.Current.Metrics;

        // Highlight tracks belonging to the currently selected child/external GameObject in the scene
        GameObject? activeSceneGo = Selection.GetActiveAs<GameObject>();
        GameObject? resolvedTargetGo = _targetRoot.IsValid() ? SceneClipBinding.ResolveGameObject(_targetRoot!, row.RelativePath) : null;
        bool matchesSceneSelection = activeSceneGo.IsValid() && resolvedTargetGo.IsValid() && ReferenceEquals(activeSceneGo, resolvedTargetGo);

        Color rowBg = selected
            ? EditorTheme.Selected
            : row.IsMissing
                ? EditorTheme.WithAlpha(EditorTheme.Red400, 26)
                : matchesSceneSelection
                    ? EditorTheme.WithAlpha(EditorTheme.Accent, 24)
                    : (index % 2 == 0 ? Color.Transparent : EditorTheme.WithAlpha(EditorTheme.Neutral300, 70));

        using (paper.Row($"anim_trow_{index}").Width(width).Height(TrackRowHeight)
            .Padding(8 + row.Indent * 14f, 6, 0, 0).Gap(4).AlignItems(LayoutAlignment.Center)
            .BackgroundColor(rowBg)
            .Hovered.BackgroundColor(selected ? EditorTheme.Selected : EditorTheme.Hover).End()
            .Tooltip(FormatTrackRowTooltip(row))
            .OnClick(index, (idx, _) =>
            {
                bool ctrl = IsCtrlDown(paper);
                bool shift = IsShiftDown(paper);
                if (shift && _selectedRowIndex >= 0)
                {
                    int lo = Math.Min(_selectedRowIndex, idx);
                    int hi = Math.Max(_selectedRowIndex, idx);
                    if (!ctrl) _selectedTrackRows.Clear();
                    for (int r = lo; r <= hi; r++)
                        _selectedTrackRows.Add(r);
                    _selectedRowIndex = idx;
                    _selectedKeyIndex = -1;
                    _selectedEventIndex = -1;
                    _curveBoundsInitialized = false;
                }
                else
                {
                    SelectTrackRow(idx, additive: ctrl);
                }
            })
            .OnRightClick(index, (idx, _) =>
            {
                if (!_selectedTrackRows.Contains(idx))
                {
                    _selectedTrackRows.Clear();
                    _selectedTrackRows.Add(idx);
                }
                _selectedRowIndex = idx;
                TrackRow r = _rows[idx];
                bool isLocked = IsRowLocked(r);
                bool isMuted = IsRowMuted(r);
                bool isSolo = IsRowSolo(r);
                bool isAdditive = IsRowAdditive(r);
                GameObject? targetGo = _targetRoot.IsValid() ? SceneClipBinding.ResolveGameObject(_targetRoot!, r.RelativePath) : null;
                GameObject? selGo = Selection.GetActiveAs<GameObject>();
                bool canRelink = selGo.IsValid() && _targetRoot.IsValid() && (r.IsMissing || !ReferenceEquals(selGo, targetGo));

                Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
                {
                    menu.Header(FormatTrackRowTooltip(r));
                    menu.Item("Add Key at Playhead", () => AddKeyToRowAt(r, _currentTime),
                        icon: EditorIcons.Diamond, enabled: !isLocked, shortcut: ShortcutKeys(ShortcutKeyAtPlayhead));

                    if (r.Kind == TrackRowKind.Group)
                    {
                        bool isCollapsed = _collapsedGroups.Contains(r.GroupKey);
                        menu.Item(isCollapsed ? "Expand Channels" : "Collapse Channels", () =>
                        {
                            if (!_collapsedGroups.Add(r.GroupKey))
                                _collapsedGroups.Remove(r.GroupKey);
                        });
                    }

                    menu.Separator();

                    if (r.Kind is TrackRowKind.Group or TrackRowKind.Curve)
                    {
                        menu.Submenu("Blend Mode", sub =>
                        {
                            sub.Item("Override", () => { if (IsRowAdditive(r)) ToggleRowAdditive(r); }, on: !isAdditive);
                            sub.Item("Additive", () => { if (!IsRowAdditive(r)) ToggleRowAdditive(r); }, on: isAdditive);
                        });
                    }

                    menu.Item("Mute Track", () => ToggleRowMute(r), on: isMuted);
                    menu.Item("Solo Track", () => ToggleRowSolo(r), on: isSolo);
                    menu.Item("Lock Track", () => ToggleRowLock(r), on: isLocked);

                    if (targetGo.IsValid() || canRelink)
                    {
                        menu.Separator();
                        if (targetGo.IsValid())
                            menu.Item($"Select '{targetGo!.Name}' in Hierarchy", () => Selection.Select(targetGo), icon: EditorIcons.Cube);
                        if (canRelink)
                            menu.Item($"Relink Track to '{selGo!.Name}'", () => RelinkRowToGameObject(r, selGo!), icon: EditorIcons.Link);
                    }

                    menu.Separator();
                    menu.Item("Remove Track", () => RemoveRowTrack(r), icon: EditorIcons.Trash, danger: true);
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
                    .BackgroundColor(row.IsMissing ? EditorTheme.Red400 : row.AccentColor).IsNotInteractable();
            }

            // Property name (with Missing indicator when path/component no longer resolves)
            string displayLabel = row.IsMissing ? $"{row.Label} (Missing!)" : row.Label;
            Color labelColor = row.IsMissing
                ? EditorTheme.Red400
                : muted
                    ? EditorTheme.Ink300
                    : (selected ? EditorTheme.Ink600 : EditorTheme.Ink500);

            paper.Box($"anim_trow_{index}_lbl").Width(UnitValue.Stretch()).Height(TrackRowHeight).IsNotInteractable()
                .Text(displayLabel, row.Kind == TrackRowKind.Group ? (EditorTheme.FontSemiBold ?? font) : font)
                .TextColor(labelColor)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            // Live value editor / readout at current time
            if (!locked && !row.IsMissing)
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
                .OnClick(index, (idx, _) =>
                {
                    if (idx >= 0 && idx < _rows.Count)
                        RemoveRowTrack(_rows[idx]);
                });
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
                    MarkClipDirty();
                    RefreshLiveSample(enterPreviewInEditMode: true);
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
                .OnClick(index, (idx, _) =>
                {
                    if (idx >= 0 && idx < _rows.Count)
                        OpenObjectKeyPicker(_rows[idx], _currentTime);
                });
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
                    MarkClipDirty();
                    RefreshLiveSample(enterPreviewInEditMode: true);
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

        SelectorTabs tabs = typeof(Asset).IsAssignableFrom(targetType)
            ? SelectorTabs.Assets
            : (targetType == typeof(GameObject) || typeof(Component).IsAssignableFrom(targetType)
                ? SelectorTabs.Scene
                : (SelectorTabs.Assets | SelectorTabs.Scene));

        SelectorModal.Open($"Select {targetType.Name}", targetType, tabs, picked =>
        {
            if (!IsClipValid) return;
            Undo.RecordObject(ClipAsset!, "Set Object Keyframe");
            _clip!.SetObjectKey(row.RelativePath, row.ComponentType, row.PropertyPath, time, picked as EngineObject);
            MarkClipDirty();
            RefreshLiveSample(enterPreviewInEditMode: true);
        });
    }

    private void ToggleAddPropertyPopup()
    {
        if (_addPropertyOpen)
        {
            _addPropertyOpen = false;
            return;
        }

        _addPropertySearch = "";
        _addPropertySearchHighlight = 0;
        _addPropertyLastHighlightSearch = "";
        _addPropertyCollapsedNodes.Clear();
        _addPropertyNodes = BuildAddPropertyHierarchyNodes();
        RefreshAnimatedPropertySet();

        // Keep deep trees tidy by default on large rigs (> 24 objects), while keeping small/medium hierarchies expanded
        if (_addPropertyNodes.Count > 24)
        {
            for (int i = 0; i < _addPropertyNodes.Count; i++)
            {
                if (_addPropertyNodes[i].Depth >= 2 && _addPropertyNodes[i].HasChildren)
                    _addPropertyCollapsedNodes.Add(i);
            }
        }

        // If the user has a child/external GameObject selected in the Hierarchy, auto-select & expand to it!
        _addPropertySelectedNodeIndex = 0;
        GameObject? activeSceneGo = Selection.GetActiveAs<GameObject>();
        if (activeSceneGo.IsValid())
        {
            for (int i = 0; i < _addPropertyNodes.Count; i++)
            {
                GameObject? nodeGo = _addPropertyNodes[i].TargetObject;
                if (nodeGo.IsValid() && ReferenceEquals(nodeGo, activeSceneGo))
                {
                    _addPropertySelectedNodeIndex = i;
                    int p = _addPropertyNodes[i].ParentIndex;
                    while (p >= 0 && p < _addPropertyNodes.Count)
                    {
                        _addPropertyCollapsedNodes.Remove(p);
                        p = _addPropertyNodes[p].ParentIndex;
                    }
                    break;
                }
            }
        }

        _addPropertyOpen = true;
    }

    private void RefreshAnimatedPropertySet()
    {
        _addPropertyAnimatedSet.Clear();
        if (!IsClipValid) return;

        for (int i = 0; i < _clip!.CurveTracks.Count; i++)
        {
            SceneCurveTrack t = _clip.CurveTracks[i];
            _addPropertyAnimatedSet.Add((t.RelativePath, t.ComponentType, StripSubChannel(t.PropertyPath)));
        }
        for (int i = 0; i < _clip.ObjectTracks.Count; i++)
        {
            SceneObjectTrack t = _clip.ObjectTracks[i];
            _addPropertyAnimatedSet.Add((t.RelativePath, t.ComponentType, t.PropertyPath));
        }
        for (int i = 0; i < _clip.StringTracks.Count; i++)
        {
            SceneStringTrack t = _clip.StringTracks[i];
            _addPropertyAnimatedSet.Add((t.RelativePath, t.ComponentType, t.PropertyPath));
        }
    }

    private bool IsPropertyAlreadyAnimated(string relativePath, AnimatablePropertyDescriptor desc)
        => _addPropertyAnimatedSet.Contains((relativePath, desc.ComponentType, desc.PropertyPath));

    internal List<AddPropertyNode> BuildAddPropertyHierarchyNodes()
    {
        var nodes = new List<AddPropertyNode>();
        if (_targetRoot.IsNotValid()) return nodes;

        AppendAddPropertyNodes(
            root: _targetRoot!,
            current: _targetRoot!,
            into: nodes,
            parentIndex: -1,
            depth: 0,
            pathPrefix: string.Empty,
            breadcrumbPrefix: string.Empty,
            displayOverride: null,
            isExternalSlot: false);

        if (_animator.IsValid())
        {
            for (int i = 0; i < _animator!.ObjectBindings.Count; i++)
            {
                AnimatorObjectBinding binding = _animator.ObjectBindings[i];
                if (string.IsNullOrWhiteSpace(binding.SlotName) || binding.Target.IsNotValid()) continue;
                AppendAddPropertyNodes(
                    root: binding.Target!,
                    current: binding.Target!,
                    into: nodes,
                    parentIndex: -1,
                    depth: 0,
                    pathPrefix: $"@{binding.SlotName}",
                    breadcrumbPrefix: string.Empty,
                    displayOverride: binding.SlotName,
                    isExternalSlot: true);
            }
        }

        return nodes;
    }

    private static void AppendAddPropertyNodes(
        GameObject root,
        GameObject current,
        List<AddPropertyNode> into,
        int parentIndex,
        int depth,
        string pathPrefix,
        string breadcrumbPrefix,
        string? displayOverride,
        bool isExternalSlot)
    {
        if (current.IsNotValid()) return;

        string localRel = Transform.GetRelativePath(current.Transform, root.Transform);
        string relPath = string.IsNullOrEmpty(pathPrefix)
            ? localRel
            : (string.IsNullOrEmpty(localRel) ? pathPrefix : $"{pathPrefix}/{localRel}");

        string displayName = displayOverride ?? current.Name;
        string nodeLabelInBreadcrumb = isExternalSlot && depth == 0 ? $"@{displayName}" : displayName;
        string breadcrumb = string.IsNullOrEmpty(breadcrumbPrefix)
            ? (isExternalSlot ? $"@{displayName} (External)" : $"{displayName} (Root)")
            : $"{breadcrumbPrefix} › {nodeLabelInBreadcrumb}";
        string nextBreadcrumbPrefix = string.IsNullOrEmpty(breadcrumbPrefix)
            ? nodeLabelInBreadcrumb
            : $"{breadcrumbPrefix} › {nodeLabelInBreadcrumb}";

        List<AnimatablePropertyDescriptor> rawProps = SceneClipBinding.GetAnimatableProperties(current);
        var items = new List<AddPropertyItem>(rawProps.Count);
        for (int i = 0; i < rawProps.Count; i++)
        {
            AnimatablePropertyDescriptor prop = rawProps[i];
            if (prop.ComponentType == "GameObject" && prop.PropertyPath == "Enabled")
            {
                items.Add(new AddPropertyItem("", "Is Active (Enabled)", EditorIcons.Eye, prop));
            }
            else
            {
                string icon = prop.IsObjectReference
                    ? EditorIcons.Cube
                    : prop.IsString
                        ? EditorIcons.Font
                        : prop.PropertyPath == "Enabled"
                            ? EditorIcons.ToggleOn
                            : (prop.SubChannels.Length > 0 ? EditorIcons.Sliders : EditorIcons.Hashtag);
                items.Add(new AddPropertyItem(prop.ComponentType, prop.PropertyPath, icon, prop));
            }
        }

        int myIndex = into.Count;
        var node = new AddPropertyNode
        {
            Index = myIndex,
            ParentIndex = parentIndex,
            TargetObject = current,
            RelativePath = relPath,
            DisplayName = isExternalSlot && depth == 0 ? $"@{displayName}" : displayName,
            Breadcrumb = breadcrumb,
            Depth = depth,
            IsExternalSlot = isExternalSlot,
            Properties = items,
        };
        into.Add(node);

        Dictionary<string, int>? siblingCounts = null;
        Dictionary<string, int>? siblingSeen = null;
        if (current.Children.Count > 1)
        {
            for (int i = 0; i < current.Children.Count; i++)
            {
                GameObject c = current.Children[i];
                if (c.IsNotValid()) continue;
                siblingCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                siblingCounts[c.Name] = siblingCounts.TryGetValue(c.Name, out int cnt) ? cnt + 1 : 1;
            }
        }

        for (int i = 0; i < current.Children.Count; i++)
        {
            GameObject child = current.Children[i];
            if (child.IsNotValid()) continue;
            node.HasChildren = true;

            string childLabel = child.Name;
            if (siblingCounts != null && siblingCounts.TryGetValue(childLabel, out int total) && total > 1)
            {
                siblingSeen ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int idx = siblingSeen.TryGetValue(childLabel, out int prev) ? prev + 1 : 1;
                siblingSeen[childLabel] = idx;
                childLabel = $"{childLabel} ({idx})";
            }

            AppendAddPropertyNodes(
                root: root,
                current: child,
                into: into,
                parentIndex: myIndex,
                depth: depth + 1,
                pathPrefix: pathPrefix,
                breadcrumbPrefix: nextBreadcrumbPrefix,
                displayOverride: childLabel,
                isExternalSlot: isExternalSlot);
        }
    }

    internal static List<(AddPropertyNode Node, AddPropertyItem Item)> FilterAddPropertyNodes(
        IReadOnlyList<AddPropertyNode> nodes,
        string searchQuery)
    {
        var results = new List<(AddPropertyNode Node, AddPropertyItem Item)>();
        if (string.IsNullOrWhiteSpace(searchQuery)) return results;

        string[] tokens = searchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return results;

        for (int n = 0; n < nodes.Count; n++)
        {
            AddPropertyNode node = nodes[n];
            for (int p = 0; p < node.Properties.Count; p++)
            {
                AddPropertyItem item = node.Properties[p];
                bool allMatch = true;
                for (int t = 0; t < tokens.Length; t++)
                {
                    string tok = tokens[t];
                    bool hit =
                        node.DisplayName.Contains(tok, StringComparison.OrdinalIgnoreCase) ||
                        node.Breadcrumb.Contains(tok, StringComparison.OrdinalIgnoreCase) ||
                        node.RelativePath.Contains(tok, StringComparison.OrdinalIgnoreCase) ||
                        item.ComponentGroup.Contains(tok, StringComparison.OrdinalIgnoreCase) ||
                        item.PropertyLabel.Contains(tok, StringComparison.OrdinalIgnoreCase);
                    if (!hit)
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (allMatch)
                    results.Add((node, item));
            }
        }

        return results;
    }

    private bool IsHierarchyNodeVisible(AddPropertyNode node)
    {
        int p = node.ParentIndex;
        while (p >= 0 && p < _addPropertyNodes.Count)
        {
            if (_addPropertyCollapsedNodes.Contains(p))
                return false;
            p = _addPropertyNodes[p].ParentIndex;
        }
        return true;
    }

    private void DrawAddPropertyPopover(Paper paper, Scribe.FontFile font, ElementHandle trigger)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        Scribe.FontFile semiBold = EditorTheme.FontSemiBold ?? font;
        float triggerHeight = trigger.Data.LayoutRect.Size.Y > 0 ? (float)trigger.Data.LayoutRect.Size.Y : 20f;

        const float PopWidth = 556f;
        const float Pad = 6f;
        const float SearchH = 28f;
        const float BodyH = 312f;
        const float InnerW = PopWidth - Pad * 2f;
        const float LeftTreeW = 224f;
        const float RightPropsW = InnerW - LeftTreeW - 1f;

        if (paper.IsKeyPressed(PaperKey.Escape))
        {
            _addPropertyOpen = false;
            return;
        }

        using (paper.Column("anim_add_prop_pop")
            .PositionType(PositionType.SelfDirected)
            .Position(0f, triggerHeight + 4f)
            .Width(PopWidth)
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Popover)
            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1)
            .DropShadow(0, 14, 40, -6, EditorTheme.Shadow)
            .Rounded(m.ContainerRounding)
            .Padding(Pad)
            .Gap(5f)
            .HookToParent()
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .StopEventPropagation()
            .IsNotFocusable()
            .Enter())
        {
            using (paper.Row("anim_add_prop_search_row").Width(InnerW).Height(SearchH).Enter())
            {
                Origami.SearchField(
                    paper,
                    "anim_add_prop_search",
                    _addPropertySearch,
                    v => _addPropertySearch = v,
                    "Search object or property (e.g. Hand LocalPosition)...")
                    .AutoFocus()
                    .Show();
            }

            EditorGUI.Divider(paper, "anim_add_prop_sdiv");

            if (_addPropertyNodes.Count == 0)
            {
                using (paper.Box("anim_add_prop_empty_w").Size(InnerW, BodyH).Enter())
                    EditorGUI.EmptyState(paper, "anim_add_prop_empty", "No animatable properties found.", font);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_addPropertySearch))
            {
                DrawAddPropertySearchResults(paper, font, semiBold, InnerW, BodyH);
                return;
            }

            if (_addPropertySelectedNodeIndex < 0 || _addPropertySelectedNodeIndex >= _addPropertyNodes.Count)
                _addPropertySelectedNodeIndex = 0;

            AddPropertyNode selectedNode = _addPropertyNodes[_addPropertySelectedNodeIndex];

            using (paper.Row("anim_add_prop_split").Size(InnerW, BodyH).Clip().Enter())
            {
                // Left Pane: Indented GameObject Hierarchy Tree
                using (paper.Column("anim_add_prop_left").Size(LeftTreeW, BodyH).Enter())
                {
                    bool hasAnyNested = _addPropertyNodes.Any(n => n.HasChildren);
                    using (paper.Row("anim_add_hier_hdr").Width(LeftTreeW).Height(22f)
                        .Padding(6, 4, 0, 0).Gap(4).AlignItems(LayoutAlignment.Center)
                        .BackgroundColor(EditorTheme.Neutral200).Enter())
                    {
                        paper.Box("anim_add_hier_ic").Width(13).Height(22f).IsNotInteractable()
                            .Text(EditorIcons.FolderTree, font).TextColor(EditorTheme.Ink300)
                            .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                        paper.Box("anim_add_hier_lbl").Width(UnitValue.Stretch()).Height(22f).IsNotInteractable()
                            .Text("Hierarchy", semiBold).TextColor(EditorTheme.Ink400)
                            .FontSize(EditorTheme.FontSizeSmall * 0.92f).Alignment(TextAlignment.MiddleLeft);

                        if (hasAnyNested)
                        {
                            bool anyCollapsed = _addPropertyCollapsedNodes.Count > 0;
                            paper.Box("anim_add_hier_expall")
                                .Width(48f).Height(16f).Rounded(3)
                                .Hovered.BackgroundColor(EditorTheme.Neutral300).End()
                                .Text(anyCollapsed ? "Expand" : "Collapse", font)
                                .TextColor(EditorTheme.Ink300).FontSize(9.5f)
                                .Alignment(TextAlignment.MiddleCenter)
                                .Cursor(PaperCursor.Pointer)
                                .Tooltip(anyCollapsed ? "Expand all child GameObjects" : "Collapse child GameObjects")
                                .OnClick(0, (_, _) =>
                                {
                                    if (_addPropertyCollapsedNodes.Count > 0)
                                    {
                                        _addPropertyCollapsedNodes.Clear();
                                    }
                                    else
                                    {
                                        for (int i = 0; i < _addPropertyNodes.Count; i++)
                                        {
                                            if (_addPropertyNodes[i].Depth >= 1 && _addPropertyNodes[i].HasChildren)
                                                _addPropertyCollapsedNodes.Add(i);
                                        }
                                    }
                                });
                        }
                    }

                    EditorGUI.Divider(paper, "anim_add_hier_hdiv");

                    Origami.ScrollView(paper, "anim_add_hier_sv", LeftTreeW, BodyH - 23f)
                        .Padding(2)
                        .Body(() =>
                        {
                            bool drewExternalDivider = false;
                            for (int i = 0; i < _addPropertyNodes.Count; i++)
                            {
                                AddPropertyNode node = _addPropertyNodes[i];
                                if (!IsHierarchyNodeVisible(node)) continue;

                                if (node.IsExternalSlot && !drewExternalDivider)
                                {
                                    drewExternalDivider = true;
                                    paper.Box("anim_add_ext_sep").Height(1).Margin(4, 4, 4, 2).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
                                    paper.Box("anim_add_ext_lbl").Height(16).Padding(6, 4, 0, 0).IsNotInteractable()
                                        .Text("External (@Slot)", semiBold).TextColor(EditorTheme.Purple400)
                                        .FontSize(9.5f).Alignment(TextAlignment.MiddleLeft);
                                }

                                bool isSel = i == _addPropertySelectedNodeIndex;
                                float indent = MathF.Min(116f, 4f + node.Depth * 11f);

                                using (paper.Row($"anim_add_hnode_{i}")
                                    .Width(UnitValue.Stretch()).Height(23f)
                                    .Rounded(EditorTheme.Roundness)
                                    .Padding(indent, 6f, 0f, 0f).Gap(4f).AlignItems(LayoutAlignment.Center)
                                    .BackgroundColor(isSel ? EditorTheme.Selected : Color.Transparent)
                                    .Hovered.BackgroundColor(isSel ? EditorTheme.Selected : EditorTheme.Hover).End()
                                    .Cursor(PaperCursor.Pointer)
                                    .Tooltip(node.Breadcrumb)
                                    .OnClick(i, (idx, _) => _addPropertySelectedNodeIndex = idx)
                                    .Enter())
                                {
                                    if (node.HasChildren)
                                    {
                                        bool collapsed = _addPropertyCollapsedNodes.Contains(i);
                                        paper.Box($"anim_add_hcaret_{i}").Width(14f).Height(18f).Rounded(3)
                                            .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                                            .Text(collapsed ? EditorIcons.CaretRight : EditorIcons.CaretDown, font)
                                            .TextColor(EditorTheme.Ink400).FontSize(10f)
                                            .Alignment(TextAlignment.MiddleCenter)
                                            .StopEventPropagation()
                                            .OnClick(i, (idx, _) =>
                                            {
                                                if (!_addPropertyCollapsedNodes.Add(idx))
                                                    _addPropertyCollapsedNodes.Remove(idx);
                                            });
                                    }
                                    else
                                    {
                                        paper.Box($"anim_add_hleaf_{i}").Width(14f).Height(18f).IsNotInteractable()
                                            .Text(node.Depth > 0 ? "·" : "", font)
                                            .TextColor(EditorTheme.Ink300).FontSize(11f)
                                            .Alignment(TextAlignment.MiddleCenter);
                                    }

                                    string objIcon = node.IsExternalSlot && node.Depth == 0 ? EditorIcons.Link : EditorIcons.Cube;
                                    Color iconCol = node.IsExternalSlot && node.Depth == 0
                                        ? EditorTheme.Purple400
                                        : (isSel || node.Depth == 0 ? EditorTheme.Accent : EditorTheme.Ink400);

                                    paper.Box($"anim_add_hic_{i}").Width(14f).Height(23f).IsNotInteractable()
                                        .Text(objIcon, font).TextColor(iconCol)
                                        .FontSize(10.5f).Alignment(TextAlignment.MiddleCenter);

                                    paper.Box($"anim_add_hnm_{i}").Width(UnitValue.Stretch()).Height(23f).IsNotInteractable()
                                        .Text(node.DisplayName, isSel || node.Depth == 0 ? semiBold : font)
                                        .TextColor(isSel ? EditorTheme.Ink600 : EditorTheme.Ink500)
                                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                                        .TextTruncate();
                                }
                            }
                        });
                }

                paper.Box("anim_add_prop_vdiv").Width(1f).Height(BodyH).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

                // Right Pane: Breadcrumb Context Header + Component-Grouped Properties
                using (paper.Column("anim_add_prop_right").Size(RightPropsW, BodyH).Enter())
                {
                    using (paper.Row("anim_add_bc_hdr").Width(RightPropsW).Height(22f)
                        .Padding(8, 8, 0, 0).Gap(5).AlignItems(LayoutAlignment.Center)
                        .BackgroundColor(EditorTheme.Neutral200)
                        .Tooltip(selectedNode.Breadcrumb)
                        .Enter())
                    {
                        string bcIcon = selectedNode.IsExternalSlot ? EditorIcons.Link : EditorIcons.Cube;
                        Color bcIconCol = selectedNode.IsExternalSlot ? EditorTheme.Purple400 : EditorTheme.Accent;
                        paper.Box("anim_add_bc_ic").Width(13f).Height(22f).IsNotInteractable()
                            .Text(bcIcon, font).TextColor(bcIconCol)
                            .FontSize(10.5f).Alignment(TextAlignment.MiddleCenter);

                        paper.Box("anim_add_bc_txt").Width(UnitValue.Stretch()).Height(22f).IsNotInteractable()
                            .Text(selectedNode.Breadcrumb, semiBold).TextColor(EditorTheme.Ink500)
                            .FontSize(EditorTheme.FontSizeSmall * 0.92f).Alignment(TextAlignment.MiddleLeft)
                            .TextTruncate();
                    }

                    EditorGUI.Divider(paper, "anim_add_bc_div");

                    Origami.ScrollView(paper, "anim_add_props_sv", RightPropsW, BodyH - 23f)
                        .Padding(3)
                        .Body(() =>
                        {
                            if (selectedNode.Properties.Count == 0)
                            {
                                EditorGUI.EmptyState(paper, "anim_add_node_empty", "No animatable properties on this object.", font);
                                return;
                            }

                            string? lastComp = null;
                            bool compCollapsed = false;

                            for (int p = 0; p < selectedNode.Properties.Count; p++)
                            {
                                AddPropertyItem item = selectedNode.Properties[p];

                                if (item.ComponentGroup.Length == 0)
                                {
                                    // Direct GameObject property ("Is Active (Enabled)")
                                    DrawAddPropertyLeafRow(
                                        paper, font, semiBold,
                                        rowId: $"anim_add_p_{selectedNode.Index}_{p}",
                                        node: selectedNode,
                                        item: item,
                                        displayLabel: item.PropertyLabel,
                                        indent: 6f,
                                        highlighted: false);
                                    continue;
                                }

                                if (!string.Equals(lastComp, item.ComponentGroup, StringComparison.Ordinal))
                                {
                                    lastComp = item.ComponentGroup;
                                    compCollapsed = _addPropertyCollapsedComponents.Contains(lastComp);
                                    string capturedComp = lastComp;

                                    paper.Box($"anim_add_csep_{p}").Height(2f).IsNotInteractable();
                                    using (paper.Row($"anim_add_chdr_{capturedComp}")
                                        .Width(UnitValue.Stretch()).Height(22f)
                                        .Rounded(EditorTheme.Roundness)
                                        .Padding(6f, 6f, 0f, 0f).Gap(5f).AlignItems(LayoutAlignment.Center)
                                        .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral300, 110))
                                        .Hovered.BackgroundColor(EditorTheme.Hover).End()
                                        .Cursor(PaperCursor.Pointer)
                                        .OnClick(0, (_, _) =>
                                        {
                                            if (!_addPropertyCollapsedComponents.Add(capturedComp))
                                                _addPropertyCollapsedComponents.Remove(capturedComp);
                                        })
                                        .Enter())
                                    {
                                        paper.Box($"anim_add_ccaret_{capturedComp}").Width(12f).Height(22f).IsNotInteractable()
                                            .Text(compCollapsed ? EditorIcons.CaretRight : EditorIcons.CaretDown, font)
                                            .TextColor(EditorTheme.Ink400).FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                                        paper.Box($"anim_add_cic_{capturedComp}").Width(13f).Height(22f).IsNotInteractable()
                                            .Text(EditorIcons.PuzzlePiece, font).TextColor(EditorTheme.Blue400)
                                            .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                                        paper.Box($"anim_add_cnm_{capturedComp}").Width(UnitValue.Stretch()).Height(22f).IsNotInteractable()
                                            .Text(capturedComp, semiBold).TextColor(EditorTheme.Ink500)
                                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                                            .TextTruncate();
                                    }
                                }

                                if (compCollapsed) continue;

                                DrawAddPropertyLeafRow(
                                    paper, font, semiBold,
                                    rowId: $"anim_add_p_{selectedNode.Index}_{p}",
                                    node: selectedNode,
                                    item: item,
                                    displayLabel: item.PropertyLabel,
                                    indent: 20f,
                                    highlighted: false);
                            }
                        });
                }
            }
        }
    }

    private void DrawAddPropertySearchResults(Paper paper, Scribe.FontFile font, Scribe.FontFile semiBold, float width, float height)
    {
        List<(AddPropertyNode Node, AddPropertyItem Item)> matches = FilterAddPropertyNodes(_addPropertyNodes, _addPropertySearch);
        if (matches.Count == 0)
        {
            using (paper.Box("anim_add_srch_empty_w").Size(width, height).Enter())
                EditorGUI.EmptyState(paper, "anim_add_srch_empty", "No matching objects or properties.", font);
            return;
        }

        if (_addPropertyLastHighlightSearch != _addPropertySearch)
        {
            _addPropertyLastHighlightSearch = _addPropertySearch;
            _addPropertySearchHighlight = 0;
        }
        if (paper.IsKeyPressedOrRepeating(PaperKey.Down)) _addPropertySearchHighlight++;
        if (paper.IsKeyPressedOrRepeating(PaperKey.Up)) _addPropertySearchHighlight--;
        _addPropertySearchHighlight = Math.Clamp(_addPropertySearchHighlight, 0, matches.Count - 1);

        if (paper.IsKeyPressed(PaperKey.Enter) || paper.IsKeyPressed(PaperKey.KeypadEnter))
        {
            (AddPropertyNode pickNode, AddPropertyItem pickItem) = matches[_addPropertySearchHighlight];
            AddPropertyTrack(pickNode.RelativePath, pickItem.Descriptor, closePopup: true);
            return;
        }

        Origami.ScrollView(paper, "anim_add_srch_sv", width, height)
            .Padding(3)
            .Body(() =>
            {
                int lastNodeIdx = -1;
                for (int i = 0; i < matches.Count; i++)
                {
                    (AddPropertyNode node, AddPropertyItem item) = matches[i];
                    if (node.Index != lastNodeIdx)
                    {
                        if (lastNodeIdx >= 0)
                            paper.Box($"anim_add_srch_sep_{i}").Height(4f).IsNotInteractable();
                        lastNodeIdx = node.Index;

                        using (paper.Row($"anim_add_srch_hdr_{node.Index}_{i}")
                            .Width(UnitValue.Stretch()).Height(20f)
                            .Rounded(3f).Padding(6f, 6f, 0f, 0f).Gap(5f)
                            .AlignItems(LayoutAlignment.Center)
                            .BackgroundColor(EditorTheme.Neutral200)
                            .Enter())
                        {
                            string hdrIcon = node.IsExternalSlot ? EditorIcons.Link : EditorIcons.Cube;
                            Color hdrCol = node.IsExternalSlot ? EditorTheme.Purple400 : EditorTheme.Accent;
                            paper.Box($"anim_add_srch_hic_{i}").Width(13f).Height(20f).IsNotInteractable()
                                .Text(hdrIcon, font).TextColor(hdrCol)
                                .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                            paper.Box($"anim_add_srch_hbc_{i}").Width(UnitValue.Stretch()).Height(20f).IsNotInteractable()
                                .Text(node.Breadcrumb, semiBold).TextColor(EditorTheme.Ink500)
                                .FontSize(EditorTheme.FontSizeSmall * 0.92f).Alignment(TextAlignment.MiddleLeft)
                                .TextTruncate();
                        }
                    }

                    string label = item.ComponentGroup.Length > 0
                        ? $"{item.ComponentGroup}.{item.PropertyLabel}"
                        : item.PropertyLabel;

                    DrawAddPropertyLeafRow(
                        paper, font, semiBold,
                        rowId: $"anim_add_srch_row_{i}",
                        node: node,
                        item: item,
                        displayLabel: label,
                        indent: 18f,
                        highlighted: i == _addPropertySearchHighlight);
                }
            });
    }

    private void DrawAddPropertyLeafRow(
        Paper paper,
        Scribe.FontFile font,
        Scribe.FontFile semiBold,
        string rowId,
        AddPropertyNode node,
        AddPropertyItem item,
        string displayLabel,
        float indent,
        bool highlighted)
    {
        bool alreadyAdded = IsPropertyAlreadyAnimated(node.RelativePath, item.Descriptor);

        using (paper.Row(rowId)
            .Width(UnitValue.Stretch()).Height(24f)
            .Rounded(EditorTheme.Roundness)
            .Padding(indent, 6f, 0f, 0f).Gap(6f).AlignItems(LayoutAlignment.Center)
            .BackgroundColor(highlighted ? EditorTheme.Hover : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Cursor(PaperCursor.Pointer)
            .Tooltip($"{node.Breadcrumb} — {(item.ComponentGroup.Length > 0 ? $"{item.ComponentGroup}.{item.PropertyLabel}" : item.PropertyLabel)}")
            .OnClick(0, (_, _) => AddPropertyTrack(node.RelativePath, item.Descriptor, closePopup: true))
            .Enter())
        {
            paper.Box($"{rowId}_ic").Width(14f).Height(24f).IsNotInteractable()
                .Text(item.Icon, font).TextColor(alreadyAdded ? EditorTheme.Green400 : EditorTheme.Ink400)
                .FontSize(10.5f).Alignment(TextAlignment.MiddleCenter);

            paper.Box($"{rowId}_lbl").Width(UnitValue.Stretch()).Height(24f).IsNotInteractable()
                .Text(displayLabel, font)
                .TextColor(alreadyAdded ? EditorTheme.Ink400 : EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                .TextTruncate();

            if (alreadyAdded)
            {
                paper.Box($"{rowId}_chk").Width(18f).Height(18f).IsNotInteractable()
                    .Text(EditorIcons.Check, semiBold).TextColor(EditorTheme.Green400)
                    .FontSize(10f).Alignment(TextAlignment.MiddleCenter);
            }
            else
            {
                // '+' quick-add button adds the property while keeping the popover open so users can add multiple properties on deep children
                paper.Box($"{rowId}_plus").Width(20f).Height(18f).Rounded(3f)
                    .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Accent, 36))
                    .Hovered.BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Accent, 96)).End()
                    .Text(EditorIcons.Plus, semiBold).TextColor(EditorTheme.AccentBright)
                    .FontSize(9.5f).Alignment(TextAlignment.MiddleCenter)
                    .Tooltip("Add track and keep popup open")
                    .StopEventPropagation()
                    .OnClick(0, (_, _) => AddPropertyTrack(node.RelativePath, item.Descriptor, closePopup: false));
            }
        }
    }

    private void RelinkRowToGameObject(TrackRow row, GameObject newTarget)
    {
        if (!IsClipValid || _targetRoot.IsNotValid() || newTarget.IsNotValid()) return;

        string newRelPath;
        if (GameObject.IsChildOrSameTransform(newTarget, _targetRoot!))
        {
            newRelPath = Transform.GetRelativePath(newTarget.Transform, _targetRoot!.Transform);
        }
        else if (_animator.IsValid())
        {
            string slot = newTarget.Name;
            Undo.RecordObject(_animator!, "Bind External GameObject");
            _animator!.SetObjectBinding(slot, newTarget);
            MarkAnimatorEdited(_animator);
            newRelPath = $"@{slot}";
        }
        else
        {
            return;
        }

        Undo.RecordObject(ClipAsset!, "Relink Animation Track Path");
        if (row.Kind == TrackRowKind.Group && row.GroupTracks != null)
        {
            foreach (SceneCurveTrack ct in row.GroupTracks)
                ct.RelativePath = newRelPath;
        }
        else if (row.CurveTrack != null)
        {
            row.CurveTrack.RelativePath = newRelPath;
        }
        else if (row.ObjectTrack != null)
        {
            row.ObjectTrack.RelativePath = newRelPath;
        }
        else if (row.StringTrack != null)
        {
            row.StringTrack.RelativePath = newRelPath;
        }

        _clip!.Invalidate();
        MarkClipDirty();
        RefreshLiveSample();
    }

    private void OpenExternalObjectBindPicker()
    {
        if (_animator.IsNotValid()) return;
        SelectorModal.Open("Bind External Scene GameObject", typeof(GameObject), SelectorTabs.Scene, picked =>
        {
            if (picked is GameObject go && go.IsValid() && _animator.IsValid())
            {
                Undo.RecordObject(_animator!, "Bind External GameObject");
                _animator!.SetObjectBinding(go.Name, go);
                MarkAnimatorEdited(_animator);
            }
        });
    }

    internal void AddPropertyTrack(string relPath, AnimatablePropertyDescriptor desc, bool closePopup = true)
    {
        if (closePopup)
            _addPropertyOpen = false;
        if (!IsClipValid) return;

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
        MarkClipDirty();
        if (!closePopup)
            RefreshAnimatedPropertySet();
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

        MarkClipDirty();
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
        MarkClipDirty();
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

    public void SelectEvent(int eventIndex)
    {
        if (!IsClipValid || eventIndex < 0 || eventIndex >= _clip!.PropertyEvents.Count)
        {
            _selectedEventIndex = -1;
            return;
        }
        _selectedEventIndex = eventIndex;
        _selectedKeyIndex = -1;
        _selectedKeys.Clear();
        SetPlayhead(_clip.PropertyEvents[eventIndex].Time, snapToFrame: false);
    }

    internal SceneAnimationEvent? AddEventAtPlayhead(bool ranged = false)
    {
        if (!IsClipValid) return null;
        Undo.RecordObject(ClipAsset!, ranged ? "Add Ranged Animation Event" : "Add Animation Event");
        float defaultLength = ranged ? MathF.Max(0.25f, _clip!.Duration > 0f ? _clip.Duration * 0.25f : 0.25f) : 0f;

        // Default target to the currently selected row's GameObject if one is selected, else root
        string defaultRelPath = (_selectedRowIndex >= 0 && _selectedRowIndex < _rows.Count)
            ? _rows[_selectedRowIndex].RelativePath
            : string.Empty;

        var ev = new SceneAnimationEvent
        {
            Time = _currentTime,
            EndTime = ranged ? _currentTime + defaultLength : _currentTime,
            RelativePath = defaultRelPath,
            FunctionName = string.Empty,
            StayFunctionName = string.Empty,
            ExitFunctionName = string.Empty,
        };
        _clip!.PropertyEvents.Add(ev);
        _selectedEventIndex = _clip.PropertyEvents.Count - 1;
        _selectedKeyIndex = -1;
        _selectedKeys.Clear();
        _clip.Invalidate();
        MarkClipDirty();
        return ev;
    }

    private void DuplicateEvent(int index)
    {
        if (!IsClipValid || index < 0 || index >= _clip!.PropertyEvents.Count) return;
        Undo.RecordObject(ClipAsset!, "Duplicate Animation Event");
        SceneAnimationEvent src = _clip.PropertyEvents[index];
        SceneAnimationEvent copy = src.Clone();
        float step = src.IsRange ? src.Duration : (1f / MathF.Max(1f, _clip.FrameRate));
        copy.Time += step;
        copy.EndTime = src.IsRange ? copy.Time + src.Duration : copy.Time;
        _clip.PropertyEvents.Add(copy);
        _selectedEventIndex = _clip.PropertyEvents.Count - 1;
        _selectedKeyIndex = -1;
        _selectedKeys.Clear();
        _clip.Invalidate();
        MarkClipDirty();
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
                .OnDragging(e => OnDragEventBar(paper, e.RelativePosition, TimeFromLocalX))
                .OnDragEnd(_ => _dragPart = DragPart.None)
                .OnRightClick(e => OnRightClickEventBar(paper, e.RelativePosition, TimeFromLocalX, LocalXFromTime))
                .OnDoubleClick(e =>
                {
                    int hit = HitTestEventIndex(e.RelativePosition, LocalXFromTime);
                    if (hit >= 0)
                    {
                        SelectEvent(hit);
                        return;
                    }
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

    private int HitTestEventIndex(Float2 localPos, Func<float, float> xFromTime)
    {
        if (!IsClipValid) return -1;
        for (int i = _clip!.PropertyEvents.Count - 1; i >= 0; i--)
        {
            SceneAnimationEvent ev = _clip.PropertyEvents[i];
            float ex0 = xFromTime(ev.Time);
            if (ev.IsRange)
            {
                float ex1 = xFromTime(ev.EndTime);
                if (localPos.X >= ex0 - 6f && localPos.X <= ex1 + 6f)
                    return i;
            }
            else
            {
                float pillWidth = Math.Clamp(FormatEventBadgeText(ev).Length * 5.8f + 12f, 16f, 110f);
                if (localPos.X >= ex0 - 7f && localPos.X <= ex0 + pillWidth)
                    return i;
            }
        }
        return -1;
    }

    private void OnRightClickEventBar(Paper paper, Float2 localPos, Func<float, float> timeFromX, Func<float, float> xFromTime)
    {
        int hitIdx = HitTestEventIndex(localPos, xFromTime);
        if (hitIdx >= 0 && IsClipValid && hitIdx < _clip!.PropertyEvents.Count)
        {
            _selectedEventIndex = hitIdx;
            _selectedKeyIndex = -1;
            SceneAnimationEvent ev = _clip.PropertyEvents[hitIdx];
            string title = string.IsNullOrEmpty(ev.FunctionName) ? "Unassigned Event" : ev.FunctionName;

            Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
            {
                menu.Header($"{title} @ {ev.Time:0.00}s");
                if (_targetRoot.IsValid() && !string.IsNullOrEmpty(ev.FunctionName))
                    menu.Item(ev.IsRange ? "Test Fire Enter" : "Test Fire Event", () => SceneClipBinding.InvokeEvent(_targetRoot!, ev), icon: EditorIcons.Play);
                if (_targetRoot.IsValid() && ev.IsRange && !string.IsNullOrEmpty(ev.ExitFunctionName))
                {
                    menu.Item("Test Fire Exit", () =>
                    {
                        var tmpExit = ev.Clone();
                        tmpExit.FunctionName = ev.ExitFunctionName;
                        SceneClipBinding.InvokeEvent(_targetRoot!, tmpExit);
                    }, icon: EditorIcons.Play);
                }

                menu.Item(ev.IsRange ? "Convert to Instant Event" : "Convert to Ranged Event", () =>
                {
                    Undo.RecordObject(ClipAsset!, "Toggle Event Range Mode");
                    if (ev.IsRange)
                        ev.EndTime = ev.Time;
                    else
                        ev.EndTime = ev.Time + MathF.Max(0.25f, 1f / MathF.Max(1f, _clip.FrameRate));
                    _clip.Invalidate();
                    MarkClipDirty();
                }, icon: ev.IsRange ? EditorIcons.Bookmark : EditorIcons.ArrowsLeftRight);

                menu.Item("Snap to Playhead", () =>
                {
                    Undo.RecordObject(ClipAsset!, "Snap Event to Playhead");
                    float span = ev.Duration;
                    ev.Time = _currentTime;
                    ev.EndTime = ev.IsRange ? _currentTime + span : _currentTime;
                    _clip.Invalidate();
                    MarkClipDirty();
                });

                menu.Item("Duplicate Event", () => DuplicateEvent(hitIdx), icon: EditorIcons.Clone, shortcut: ShortcutKeys(ShortcutDuplicate));
                menu.Separator();
                menu.Item("Delete Event", () =>
                {
                    if (hitIdx < _clip.PropertyEvents.Count)
                    {
                        Undo.RecordObject(ClipAsset!, "Delete Animation Event");
                        _clip.PropertyEvents.RemoveAt(hitIdx);
                        _selectedEventIndex = -1;
                        _clip.Invalidate();
                        MarkClipDirty();
                    }
                }, icon: EditorIcons.Trash, danger: true, shortcut: ShortcutKeys(ShortcutDelete));
            });
            return;
        }

        SetPlayhead(timeFromX(localPos.X));
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
        {
            menu.Header($"Events @ {_currentTime:0.00}s");
            menu.Item("Add Instant Event", () => AddEventAtPlayhead(ranged: false), icon: EditorIcons.Bookmark, shortcut: ShortcutKeys(ShortcutAddEvent));
            menu.Item("Add Ranged Event", () => AddEventAtPlayhead(ranged: true), icon: EditorIcons.ArrowsLeftRight, shortcut: ShortcutKeys(ShortcutAddRangedEvent));
        });
    }

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
            else
            {
                float pillWidth = Math.Clamp(FormatEventBadgeText(ev).Length * 5.8f + 12f, 16f, 110f);
                if (localPos.X >= ex0 - 7f && localPos.X <= ex0 + pillWidth)
                {
                    _selectedEventIndex = i;
                    _selectedKeyIndex = -1;
                    _dragPart = DragPart.Event;
                    _dragKey = i;
                    _dragStartTime = ev.Time;
                    _dragEndTime = ev.EndTime;
                    _dragClickOffset = MathF.Max(0f, timeFromX(localPos.X) - ev.Time);
                    Undo.RecordObject(ClipAsset!, "Move Animation Event");
                    return;
                }
            }
        }

        _selectedEventIndex = -1;
        _dragPart = DragPart.Playhead;
        SetPlayhead(timeFromX(localPos.X));
    }

    private void OnDragEventBar(Paper paper, Float2 localPos, Func<float, float> timeFromX)
    {
        bool freeSnap = IsAltDown(paper);
        if (_dragPart == DragPart.Playhead)
        {
            SetPlayhead(timeFromX(localPos.X), snapToFrame: !freeSnap);
            return;
        }
        if (!IsClipValid || _dragKey < 0 || _dragKey >= _clip!.PropertyEvents.Count) return;

        float fps = MathF.Max(1f, _clip.FrameRate);
        float minStep = freeSnap ? 0.005f : (1f / fps);
        float rawT = MathF.Max(0f, timeFromX(localPos.X));
        float snappedT = freeSnap ? rawT : MathF.Round(rawT * fps) / fps;
        SceneAnimationEvent ev = _clip.PropertyEvents[_dragKey];

        if (_dragPart == DragPart.Event)
        {
            float span = MathF.Max(0f, _dragEndTime - _dragStartTime);
            float rawStart = MathF.Max(0f, rawT - _dragClickOffset);
            float newStart = freeSnap ? rawStart : MathF.Round(rawStart * fps) / fps;
            ev.Time = newStart;
            ev.EndTime = span > 1e-5f ? newStart + span : newStart;
            _clip.Invalidate();
            MarkClipDirty();
        }
        else if (_dragPart == DragPart.EventStart)
        {
            ev.Time = MathF.Min(snappedT, MathF.Max(0f, ev.EndTime - minStep));
            _clip.Invalidate();
            MarkClipDirty();
        }
        else if (_dragPart == DragPart.EventEnd)
        {
            ev.EndTime = MathF.Max(snappedT, ev.Time + minStep);
            _clip.Invalidate();
            MarkClipDirty();
        }
    }

    private static string FormatEventBadgeText(SceneAnimationEvent ev)
    {
        if (ev.IsRange)
        {
            string enter = string.IsNullOrEmpty(ev.FunctionName) ? "" : ev.FunctionName;
            string stay = string.IsNullOrEmpty(ev.StayFunctionName) ? "" : ev.StayFunctionName;
            string exit = string.IsNullOrEmpty(ev.ExitFunctionName) ? "" : ev.ExitFunctionName;
            if (enter.Length == 0 && stay.Length == 0 && exit.Length == 0)
                return "<Select Range Callbacks>";
            if (enter.Length > 0 && exit.Length > 0)
                return $"{enter} -> {exit}";
            if (enter.Length > 0) return enter;
            if (stay.Length > 0) return $"Stay: {stay}";
            return $"Exit: {exit}";
        }

        if (string.IsNullOrEmpty(ev.FunctionName))
            return "<Select Function>";

        return ev.ArgType switch
        {
            ProwlActionArgType.Float => $"{ev.FunctionName}({ev.FloatParam:0.##})",
            ProwlActionArgType.Int => $"{ev.FunctionName}({ev.IntParam})",
            ProwlActionArgType.Bool => $"{ev.FunctionName}({(ev.BoolParam ? "true" : "false")})",
            ProwlActionArgType.String => $"{ev.FunctionName}(\"{ev.StringParam}\")",
            ProwlActionArgType.Object => $"{ev.FunctionName}({(ev.ObjectParam.IsValid() ? ev.ObjectParam!.Name : "None")})",
            _ => $"{ev.FunctionName}()",
        };
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
                bool unassigned = string.IsNullOrEmpty(ev.FunctionName) && string.IsNullOrEmpty(ev.StayFunctionName) && string.IsNullOrEmpty(ev.ExitFunctionName);
                string badge = FormatEventBadgeText(ev);

                if (ev.IsRange)
                {
                    float ex0 = x + (ev.Time - _viewMinTime) / range * w;
                    float ex1 = x + (ev.EndTime - _viewMinTime) / range * w;
                    if (ex1 < x - 8f || ex0 > x + w + 8f) continue;

                    Color barCol = sel ? EditorTheme.Amber400 : (unassigned ? EditorTheme.Red400 : EditorTheme.Purple400);
                    float barW = MathF.Max(6f, ex1 - ex0);
                    canvas.RoundedRectFilled(ex0, y + 2f, barW, h - 4f, 3f, 3f, 3f, 3f, EditorTheme.ToColor32(barCol, sel ? 0.78f : 0.48f));
                    canvas.RectFilled(ex0, y + 2f, 2f, h - 4f, EditorTheme.ToColor32(barCol, 1f));
                    canvas.RectFilled(ex0 + barW - 2f, y + 2f, 2f, h - 4f, EditorTheme.ToColor32(barCol, 1f));

                    if (barW > 24f)
                    {
                        int maxChars = Math.Max(3, (int)((barW - 10f) / 5.6f));
                        string clipped = badge.Length > maxChars ? badge[..maxChars] : badge;
                        canvas.DrawText(clipped, ex0 + 5f, y + 4f, Color32.FromArgb(245, 255, 255, 255), 9.5f, font);
                    }
                }
                else
                {
                    float ex = x + (ev.Time - _viewMinTime) / range * w;
                    if (ex < x - 60f || ex > x + w + 8f) continue;

                    Color col = sel ? EditorTheme.Amber400 : (unassigned ? EditorTheme.Red400 : EditorTheme.Blue400);
                    float pillW = Math.Clamp(badge.Length * 5.6f + 10f, 18f, 108f);
                    int maxChars = Math.Max(3, (int)((pillW - 8f) / 5.6f));
                    string clipped = badge.Length > maxChars ? badge[..maxChars] : badge;

                    canvas.RoundedRectFilled(ex + 2f, y + 3f, pillW, h - 6f, 3f, 3f, 3f, 3f, EditorTheme.ToColor32(col, sel ? 0.45f : 0.24f));
                    canvas.RectFilled(ex - 1f, y + 2f, 2f, h - 4f, EditorTheme.ToColor32(col, 1f));
                    canvas.CircleFilled(ex, y + h * 0.5f, sel ? 4.5f : 3.5f, EditorTheme.ToColor32(col, 1f));
                    canvas.DrawText(clipped, ex + 6f, y + 4.5f, sel ? Color32.FromArgb(255, 255, 255, 255) : Color32.FromArgb(220, 235, 240, 250), 9.5f, font);
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
    }

    private void HandlePanelShortcuts(Paper paper)
    {
        if (!IsClipValid || _addPropertyOpen || ContextMenu.IsOpen || ShortcutManager.IsRebinding || paper.FocusedElementId != 0)
            return;

        if (!_panelScreenRect.Contains(paper.PointerPos))
            return;

        float fps = MathF.Max(1f, _clip!.FrameRate);

        if (ShortcutManager.IsPressed(ShortcutPlayPause))
        {
            TogglePlayPause();
        }
        else if (ShortcutManager.IsPressed(ShortcutFirstFrame))
        {
            _playing = false;
            SetPlayhead(0f);
        }
        else if (ShortcutManager.IsPressed(ShortcutPreviousFrame))
        {
            _playing = false;
            SetPlayhead(MathF.Max(0f, _currentTime - 1f / fps));
        }
        else if (ShortcutManager.IsPressed(ShortcutNextFrame))
        {
            _playing = false;
            SetPlayhead(_currentTime + 1f / fps);
        }
        else if (ShortcutManager.IsPressed(ShortcutLastFrame))
        {
            _playing = false;
            SetPlayhead(_clip.Duration);
        }
        else if (ShortcutManager.IsPressed(ShortcutTogglePreview))
        {
            if (!Application.IsPlaying)
            {
                if (_previewing) StopPreviewAndRestore();
                else { EnterPreviewMode(); SampleAtPlayhead(); }
            }
        }
        else if (ShortcutManager.IsPressed(ShortcutToggleRecord))
        {
            if (_targetRoot.IsValid() && !NeedsAnimatorCreation)
                ToggleRecording();
        }
        else if (ShortcutManager.IsPressed(ShortcutKeyAtPlayhead))
        {
            KeyAtPlayhead();
        }
        else if (ShortcutManager.IsPressed(ShortcutAddEvent))
        {
            AddEventAtPlayhead(ranged: false);
        }
        else if (ShortcutManager.IsPressed(ShortcutAddRangedEvent))
        {
            AddEventAtPlayhead(ranged: true);
        }
        else if (ShortcutManager.IsPressed(ShortcutDelete))
        {
            DeleteSelectedKeysOrEvent();
        }
        else if (ShortcutManager.IsPressed(ShortcutSelectAll))
        {
            SelectAllKeys();
        }
        else if (ShortcutManager.IsPressed(ShortcutCopy))
        {
            CopySelectedKeys();
        }
        else if (ShortcutManager.IsPressed(ShortcutPaste))
        {
            PasteCopiedKeysAt(_currentTime);
        }
        else if (ShortcutManager.IsPressed(ShortcutDuplicate))
        {
            if (_selectedEventIndex >= 0 && _selectedEventIndex < _clip.PropertyEvents.Count)
                DuplicateEvent(_selectedEventIndex);
            else
                DuplicateSelectedKeys();
        }
        else if (ShortcutManager.IsPressed(ShortcutFrameSelection))
        {
            FrameSelectedOrAll();
        }
        else if (ShortcutManager.IsPressed(ShortcutToggleCurvesView))
        {
            _curvesMode = !_curvesMode;
            if (_curvesMode) _curveBoundsInitialized = false;
        }
        else if (ShortcutManager.IsPressed(ShortcutSetWorkRangeIn))
        {
            _useWorkRange = true;
            _workRangeIn = _currentTime;
            if (_workRangeOut <= _workRangeIn)
                _workRangeOut = _workRangeIn + MathF.Max(0.25f, 1f / fps);
        }
        else if (ShortcutManager.IsPressed(ShortcutSetWorkRangeOut))
        {
            _useWorkRange = true;
            _workRangeOut = MathF.Max(_currentTime, _workRangeIn + 1f / fps);
        }
        else if (ShortcutManager.IsPressed(ShortcutToggleWorkRange))
        {
            _useWorkRange = !_useWorkRange;
            if (_useWorkRange && _workRangeOut <= _workRangeIn + 1e-4f)
            {
                _workRangeIn = 0f;
                _workRangeOut = MathF.Max(0.25f, _clip.Duration > 0f ? _clip.Duration : 1f);
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
        else
        {
            ScrollTrackListBy(-deltaY);
        }
    }

    private void OpenTimeRulerContextMenu(Paper paper, float clickTime)
    {
        float fps = IsClipValid ? MathF.Max(1f, _clip!.FrameRate) : 60f;
        float snapped = MathF.Max(0f, MathF.Round(clickTime * fps) / fps);
        int frame = (int)MathF.Round(snapped * fps);

        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
        {
            menu.Header($"Timeline @ {snapped:0.00}s (F{frame})");
            menu.Item("Move Playhead Here", () =>
            {
                _playing = false;
                SetPlayhead(snapped);
            }, shortcut: "LMB");
            menu.Item("Add Instant Event Here", () =>
            {
                SetPlayhead(snapped);
                AddEventAtPlayhead(ranged: false);
            }, icon: EditorIcons.Bookmark, shortcut: ShortcutKeys(ShortcutAddEvent));
            menu.Item("Add Ranged Event Here", () =>
            {
                SetPlayhead(snapped);
                AddEventAtPlayhead(ranged: true);
            }, icon: EditorIcons.ArrowsLeftRight, shortcut: ShortcutKeys(ShortcutAddRangedEvent));

            menu.Separator();
            menu.Submenu("Work Range", sub =>
            {
                sub.Item("Set In Point Here", () =>
                {
                    _useWorkRange = true;
                    _workRangeIn = snapped;
                    if (_workRangeOut <= _workRangeIn)
                        _workRangeOut = _workRangeIn + MathF.Max(0.25f, 1f / fps);
                }, shortcut: ShortcutKeys(ShortcutSetWorkRangeIn));
                sub.Item("Set Out Point Here", () =>
                {
                    _useWorkRange = true;
                    _workRangeOut = MathF.Max(snapped, _workRangeIn + 1f / fps);
                }, shortcut: ShortcutKeys(ShortcutSetWorkRangeOut));
                sub.Item("Enable Work Range", () => _useWorkRange = !_useWorkRange, on: _useWorkRange, shortcut: ShortcutKeys(ShortcutToggleWorkRange));
            });

            menu.Separator();
            menu.Item("Frame All", FrameSelectedOrAll, shortcut: ShortcutKeys(ShortcutFrameSelection));
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

        foreach ((SceneCurveTrack ct, HashSet<int> indices) in _selectedDirectCurveKeys)
        {
            foreach (int kIdx in indices)
            {
                if (kIdx < 0 || kIdx >= ct.Curve.Count) continue;
                Keyframe kf = ct.Curve[kIdx];
                minT = MathF.Min(minT, kf.Time);
                maxT = MathF.Max(maxT, kf.Time);
                minV = MathF.Min(minV, kf.Value);
                maxV = MathF.Max(maxV, kf.Value);
                count++;
            }
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

    private void ResolveSelectedUnderlyingKeys(
        Dictionary<SceneCurveTrack, HashSet<int>> curveKeys,
        Dictionary<SceneObjectTrack, HashSet<int>> objectKeys,
        Dictionary<SceneStringTrack, HashSet<int>> stringKeys)
    {
        if (_selectedKeys.Count == 0 && _selectedRowIndex >= 0 && _selectedKeyIndex >= 0)
            _selectedKeys.Add((_selectedRowIndex, _selectedKeyIndex));

        foreach ((int rIdx, int kIdx) in _selectedKeys)
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
                    for (int k = 0; k < ct.Curve.Count; k++)
                    {
                        if (MathF.Abs(ct.Curve[k].Time - targetTime) <= 0.0005f)
                        {
                            if (!curveKeys.TryGetValue(ct, out HashSet<int>? set))
                                curveKeys[ct] = set = [];
                            set.Add(k);
                        }
                    }
                }
            }
            else if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null && kIdx >= 0 && kIdx < row.CurveTrack.Curve.Count)
            {
                if (!curveKeys.TryGetValue(row.CurveTrack, out HashSet<int>? set))
                    curveKeys[row.CurveTrack] = set = [];
                set.Add(kIdx);
            }
            else if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null && kIdx >= 0 && kIdx < row.ObjectTrack.Keyframes.Count)
            {
                if (!objectKeys.TryGetValue(row.ObjectTrack, out HashSet<int>? set))
                    objectKeys[row.ObjectTrack] = set = [];
                set.Add(kIdx);
            }
            else if (row.Kind == TrackRowKind.String && row.StringTrack != null && kIdx >= 0 && kIdx < row.StringTrack.Keyframes.Count)
            {
                if (!stringKeys.TryGetValue(row.StringTrack, out HashSet<int>? set))
                    stringKeys[row.StringTrack] = set = [];
                set.Add(kIdx);
            }
        }

        foreach ((SceneCurveTrack ct, HashSet<int> directSet) in _selectedDirectCurveKeys)
        {
            if (ct.Locked) continue;
            if (!curveKeys.TryGetValue(ct, out HashSet<int>? set))
                curveKeys[ct] = set = [];
            foreach (int kIdx in directSet)
            {
                if (kIdx >= 0 && kIdx < ct.Curve.Count)
                    set.Add(kIdx);
            }
        }
    }

    private void RebuildSelectionFromTrackTimes(
        Dictionary<SceneCurveTrack, List<float>> curveTimes,
        Dictionary<SceneObjectTrack, List<float>> objectTimes,
        Dictionary<SceneStringTrack, List<float>> stringTimes)
    {
        _selectedKeys.Clear();
        _selectedDirectCurveKeys.Clear();
        _selectedKeyIndex = -1;
        bool keepRowSelection = (_selectedRowIndex >= 0 && _selectedRowIndex < _rows.Count && _rows[_selectedRowIndex].Kind == TrackRowKind.Group)
            || _selectedTrackRows.Count > 1;
        var matchedExpandedTracks = new HashSet<SceneCurveTrack>(ReferenceEqualityComparer.Instance);

        for (int i = 0; i < _rows.Count; i++)
        {
            TrackRow row = _rows[i];
            if (row.Kind == TrackRowKind.Group && row.GroupTracks != null)
            {
                row.SummaryTimes = ComputeGroupSummaryTimes(row.GroupTracks);
                if (!_curvesMode)
                {
                    foreach (SceneCurveTrack ct in row.GroupTracks)
                    {
                        if (!curveTimes.TryGetValue(ct, out List<float>? times)) continue;
                        for (int k = 0; k < row.SummaryTimes.Count; k++)
                        {
                            float st = row.SummaryTimes[k];
                            for (int t = 0; t < times.Count; t++)
                            {
                                if (MathF.Abs(st - times[t]) <= 0.0005f)
                                {
                                    _selectedKeys.Add((i, k));
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null && curveTimes.TryGetValue(row.CurveTrack, out List<float>? cTimes))
            {
                matchedExpandedTracks.Add(row.CurveTrack);
                AnimationCurve c = row.CurveTrack.Curve;
                for (int k = 0; k < c.Count; k++)
                {
                    for (int t = 0; t < cTimes.Count; t++)
                    {
                        if (MathF.Abs(c[k].Time - cTimes[t]) <= 0.0005f)
                        {
                            _selectedKeys.Add((i, k));
                            if (!keepRowSelection && (i == _selectedRowIndex || _selectedKeyIndex < 0))
                            {
                                _selectedRowIndex = i;
                                _selectedKeyIndex = k;
                            }
                            else if (i == _selectedRowIndex)
                            {
                                _selectedKeyIndex = k;
                            }
                            break;
                        }
                    }
                }
            }
            else if (row.Kind == TrackRowKind.Object && row.ObjectTrack != null && objectTimes.TryGetValue(row.ObjectTrack, out List<float>? oTimes))
            {
                List<SceneObjectKeyframe> kfs = row.ObjectTrack.Keyframes;
                for (int k = 0; k < kfs.Count; k++)
                {
                    for (int t = 0; t < oTimes.Count; t++)
                    {
                        if (MathF.Abs(kfs[k].Time - oTimes[t]) <= 0.0005f)
                        {
                            _selectedKeys.Add((i, k));
                            if (i == _selectedRowIndex || _selectedKeyIndex < 0)
                            {
                                _selectedRowIndex = i;
                                _selectedKeyIndex = k;
                            }
                            break;
                        }
                    }
                }
            }
            else if (row.Kind == TrackRowKind.String && row.StringTrack != null && stringTimes.TryGetValue(row.StringTrack, out List<float>? sTimes))
            {
                List<SceneStringKeyframe> kfs = row.StringTrack.Keyframes;
                for (int k = 0; k < kfs.Count; k++)
                {
                    for (int t = 0; t < sTimes.Count; t++)
                    {
                        if (MathF.Abs(kfs[k].Time - sTimes[t]) <= 0.0005f)
                        {
                            _selectedKeys.Add((i, k));
                            if (!keepRowSelection && (i == _selectedRowIndex || _selectedKeyIndex < 0))
                            {
                                _selectedRowIndex = i;
                                _selectedKeyIndex = k;
                            }
                            break;
                        }
                    }
                }
            }
        }

        // Preserve curve key selection for collapsed group tracks that have no expanded Curve row in _rows
        foreach ((SceneCurveTrack ct, List<float> cTimes) in curveTimes)
        {
            if (matchedExpandedTracks.Contains(ct)) continue;
            AnimationCurve c = ct.Curve;
            for (int k = 0; k < c.Count; k++)
            {
                for (int t = 0; t < cTimes.Count; t++)
                {
                    if (MathF.Abs(c[k].Time - cTimes[t]) <= 0.0005f)
                    {
                        if (!_selectedDirectCurveKeys.TryGetValue(ct, out HashSet<int>? set))
                            _selectedDirectCurveKeys[ct] = set = [];
                        set.Add(k);
                        break;
                    }
                }
            }
        }
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
            MarkClipDirty();
            return;
        }

        var curveKeys = new Dictionary<SceneCurveTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var objectKeys = new Dictionary<SceneObjectTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var stringKeys = new Dictionary<SceneStringTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        ResolveSelectedUnderlyingKeys(curveKeys, objectKeys, stringKeys);

        if (curveKeys.Count == 0 && objectKeys.Count == 0 && stringKeys.Count == 0) return;

        Undo.RecordObject(ClipAsset!, "Delete Selected Keyframes");

        foreach ((SceneCurveTrack track, HashSet<int> indices) in curveKeys)
        {
            foreach (int idx in indices.OrderByDescending(static x => x))
                if (idx >= 0 && idx < track.Curve.Count)
                    track.Curve.RemoveKey(idx);
        }
        foreach ((SceneObjectTrack track, HashSet<int> indices) in objectKeys)
        {
            foreach (int idx in indices.OrderByDescending(static x => x))
                if (idx >= 0 && idx < track.Keyframes.Count)
                    track.Keyframes.RemoveAt(idx);
        }
        foreach ((SceneStringTrack track, HashSet<int> indices) in stringKeys)
        {
            foreach (int idx in indices.OrderByDescending(static x => x))
                if (idx >= 0 && idx < track.Keyframes.Count)
                    track.Keyframes.RemoveAt(idx);
        }

        _selectedKeys.Clear();
        _selectedDirectCurveKeys.Clear();
        _selectedKeyIndex = -1;
        _clip!.Invalidate();
        MarkClipDirty();
        RefreshLiveSample();
    }

    private void SelectAllKeys()
    {
        _selectedKeys.Clear();
        _selectedDirectCurveKeys.Clear();
        _selectedEventIndex = -1;
        if (_curvesMode)
        {
            List<VisibleCurveChannel> vis = GetVisibleCurves();
            foreach (VisibleCurveChannel ch in vis)
            {
                if (ch.IsLocked) continue;
                for (int k = 0; k < ch.Track.Curve.Count; k++)
                {
                    if (ch.RowIndex >= 0)
                        _selectedKeys.Add((ch.RowIndex, k));
                    else
                    {
                        if (!_selectedDirectCurveKeys.TryGetValue(ch.Track, out HashSet<int>? set))
                            _selectedDirectCurveKeys[ch.Track] = set = [];
                        set.Add(k);
                    }
                }
            }
            return;
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            TrackRow row = _rows[i];
            if (IsRowLocked(row)) continue;
            int count = GetRowKeyCount(row);
            for (int k = 0; k < count; k++)
            {
                _selectedKeys.Add((i, k));
                if (_selectedKeyIndex < 0)
                {
                    _selectedRowIndex = i;
                    _selectedKeyIndex = k;
                }
            }
        }
    }

    private void CopySelectedKeys() => CopySelectedKeys(out _);

    private bool CopySelectedKeys(out float earliestTime)
    {
        earliestTime = 0f;
        if (!IsClipValid) return false;

        var curveKeys = new Dictionary<SceneCurveTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var objectKeys = new Dictionary<SceneObjectTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var stringKeys = new Dictionary<SceneStringTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        ResolveSelectedUnderlyingKeys(curveKeys, objectKeys, stringKeys);

        if (curveKeys.Count == 0 && objectKeys.Count == 0 && stringKeys.Count == 0) return false;

        float minT = float.MaxValue;
        foreach ((SceneCurveTrack track, HashSet<int> indices) in curveKeys)
            foreach (int idx in indices)
                minT = MathF.Min(minT, track.Curve[idx].Time);
        foreach ((SceneObjectTrack track, HashSet<int> indices) in objectKeys)
            foreach (int idx in indices)
                minT = MathF.Min(minT, track.Keyframes[idx].Time);
        foreach ((SceneStringTrack track, HashSet<int> indices) in stringKeys)
            foreach (int idx in indices)
                minT = MathF.Min(minT, track.Keyframes[idx].Time);

        if (minT == float.MaxValue) return false;
        earliestTime = minT;

        _copiedCurveKeys.Clear();
        _copiedObjectKeys.Clear();
        _copiedStringKeys.Clear();

        foreach ((SceneCurveTrack track, HashSet<int> indices) in curveKeys)
            foreach (int idx in indices)
            {
                Keyframe kf = track.Curve[idx];
                _copiedCurveKeys.Add((track.RelativePath, track.ComponentType, track.PropertyPath, kf.Time - minT, kf));
            }
        foreach ((SceneObjectTrack track, HashSet<int> indices) in objectKeys)
            foreach (int idx in indices)
            {
                SceneObjectKeyframe kf = track.Keyframes[idx];
                _copiedObjectKeys.Add((track.RelativePath, track.ComponentType, track.PropertyPath, kf.Time - minT, kf.Value));
            }
        foreach ((SceneStringTrack track, HashSet<int> indices) in stringKeys)
            foreach (int idx in indices)
            {
                SceneStringKeyframe kf = track.Keyframes[idx];
                _copiedStringKeys.Add((track.RelativePath, track.ComponentType, track.PropertyPath, kf.Time - minT, kf.Value));
            }

        return true;
    }

    private void DuplicateSelectedKeys()
    {
        if (!IsClipValid) return;
        if (!CopySelectedKeys(out float earliestTime)) return;
        float step = 1f / MathF.Max(1f, _clip!.FrameRate);
        PasteCopiedKeysAt(earliestTime + step);
    }

    private void PasteCopiedKeysAt(float baseTime)
    {
        if (!IsClipValid) return;
        if (_copiedCurveKeys.Count == 0 && _copiedObjectKeys.Count == 0 && _copiedStringKeys.Count == 0) return;

        Undo.RecordObject(ClipAsset!, "Paste Keyframes");
        var pastedCurveTimes = new Dictionary<SceneCurveTrack, List<float>>(ReferenceEqualityComparer.Instance);
        var pastedObjectTimes = new Dictionary<SceneObjectTrack, List<float>>(ReferenceEqualityComparer.Instance);
        var pastedStringTimes = new Dictionary<SceneStringTrack, List<float>>(ReferenceEqualityComparer.Instance);

        foreach (var item in _copiedCurveKeys)
        {
            float t = MathF.Max(0f, baseTime + item.RelTime);
            SceneCurveTrack track = _clip!.GetOrCreateCurveTrack(item.RelPath, item.CompType, item.PropPath);
            if (track.Locked) continue;
            Keyframe kf = item.Key;
            _clip.SetCurveKey(item.RelPath, item.CompType, item.PropPath, t, kf.Value);
            for (int k = 0; k < track.Curve.Count; k++)
            {
                if (MathF.Abs(track.Curve[k].Time - t) <= 0.0005f)
                {
                    track.Curve.MoveKey(k, new Keyframe(t, kf.Value, kf.InTangent, kf.OutTangent, kf.Interpolation));
                    break;
                }
            }
            if (!pastedCurveTimes.TryGetValue(track, out List<float>? list))
                pastedCurveTimes[track] = list = [];
            list.Add(t);
        }

        foreach (var item in _copiedObjectKeys)
        {
            float t = MathF.Max(0f, baseTime + item.RelTime);
            SceneObjectTrack track = _clip!.SetObjectKey(item.RelPath, item.CompType, item.PropPath, t, item.Value);
            if (!pastedObjectTimes.TryGetValue(track, out List<float>? list))
                pastedObjectTimes[track] = list = [];
            list.Add(t);
        }

        foreach (var item in _copiedStringKeys)
        {
            float t = MathF.Max(0f, baseTime + item.RelTime);
            SceneStringTrack track = _clip!.SetStringKey(item.RelPath, item.CompType, item.PropPath, t, item.Value);
            if (!pastedStringTimes.TryGetValue(track, out List<float>? list))
                pastedStringTimes[track] = list = [];
            list.Add(t);
        }

        _clip!.Invalidate();
        RebuildRows();
        RebuildSelectionFromTrackTimes(pastedCurveTimes, pastedObjectTimes, pastedStringTimes);
        MarkClipDirty();
        RefreshLiveSample();
    }

    private void ShiftSelectedKeysTime(float dt)
    {
        if (!IsClipValid || MathF.Abs(dt) <= 1e-6f) return;
        CaptureMultiDragSnapshot();
        if (_dragCurveSnapshots.Count == 0 && _dragObjectSnapshots.Count == 0 && _dragStringSnapshots.Count == 0) return;

        Undo.RecordObject(ClipAsset!, "Shift Selected Keyframes Time");
        ApplyMultiDrag(dt, 0f);
    }

    private static void ApplyTangentModeToKey(AnimationCurve curve, int index, CurveTangentMode mode)
    {
        if (index < 0 || index >= curve.Count) return;
        Keyframe cur = curve[index];
        if (mode == CurveTangentMode.Flat)
        {
            curve.MoveKey(index, new Keyframe(cur.Time, cur.Value, 0f, 0f, cur.Interpolation));
            return;
        }
        if (mode == CurveTangentMode.Linear)
        {
            float inSlope = 0f, outSlope = 0f;
            if (index > 0)
            {
                Keyframe prev = curve[index - 1];
                float dt = cur.Time - prev.Time;
                inSlope = dt > 1e-6f ? (cur.Value - prev.Value) / dt : 0f;
            }
            if (index < curve.Count - 1)
            {
                Keyframe next = curve[index + 1];
                float dt = next.Time - cur.Time;
                outSlope = dt > 1e-6f ? (next.Value - cur.Value) / dt : 0f;
            }
            if (index == 0) inSlope = outSlope;
            if (index == curve.Count - 1) outSlope = inSlope;
            curve.MoveKey(index, new Keyframe(cur.Time, cur.Value, inSlope, outSlope, cur.Interpolation));
            return;
        }
        AnimationClip.AutoSmoothSingleKey(curve, index);
    }

    private void ModifySelectedCurveKeys(string undoLabel, Func<Keyframe, Keyframe> transform, CurveTangentMode? smoothMode = null)
    {
        if (!IsClipValid) return;

        var curveKeys = new Dictionary<SceneCurveTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var objectKeys = new Dictionary<SceneObjectTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var stringKeys = new Dictionary<SceneStringTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        ResolveSelectedUnderlyingKeys(curveKeys, objectKeys, stringKeys);

        if (curveKeys.Count == 0) return;

        Undo.RecordObject(ClipAsset!, undoLabel);
        foreach ((SceneCurveTrack track, HashSet<int> indices) in curveKeys)
        {
            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= track.Curve.Count) continue;
                Keyframe updated = transform(track.Curve[idx]);
                track.Curve.MoveKey(idx, updated);
            }
            if (smoothMode.HasValue)
            {
                foreach (int idx in indices)
                    ApplyTangentModeToKey(track.Curve, idx, smoothMode.Value);
            }
        }

        _clip!.Invalidate();
        MarkClipDirty();
        RefreshLiveSample();
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
            .OnScroll(e =>
            {
                if (IsShiftDown(paper))
                    ScrollTrackListBy(-(float)e.Delta * TrackRowHeight * 2.5f);
                else
                    ZoomTimeline((float)e.Delta, (float)e.NormalizedPosition.X);
            });

        using (sheet.Enter())
        {
            paper.Draw((canvas, rect) => PaintDopeSheet(canvas, rect));
        }
    }

    private (int RowIndex, int KeyIndex) HitTestDopeSheetKey(Float2 localPos, Func<float, float> xFromTime)
    {
        int rowIdx = (int)MathF.Floor((localPos.Y + _trackScrollY) / TrackRowHeight);
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

        // Unity-style Dope Sheet Multi-Key Time-Stretching Bars (left/right edges of selection)
        if (_selectedKeys.Count > 1 && IsClipValid && TryGetSelectedKeyTimeBounds(out float selMinT, out float selMaxT) && selMaxT - selMinT > 0.001f)
        {
            float leftBarX = xFromTime(selMinT);
            float rightBarX = xFromTime(selMaxT);
            if (MathF.Abs(localPos.X - leftBarX) <= 6f)
            {
                _dragPart = DragPart.StretchLeft;
                _stretchAnchorTime = selMaxT;
                _stretchOrigSpan = selMinT - selMaxT; // negative
                CaptureMultiDragSnapshot();
                Undo.RecordObject(ClipAsset!, "Scale Selected Keyframes");
                return;
            }
            if (MathF.Abs(localPos.X - rightBarX) <= 6f)
            {
                _dragPart = DragPart.StretchRight;
                _stretchAnchorTime = selMinT;
                _stretchOrigSpan = selMaxT - selMinT; // positive
                CaptureMultiDragSnapshot();
                Undo.RecordObject(ClipAsset!, "Scale Selected Keyframes");
                return;
            }
        }

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
            _selectedTrackRows.Clear();
            _selectedTrackRows.Add(rowIdx);
            _selectedKeyIndex = keyIdx;
            _curveBoundsInitialized = false;
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
        _selectedTrackRows.Clear();
        if (rowIdx >= 0) _selectedTrackRows.Add(rowIdx);
        _curveBoundsInitialized = false;
        if (!shift && !ctrl)
        {
            _selectedKeys.Clear();
            _selectedDirectCurveKeys.Clear();
            _selectedKeyIndex = -1;
        }
        _dragPart = DragPart.MarqueeSelect;
        _marqueeStartLocal = localPos;
        _marqueeEndLocal = localPos;
    }

    private void CaptureMultiDragSnapshot()
    {
        _dragCurveSnapshots.Clear();
        _dragObjectSnapshots.Clear();
        _dragStringSnapshots.Clear();

        var curveKeys = new Dictionary<SceneCurveTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var objectKeys = new Dictionary<SceneObjectTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var stringKeys = new Dictionary<SceneStringTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        ResolveSelectedUnderlyingKeys(curveKeys, objectKeys, stringKeys);

        foreach ((SceneCurveTrack track, HashSet<int> indices) in curveKeys)
        {
            var arr = new Keyframe[track.Curve.Count];
            for (int k = 0; k < arr.Length; k++) arr[k] = track.Curve[k];
            _dragCurveSnapshots.Add((track, arr, indices));
        }

        foreach ((SceneObjectTrack track, HashSet<int> indices) in objectKeys)
        {
            var arr = new SceneObjectKeyframe[track.Keyframes.Count];
            for (int k = 0; k < arr.Length; k++) arr[k] = track.Keyframes[k].Clone();
            _dragObjectSnapshots.Add((track, arr, indices));
        }

        foreach ((SceneStringTrack track, HashSet<int> indices) in stringKeys)
        {
            var arr = new SceneStringKeyframe[track.Keyframes.Count];
            for (int k = 0; k < arr.Length; k++) arr[k] = track.Keyframes[k].Clone();
            _dragStringSnapshots.Add((track, arr, indices));
        }
    }

    internal void ApplyMultiDrag(float dt, float dv)
    {
        if (!IsClipValid) return;

        var movedCurveTimes = new Dictionary<SceneCurveTrack, List<float>>(ReferenceEqualityComparer.Instance);
        var movedObjectTimes = new Dictionary<SceneObjectTrack, List<float>>(ReferenceEqualityComparer.Instance);
        var movedStringTimes = new Dictionary<SceneStringTrack, List<float>>(ReferenceEqualityComparer.Instance);

        for (int i = 0; i < _dragCurveSnapshots.Count; i++)
        {
            (SceneCurveTrack track, Keyframe[] orig, HashSet<int> dragged) = _dragCurveSnapshots[i];
            if (track.Locked) continue;

            var movedTimes = new List<float>(dragged.Count);
            track.Curve.Clear();
            for (int k = 0; k < orig.Length; k++)
            {
                Keyframe kf = orig[k];
                if (dragged.Contains(k))
                {
                    float nt = MathF.Max(0f, kf.Time + dt);
                    float nv = kf.Value + dv;
                    track.Curve.AddKey(new Keyframe(nt, nv, kf.InTangent, kf.OutTangent, kf.Interpolation));
                    movedTimes.Add(nt);
                }
                else
                {
                    track.Curve.AddKey(kf);
                }
            }
            movedCurveTimes[track] = movedTimes;
        }

        for (int i = 0; i < _dragObjectSnapshots.Count; i++)
        {
            (SceneObjectTrack track, SceneObjectKeyframe[] orig, HashSet<int> dragged) = _dragObjectSnapshots[i];
            if (track.Locked) continue;

            var movedTimes = new List<float>(dragged.Count);
            track.Keyframes.Clear();
            for (int k = 0; k < orig.Length; k++)
            {
                SceneObjectKeyframe kf = orig[k].Clone();
                if (dragged.Contains(k))
                {
                    kf.Time = MathF.Max(0f, kf.Time + dt);
                    movedTimes.Add(kf.Time);
                }
                track.Keyframes.Add(kf);
            }
            track.SortKeyframes();
            movedObjectTimes[track] = movedTimes;
        }

        for (int i = 0; i < _dragStringSnapshots.Count; i++)
        {
            (SceneStringTrack track, SceneStringKeyframe[] orig, HashSet<int> dragged) = _dragStringSnapshots[i];
            if (track.Locked) continue;

            var movedTimes = new List<float>(dragged.Count);
            track.Keyframes.Clear();
            for (int k = 0; k < orig.Length; k++)
            {
                SceneStringKeyframe kf = orig[k].Clone();
                if (dragged.Contains(k))
                {
                    kf.Time = MathF.Max(0f, kf.Time + dt);
                    movedTimes.Add(kf.Time);
                }
                track.Keyframes.Add(kf);
            }
            track.SortKeyframes();
            movedStringTimes[track] = movedTimes;
        }

        RebuildSelectionFromTrackTimes(movedCurveTimes, movedObjectTimes, movedStringTimes);
        _clip!.Invalidate();
        MarkClipDirty();
        RefreshLiveSample();
    }

    private bool TryGetSelectedKeyTimeBounds(out float minT, out float maxT)
    {
        minT = float.MaxValue;
        maxT = float.MinValue;
        int count = 0;
        foreach ((int rIdx, int kIdx) in _selectedKeys)
        {
            if (rIdx < 0 || rIdx >= _rows.Count) continue;
            float t = GetKeyTime(_rows[rIdx], kIdx);
            minT = MathF.Min(minT, t);
            maxT = MathF.Max(maxT, t);
            count++;
        }
        return count >= 2 && maxT > minT;
    }

    private void ApplyMultiTimeScale(float anchorTime, float scale, bool snapToFrame)
    {
        if (!IsClipValid) return;
        float fps = MathF.Max(1f, _clip!.FrameRate);

        var movedCurveTimes = new Dictionary<SceneCurveTrack, List<float>>(ReferenceEqualityComparer.Instance);
        var movedObjectTimes = new Dictionary<SceneObjectTrack, List<float>>(ReferenceEqualityComparer.Instance);
        var movedStringTimes = new Dictionary<SceneStringTrack, List<float>>(ReferenceEqualityComparer.Instance);

        float TransformTime(float origT)
        {
            float nt = MathF.Max(0f, anchorTime + (origT - anchorTime) * scale);
            return snapToFrame ? MathF.Round(nt * fps) / fps : nt;
        }

        for (int i = 0; i < _dragCurveSnapshots.Count; i++)
        {
            (SceneCurveTrack track, Keyframe[] orig, HashSet<int> dragged) = _dragCurveSnapshots[i];
            if (track.Locked) continue;

            var movedTimes = new List<float>(dragged.Count);
            track.Curve.Clear();
            for (int k = 0; k < orig.Length; k++)
            {
                Keyframe kf = orig[k];
                if (dragged.Contains(k))
                {
                    float nt = TransformTime(kf.Time);
                    track.Curve.AddKey(new Keyframe(nt, kf.Value, kf.InTangent / scale, kf.OutTangent / scale, kf.Interpolation));
                    movedTimes.Add(nt);
                }
                else
                {
                    track.Curve.AddKey(kf);
                }
            }
            movedCurveTimes[track] = movedTimes;
        }

        for (int i = 0; i < _dragObjectSnapshots.Count; i++)
        {
            (SceneObjectTrack track, SceneObjectKeyframe[] orig, HashSet<int> dragged) = _dragObjectSnapshots[i];
            if (track.Locked) continue;

            var movedTimes = new List<float>(dragged.Count);
            track.Keyframes.Clear();
            for (int k = 0; k < orig.Length; k++)
            {
                SceneObjectKeyframe kf = orig[k].Clone();
                if (dragged.Contains(k))
                {
                    kf.Time = TransformTime(kf.Time);
                    movedTimes.Add(kf.Time);
                }
                track.Keyframes.Add(kf);
            }
            track.SortKeyframes();
            movedObjectTimes[track] = movedTimes;
        }

        for (int i = 0; i < _dragStringSnapshots.Count; i++)
        {
            (SceneStringTrack track, SceneStringKeyframe[] orig, HashSet<int> dragged) = _dragStringSnapshots[i];
            if (track.Locked) continue;

            var movedTimes = new List<float>(dragged.Count);
            track.Keyframes.Clear();
            for (int k = 0; k < orig.Length; k++)
            {
                SceneStringKeyframe kf = orig[k].Clone();
                if (dragged.Contains(k))
                {
                    kf.Time = TransformTime(kf.Time);
                    movedTimes.Add(kf.Time);
                }
                track.Keyframes.Add(kf);
            }
            track.SortKeyframes();
            movedStringTimes[track] = movedTimes;
        }

        RebuildSelectionFromTrackTimes(movedCurveTimes, movedObjectTimes, movedStringTimes);
        _clip!.Invalidate();
        MarkClipDirty();
        RefreshLiveSample();
    }

    private void OnDragDopeSheet(Paper paper, DragEvent e, Func<float, float> timeFromX, Func<float, float> xFromTime)
    {
        if (_dragPart == DragPart.Keyframe && IsClipValid && _dragRow >= 0 && _dragRow < _rows.Count)
        {
            float fps = MathF.Max(1f, _clip!.FrameRate);
            float rawTargetT = timeFromX(e.RelativePosition.X);
            float targetT = IsAltDown(paper) ? rawTargetT : MathF.Round(rawTargetT * fps) / fps;
            float dt = targetT - _dragStartTime;
            ApplyMultiDrag(dt, 0f);
        }
        else if ((_dragPart == DragPart.StretchLeft || _dragPart == DragPart.StretchRight) && IsClipValid && MathF.Abs(_stretchOrigSpan) > 1e-4f)
        {
            float curT = MathF.Max(0f, timeFromX(e.RelativePosition.X));
            float newSpan = curT - _stretchAnchorTime;
            float scale = Math.Clamp(newSpan / _stretchOrigSpan, 0.05f, 20f);
            ApplyMultiTimeScale(_stretchAnchorTime, scale, snapToFrame: !IsAltDown(paper));
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
            float cy = i * TrackRowHeight - _trackScrollY + TrackRowHeight * 0.5f;
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
        int rowIdx = (int)MathF.Floor((localPos.Y + _trackScrollY) / TrackRowHeight);
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
            _selectedTrackRows.Clear();
            _selectedTrackRows.Add(rowIdx);
            _curveBoundsInitialized = false;
            if (keyIdx >= 0)
            {
                _selectedKeyIndex = keyIdx;
                _selectedKeys.Add((rowIdx, keyIdx));
            }
        }

        TrackRow? row = rowIdx >= 0 && rowIdx < _rows.Count ? _rows[rowIdx] : null;
        float fps = IsClipValid ? MathF.Max(1f, _clip!.FrameRate) : 60f;
        float clickTime = MathF.Max(0f, MathF.Round(timeFromX(localPos.X) * fps) / fps);
        bool hasClipboard = _copiedCurveKeys.Count > 0 || _copiedObjectKeys.Count > 0 || _copiedStringKeys.Count > 0;
        bool hasSelection = _selectedKeys.Count > 0 || (_selectedRowIndex >= 0 && _selectedKeyIndex >= 0);

        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
        {
            if (row != null && keyIdx >= 0)
                menu.Header(_selectedKeys.Count > 1 ? $"{_selectedKeys.Count} Keyframes Selected" : $"{row.Label} @ {GetKeyTime(row, keyIdx):0.00}s");
            else if (row != null)
                menu.Header($"{row.Label} @ {clickTime:0.00}s");
            else
                menu.Header($"Dope Sheet @ {clickTime:0.00}s");

            if (row != null && keyIdx < 0)
            {
                menu.Item("Add Keyframe Here", () => AddKeyToRowAt(row, clickTime),
                    icon: EditorIcons.Diamond, enabled: !IsRowLocked(row), shortcut: "Ctrl+LMB");
            }

            if (row != null && keyIdx >= 0 && !IsRowLocked(row))
            {
                if (row.Kind == TrackRowKind.Object)
                    menu.Item("Assign Object Reference...", () => OpenObjectKeyPicker(row, GetKeyTime(row, keyIdx)), icon: EditorIcons.Cube);

                if (row.Kind is TrackRowKind.Curve or TrackRowKind.Group)
                {
                    CurveInterpolation? curInterp = row.CurveTrack != null && keyIdx < row.CurveTrack.Curve.Count
                        ? row.CurveTrack.Curve[keyIdx].Interpolation
                        : null;

                    menu.Submenu("Interpolation", sub =>
                    {
                        sub.Item("Cubic Smooth", () => ModifySelectedCurveKeys("Set Cubic Interpolation",
                            k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, CurveInterpolation.CubicSpline), CurveTangentMode.Auto),
                            on: curInterp == CurveInterpolation.CubicSpline);
                        sub.Item("Linear", () => ModifySelectedCurveKeys("Set Linear Interpolation",
                            k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, CurveInterpolation.Linear), CurveTangentMode.Linear),
                            on: curInterp == CurveInterpolation.Linear);
                        sub.Item("Step (Constant)", () => ModifySelectedCurveKeys("Set Step Interpolation",
                            k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, CurveInterpolation.Step)),
                            on: curInterp == CurveInterpolation.Step);
                    });

                    menu.Submenu("Tangents", sub =>
                    {
                        sub.Item("Auto Smooth", () => ModifySelectedCurveKeys("Set Auto Tangents",
                            k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, CurveInterpolation.CubicSpline), CurveTangentMode.Auto));
                        sub.Item("Linear", () => ModifySelectedCurveKeys("Set Linear Tangents",
                            k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, CurveInterpolation.Linear), CurveTangentMode.Linear));
                        sub.Item("Flat", () => ModifySelectedCurveKeys("Set Flat Tangents",
                            k => new Keyframe(k.Time, k.Value, 0f, 0f, CurveInterpolation.CubicSpline)));
                    });
                }

                menu.Separator();
                menu.Item("Copy", CopySelectedKeys, icon: EditorIcons.Copy, shortcut: ShortcutKeys(ShortcutCopy));
                menu.Item("Duplicate", DuplicateSelectedKeys, icon: EditorIcons.Clone, shortcut: ShortcutKeys(ShortcutDuplicate));
                if (hasClipboard)
                    menu.Item("Paste Here", () => PasteCopiedKeysAt(clickTime), icon: EditorIcons.Paste, shortcut: ShortcutKeys(ShortcutPaste));

                menu.Separator();
                string delLabel = _selectedKeys.Count > 1
                    ? $"Delete {_selectedKeys.Count} Keyframes"
                    : (row.Kind == TrackRowKind.Group ? "Delete Group Keyframes" : "Delete Keyframe");
                menu.Item(delLabel, DeleteSelectedKeysOrEvent, icon: EditorIcons.Trash, danger: true, shortcut: ShortcutKeys(ShortcutDelete));
            }
            else if (hasClipboard)
            {
                menu.Item("Paste Keyframes Here", () => PasteCopiedKeysAt(clickTime), icon: EditorIcons.Paste, shortcut: ShortcutKeys(ShortcutPaste));
            }

            menu.Separator();
            menu.Item("Move Playhead Here", () =>
            {
                _playing = false;
                SetPlayhead(clickTime);
            }, shortcut: "MMB");
            menu.Item("Select All Keys", SelectAllKeys, shortcut: ShortcutKeys(ShortcutSelectAll));
            menu.Item(hasSelection ? "Frame Selected Keys" : "Frame All", FrameSelectedOrAll, shortcut: ShortcutKeys(ShortcutFrameSelection));
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
        MarkClipDirty();
        RefreshLiveSample();
    }

    private void PaintDopeSheet(Canvas canvas, Rect rect)
    {
        float x = (float)rect.Min.X, y = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        float range = MathF.Max(0.05f, _viewMaxTime - _viewMinTime);

        canvas.SaveState();

        // 1. Alternating row stripes (synchronized with Left Property List scroll)
        for (int i = 0; i < _rows.Count; i++)
        {
            float ry = y + i * TrackRowHeight - _trackScrollY;
            if (ry + TrackRowHeight < y) continue;
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

        // 3. Keyframe diamonds per row (synchronized with Left Property List scroll)
        for (int i = 0; i < _rows.Count; i++)
        {
            float cy = y + i * TrackRowHeight - _trackScrollY + TrackRowHeight * 0.5f;
            if (cy < y - 10f) continue;
            if (cy > y + h + 10f) break;

            TrackRow row = _rows[i];

            if (row.Kind == TrackRowKind.Group && row.SummaryTimes != null)
            {
                for (int k = 0; k < row.SummaryTimes.Count; k++)
                {
                    float kx = x + (row.SummaryTimes[k] - _viewMinTime) / range * w;
                    if (kx < x - 8f || kx > x + w + 8f) continue;
                    bool keySel = IsKeySelected(_selectedKeys, i, k, _selectedRowIndex, _selectedKeyIndex);
                    PaintDiamond(canvas, kx, cy, keySel ? 6f : 5f, keySel ? EditorTheme.Amber400 : EditorTheme.Ink400, keySel);
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
                    PaintDiamond(canvas, kx, cy, keySel ? 5.5f : 4.5f, keySel ? EditorTheme.Amber400 : row.AccentColor, keySel);
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
                    PaintDiamond(canvas, kx, cy, keySel ? 5.5f : 4.5f, keySel ? EditorTheme.Amber400 : row.AccentColor, keySel);
                }
            }
        }

        // 4. Unity-style Multi-Key Time-Stretching Bars when 2+ keys with a time span are selected
        if (_selectedKeys.Count > 1 && TryGetSelectedKeyTimeBounds(out float selMinT, out float selMaxT) && selMaxT - selMinT > 0.001f)
        {
            float sx0 = x + (selMinT - _viewMinTime) / range * w;
            float sx1 = x + (selMaxT - _viewMinTime) / range * w;
            float clamped0 = Math.Clamp(sx0, x, x + w);
            float clamped1 = Math.Clamp(sx1, x, x + w);
            if (clamped1 > clamped0)
                canvas.RectFilled(clamped0, y, clamped1 - clamped0, h, EditorTheme.ToColor32(EditorTheme.Blue400, 0.08f));
            if (sx0 >= x - 4f && sx0 <= x + w + 4f)
            {
                canvas.RectFilled(sx0 - 1.5f, y, 3f, h, EditorTheme.ToColor32(EditorTheme.Blue400, 0.85f));
                canvas.RectFilled(sx0 - 4f, y, 8f, 6f, EditorTheme.ToColor32(EditorTheme.Blue400, 1f));
            }
            if (sx1 >= x - 4f && sx1 <= x + w + 4f)
            {
                canvas.RectFilled(sx1 - 1.5f, y, 3f, h, EditorTheme.ToColor32(EditorTheme.Blue400, 0.85f));
                canvas.RectFilled(sx1 - 4f, y, 8f, 6f, EditorTheme.ToColor32(EditorTheme.Blue400, 1f));
            }
        }

        // 5. Playhead line
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

    internal List<VisibleCurveChannel> GetVisibleCurves()
    {
        var result = new List<VisibleCurveChannel>();
        if (!IsClipValid) return result;

        var seenTracks = new HashSet<SceneCurveTrack>(ReferenceEqualityComparer.Instance);
        var expandedRowMap = new Dictionary<SceneCurveTrack, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Kind == TrackRowKind.Curve && _rows[i].CurveTrack != null)
                expandedRowMap[_rows[i].CurveTrack!] = i;
        }

        void AddTrack(SceneCurveTrack track, int fallbackColorIdx)
        {
            if (!seenTracks.Add(track)) return;
            if (expandedRowMap.TryGetValue(track, out int rowIdx))
            {
                TrackRow r = _rows[rowIdx];
                result.Add(new VisibleCurveChannel(track, r.Label, r.AccentColor, rowIdx, IsRowLocked(r)));
            }
            else
            {
                result.Add(new VisibleCurveChannel(track, track.PropertyPath, ChannelColor(track.PropertyPath, fallbackColorIdx), -1, track.Locked));
            }
        }

        bool anySelectedRow = _selectedTrackRows.Count > 0 || (_selectedRowIndex >= 0 && _selectedRowIndex < _rows.Count);
        if (!anySelectedRow)
        {
            for (int i = 0; i < _clip!.CurveTracks.Count; i++)
                AddTrack(_clip.CurveTracks[i], i);
            return result;
        }

        IEnumerable<int> activeRows = _selectedTrackRows.Count > 0
            ? _selectedTrackRows.OrderBy(static x => x)
            : [_selectedRowIndex];

        foreach (int rIdx in activeRows)
        {
            if (rIdx < 0 || rIdx >= _rows.Count) continue;
            TrackRow sel = _rows[rIdx];
            if (sel.Kind == TrackRowKind.Curve && sel.CurveTrack != null)
            {
                AddTrack(sel.CurveTrack, rIdx);
            }
            else if (sel.Kind == TrackRowKind.Group && sel.GroupTracks != null)
            {
                for (int g = 0; g < sel.GroupTracks.Count; g++)
                    AddTrack(sel.GroupTracks[g], g);
            }
        }

        return result;
    }

    private bool IsCurveChannelKeySelected(VisibleCurveChannel ch, int keyIndex)
    {
        if (ch.RowIndex >= 0 && IsKeySelected(_selectedKeys, ch.RowIndex, keyIndex,
            _selectedRowIndex == ch.RowIndex ? _selectedRowIndex : -1, _selectedKeyIndex))
        {
            return true;
        }
        return _selectedDirectCurveKeys.TryGetValue(ch.Track, out HashSet<int>? set) && set.Contains(keyIndex);
    }

    private int TotalSelectedKeyCount()
    {
        int total = _selectedKeys.Count;
        foreach (HashSet<int> s in _selectedDirectCurveKeys.Values)
            total += s.Count;
        return total;
    }

    private bool TryGetSingleSelectedCurveKey(out SceneCurveTrack? track, out int keyIndex)
    {
        track = null;
        keyIndex = -1;
        if (TotalSelectedKeyCount() != 1) return false;

        if (_selectedKeys.Count == 1)
        {
            (int rIdx, int kIdx) = _selectedKeys.First();
            if (rIdx >= 0 && rIdx < _rows.Count && _rows[rIdx].Kind == TrackRowKind.Curve && _rows[rIdx].CurveTrack != null
                && kIdx >= 0 && kIdx < _rows[rIdx].CurveTrack!.Curve.Count)
            {
                track = _rows[rIdx].CurveTrack;
                keyIndex = kIdx;
                return true;
            }
            return false;
        }

        foreach ((SceneCurveTrack ct, HashSet<int> set) in _selectedDirectCurveKeys)
        {
            if (set.Count == 1)
            {
                int kIdx = set.First();
                if (kIdx >= 0 && kIdx < ct.Curve.Count)
                {
                    track = ct;
                    keyIndex = kIdx;
                    return true;
                }
            }
        }
        return false;
    }

    private void EnsureCurveBounds()
    {
        if (_curveBoundsInitialized) return;
        _curveBoundsInitialized = true;

        float minV = float.MaxValue, maxV = float.MinValue;
        bool any = false;

        List<VisibleCurveChannel> visible = GetVisibleCurves();
        for (int i = 0; i < visible.Count; i++)
        {
            SceneCurveTrack ct = visible[i].Track;
            if (ct.Curve.Count == 0) continue;

            CurveRenderer.GetBounds(ct.Curve, out _, out _, out float cMinV, out float cMaxV);
            minV = MathF.Min(minV, cMinV);
            maxV = MathF.Max(maxV, cMaxV);
            any = true;
        }

        if (!any)
        {
            _curveMinVal = -1f;
            _curveMaxVal = 2f;
        }
        else if (maxV - minV < 1e-3f)
        {
            float center = (minV + maxV) * 0.5f;
            _curveMinVal = center - 1.5f;
            _curveMaxVal = center + 1.5f;
        }
        else
        {
            float pad = MathF.Max(0.2f, (maxV - minV) * 0.15f);
            _curveMinVal = minV - pad;
            _curveMaxVal = maxV + pad;
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
            .OnDragEnd(_ =>
            {
                _dragPart = DragPart.None;
                _dragTangentTrack = null;
            })
            .OnDoubleClick(e =>
            {
                if (!IsClipValid) return;
                if (TryPickCurveForAdd(e.RelativePosition, timeFromX, LocalYFromValue, out VisibleCurveChannel targetCh, out bool nearCurve))
                {
                    Undo.RecordObject(ClipAsset!, "Add Curve Keyframe");
                    float t = timeFromX(e.RelativePosition.X);
                    float v = nearCurve ? targetCh.Track.Curve.Evaluate(t) : ValueFromLocalY(e.RelativePosition.Y);
                    _clip!.SetCurveKey(targetCh.Track.RelativePath, targetCh.Track.ComponentType, targetCh.Track.PropertyPath, t, v);
                    MarkClipDirty();
                    RefreshLiveSample();
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

    private bool TryPickCurveForAdd(Float2 localPos, Func<float, float> timeFromX, Func<float, float> yFromVal,
        out VisibleCurveChannel picked, out bool nearCurveLine)
    {
        picked = default;
        nearCurveLine = false;
        List<VisibleCurveChannel> visible = GetVisibleCurves();
        if (visible.Count == 0) return false;

        float t = timeFromX(localPos.X);
        float bestDist = float.MaxValue;
        int bestIdx = -1;

        for (int i = 0; i < visible.Count; i++)
        {
            if (visible[i].IsLocked) continue;
            float cy = yFromVal(visible[i].Track.Curve.Evaluate(t));
            float dist = MathF.Abs(localPos.Y - cy);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }

        if (bestIdx < 0) return false;
        picked = visible[bestIdx];
        nearCurveLine = visible.Count > 1 && bestDist <= 14f;
        return true;
    }

    private void OnPressCurves(Paper paper, Float2 localPos, Func<float, float> timeFromX, Func<float, float> xFromTime,
        Func<float, float> valFromY, Func<float, float> yFromVal)
    {
        bool shift = IsShiftDown(paper);
        bool ctrl = IsCtrlDown(paper);
        _dragPressLocalPos = localPos;
        _selectedEventIndex = -1;

        float rangeT = MathF.Max(0.05f, _viewMaxTime - _viewMinTime);
        float rangeV = MathF.Max(0.01f, _curveMaxVal - _curveMinVal);
        float tToX = MathF.Max(1f, _timelineWidth) / rangeT;
        float vToY = MathF.Max(1f, _timelineHeight) / rangeV;

        List<VisibleCurveChannel> visible = GetVisibleCurves();

        // 1. Hit-test Bezier In/Out tangent handles of selected curve keyframes first
        for (int i = 0; i < visible.Count; i++)
        {
            VisibleCurveChannel ch = visible[i];
            if (ch.IsLocked) continue;
            SceneCurveTrack ct = ch.Track;

            for (int k = 0; k < ct.Curve.Count; k++)
            {
                if (!IsCurveChannelKeySelected(ch, k)) continue;
                Keyframe kf = ct.Curve[k];
                if (kf.Interpolation != CurveInterpolation.CubicSpline) continue;

                float kx = xFromTime(kf.Time);
                float ky = yFromVal(kf.Value);

                if (k > 0)
                {
                    (float hx, float hy) = CurveRenderer.GetTangentScreenPos(kx, ky, kf.InTangent, -1f, tToX, vToY);
                    if (MathF.Abs(localPos.X - hx) <= 8f && MathF.Abs(localPos.Y - hy) <= 8f)
                    {
                        _dragPart = DragPart.TangentIn;
                        _dragTangentTrack = ct;
                        _dragKey = k;
                        if (IsClipValid) Undo.RecordObject(ClipAsset!, "Edit Curve Tangent");
                        return;
                    }
                }
                if (k < ct.Curve.Count - 1)
                {
                    (float hx, float hy) = CurveRenderer.GetTangentScreenPos(kx, ky, kf.OutTangent, 1f, tToX, vToY);
                    if (MathF.Abs(localPos.X - hx) <= 8f && MathF.Abs(localPos.Y - hy) <= 8f)
                    {
                        _dragPart = DragPart.TangentOut;
                        _dragTangentTrack = ct;
                        _dragKey = k;
                        if (IsClipValid) Undo.RecordObject(ClipAsset!, "Edit Curve Tangent");
                        return;
                    }
                }
            }
        }

        // 2. Hit-test curve keyframes (preserving any Group / multi-row selection in the Left Track List!)
        for (int i = visible.Count - 1; i >= 0; i--)
        {
            VisibleCurveChannel ch = visible[i];
            if (ch.IsLocked) continue;
            SceneCurveTrack ct = ch.Track;

            for (int k = 0; k < ct.Curve.Count; k++)
            {
                Keyframe kf = ct.Curve[k];
                float kx = xFromTime(kf.Time);
                float ky = yFromVal(kf.Value);
                if (MathF.Abs(localPos.X - kx) <= 8f && MathF.Abs(localPos.Y - ky) <= 8f)
                {
                    bool alreadySel = IsCurveChannelKeySelected(ch, k);
                    if (ctrl)
                    {
                        if (ch.RowIndex >= 0)
                        {
                            if (!_selectedKeys.Remove((ch.RowIndex, k)))
                                _selectedKeys.Add((ch.RowIndex, k));
                        }
                        else
                        {
                            if (!_selectedDirectCurveKeys.TryGetValue(ct, out HashSet<int>? set))
                                _selectedDirectCurveKeys[ct] = set = [];
                            if (!set.Remove(k))
                                set.Add(k);
                        }
                    }
                    else if (shift)
                    {
                        if (ch.RowIndex >= 0)
                            _selectedKeys.Add((ch.RowIndex, k));
                        else
                        {
                            if (!_selectedDirectCurveKeys.TryGetValue(ct, out HashSet<int>? set))
                                _selectedDirectCurveKeys[ct] = set = [];
                            set.Add(k);
                        }
                    }
                    else if (!alreadySel)
                    {
                        _selectedKeys.Clear();
                        _selectedDirectCurveKeys.Clear();
                        if (ch.RowIndex >= 0)
                            _selectedKeys.Add((ch.RowIndex, k));
                        else
                            _selectedDirectCurveKeys[ct] = [k];
                    }

                    // Only set _selectedRowIndex if no track row was selected yet — NEVER overwrite a Group/multi-row selection!
                    if (_selectedRowIndex < 0 && ch.RowIndex >= 0)
                        _selectedRowIndex = ch.RowIndex;
                    if (_selectedRowIndex == ch.RowIndex)
                        _selectedKeyIndex = k;

                    _dragPart = DragPart.Keyframe;
                    _dragRow = ch.RowIndex;
                    _dragKey = k;
                    _dragStartTime = kf.Time;
                    _dragStartValue = kf.Value;
                    CaptureMultiDragSnapshot();
                    if (IsClipValid) Undo.RecordObject(ClipAsset!, "Edit Curve Keyframes");
                    return;
                }
            }
        }

        // Unreal Ctrl + LMB Click on Curve Canvas: add keyframe at cursor (on closest curve!)
        if (ctrl && IsClipValid && TryPickCurveForAdd(localPos, timeFromX, yFromVal, out VisibleCurveChannel addCh, out bool nearLine))
        {
            Undo.RecordObject(ClipAsset!, "Add Curve Keyframe");
            float t = timeFromX(localPos.X);
            float v = nearLine ? addCh.Track.Curve.Evaluate(t) : valFromY(localPos.Y);
            _clip!.SetCurveKey(addCh.Track.RelativePath, addCh.Track.ComponentType, addCh.Track.PropertyPath, t, v);
            MarkClipDirty();
            RefreshLiveSample();
            return;
        }

        // LMB Drag on empty space -> Unreal Marquee Box Selection
        if (!shift && !ctrl)
        {
            _selectedKeys.Clear();
            _selectedDirectCurveKeys.Clear();
            _selectedKeyIndex = -1;
        }
        _dragPart = DragPart.MarqueeSelect;
        _marqueeStartLocal = localPos;
        _marqueeEndLocal = localPos;
    }

    private void OnDragCurves(Paper paper, DragEvent e, Func<float, float> timeFromX, Func<float, float> xFromTime,
        Func<float, float> valFromY, Func<float, float> yFromVal)
    {
        if ((_dragPart == DragPart.TangentIn || _dragPart == DragPart.TangentOut) && IsClipValid && _dragTangentTrack != null)
        {
            SceneCurveTrack ct = _dragTangentTrack;
            if (ct.Locked || _dragKey < 0 || _dragKey >= ct.Curve.Count) return;

            Keyframe kf = ct.Curve[_dragKey];
            float mouseT = timeFromX(e.RelativePosition.X);
            float mouseV = valFromY(e.RelativePosition.Y);
            float dt = mouseT - kf.Time;
            float dv = mouseV - kf.Value;

            // Keep tangent handle on its proper side (In = left, Out = right)
            if (_dragPart == DragPart.TangentIn) dt = MathF.Min(-1e-4f, dt);
            else dt = MathF.Max(1e-4f, dt);

            float newSlope = Math.Clamp(dv / dt, -10000f, 10000f);
            bool broken = IsAltDown(paper); // Hold Alt to break tangent independently; default is Unified Smooth
            float newIn = (_dragPart == DragPart.TangentIn || !broken) ? newSlope : kf.InTangent;
            float newOut = (_dragPart == DragPart.TangentOut || !broken) ? newSlope : kf.OutTangent;

            ct.Curve.MoveKey(_dragKey, new Keyframe(kf.Time, kf.Value, newIn, newOut, CurveInterpolation.CubicSpline));
            _clip!.Invalidate();
            MarkClipDirty();
            RefreshLiveSample();
            return;
        }

        if (_dragPart == DragPart.Keyframe && IsClipValid && _dragCurveSnapshots.Count > 0)
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

            ApplyMultiDrag(dt, dv);
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
        {
            _selectedKeys.Clear();
            _selectedDirectCurveKeys.Clear();
            _selectedKeyIndex = -1;
        }

        List<VisibleCurveChannel> visible = GetVisibleCurves();
        for (int i = 0; i < visible.Count; i++)
        {
            VisibleCurveChannel ch = visible[i];
            if (ch.IsLocked) continue;
            SceneCurveTrack ct = ch.Track;

            for (int k = 0; k < ct.Curve.Count; k++)
            {
                Keyframe kf = ct.Curve[k];
                float kx = xFromTime(kf.Time);
                float ky = yFromVal(kf.Value);
                if (kx >= minX && kx <= maxX && ky >= minY && ky <= maxY)
                {
                    if (ch.RowIndex >= 0)
                        _selectedKeys.Add((ch.RowIndex, k));
                    else
                    {
                        if (!_selectedDirectCurveKeys.TryGetValue(ct, out HashSet<int>? set))
                            _selectedDirectCurveKeys[ct] = set = [];
                        set.Add(k);
                    }
                    // Never overwrite a Group or multi-row selection in the Left Track List during curve marquee select!
                    if (_selectedRowIndex < 0 && ch.RowIndex >= 0)
                        _selectedRowIndex = ch.RowIndex;
                    if (_selectedRowIndex == ch.RowIndex)
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
        float LocalYFromValue(float v) => (1f - (v - _curveMinVal) / rangeV) * MathF.Max(1f, _timelineHeight);

        List<VisibleCurveChannel> visible = GetVisibleCurves();
        bool hasPick = TryPickCurveForAdd(localPos, timeFromX, LocalYFromValue, out VisibleCurveChannel pickedCh, out bool nearCurve);
        int selCount = TotalSelectedKeyCount();
        bool hasSelection = selCount > 0 || (_selectedRowIndex >= 0 && _selectedKeyIndex >= 0);
        bool hasClipboard = _copiedCurveKeys.Count > 0 || _copiedObjectKeys.Count > 0 || _copiedStringKeys.Count > 0;

        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
        {
            menu.Header(selCount > 1 ? $"{selCount} Keyframes Selected" : $"Curves @ {clickTime:0.00}s");

            if (hasPick)
            {
                menu.Item($"Add Keyframe on {pickedCh.Label}", () =>
                {
                    Undo.RecordObject(ClipAsset!, "Add Curve Keyframe");
                    float v = nearCurve ? pickedCh.Track.Curve.Evaluate(clickTime) : clickVal;
                    _clip!.SetCurveKey(pickedCh.Track.RelativePath, pickedCh.Track.ComponentType, pickedCh.Track.PropertyPath, clickTime, v);
                    MarkClipDirty();
                    RefreshLiveSample();
                }, icon: EditorIcons.Diamond, shortcut: "Ctrl+LMB");
            }

            if (visible.Count > 1)
            {
                menu.Item("Add Keyframe on All Visible Curves", () =>
                {
                    Undo.RecordObject(ClipAsset!, "Add Group Curve Keyframes");
                    foreach (VisibleCurveChannel ch in visible)
                    {
                        if (ch.IsLocked) continue;
                        float v = ch.Track.Curve.Evaluate(clickTime);
                        _clip!.SetCurveKey(ch.Track.RelativePath, ch.Track.ComponentType, ch.Track.PropertyPath, clickTime, v);
                    }
                    MarkClipDirty();
                    RefreshLiveSample();
                }, icon: EditorIcons.Diamond);
            }

            if (hasSelection)
            {
                menu.Submenu("Interpolation", sub =>
                {
                    sub.Item("Cubic Smooth", () => ModifySelectedCurveKeys("Set Cubic Interpolation",
                        k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, CurveInterpolation.CubicSpline), CurveTangentMode.Auto));
                    sub.Item("Linear", () => ModifySelectedCurveKeys("Set Linear Interpolation",
                        k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, CurveInterpolation.Linear), CurveTangentMode.Linear));
                    sub.Item("Step (Constant)", () => ModifySelectedCurveKeys("Set Step Interpolation",
                        k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, CurveInterpolation.Step)));
                });

                menu.Submenu("Tangents", sub =>
                {
                    sub.Item("Auto Smooth", () => ModifySelectedCurveKeys("Set Auto Tangents",
                        k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, CurveInterpolation.CubicSpline), CurveTangentMode.Auto));
                    sub.Item("Linear", () => ModifySelectedCurveKeys("Set Linear Tangents",
                        k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, CurveInterpolation.Linear), CurveTangentMode.Linear));
                    sub.Item("Flat", () => ModifySelectedCurveKeys("Set Flat Tangents",
                        k => new Keyframe(k.Time, k.Value, 0f, 0f, CurveInterpolation.CubicSpline)));
                });

                menu.Separator();
                menu.Item("Copy", CopySelectedKeys, icon: EditorIcons.Copy, shortcut: ShortcutKeys(ShortcutCopy));
                menu.Item("Duplicate", DuplicateSelectedKeys, icon: EditorIcons.Clone, shortcut: ShortcutKeys(ShortcutDuplicate));
                if (hasClipboard)
                    menu.Item("Paste Here", () => PasteCopiedKeysAt(clickTime), icon: EditorIcons.Paste, shortcut: ShortcutKeys(ShortcutPaste));

                menu.Separator();
                menu.Item(selCount > 1 ? $"Delete {selCount} Keyframes" : "Delete Keyframe",
                    DeleteSelectedKeysOrEvent, icon: EditorIcons.Trash, danger: true, shortcut: ShortcutKeys(ShortcutDelete));
            }
            else if (hasClipboard)
            {
                menu.Item("Paste Keyframes Here", () => PasteCopiedKeysAt(clickTime), icon: EditorIcons.Paste, shortcut: ShortcutKeys(ShortcutPaste));
            }

            menu.Separator();
            menu.Item("Move Playhead Here", () =>
            {
                _playing = false;
                SetPlayhead(clickTime);
            }, shortcut: "MMB");
            menu.Item("Select All Keys", SelectAllKeys, shortcut: ShortcutKeys(ShortcutSelectAll));
            menu.Item(hasSelection ? "Frame Selected Keys" : "Frame All", FrameSelectedOrAll, shortcut: ShortcutKeys(ShortcutFrameSelection));
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

        // Draw all visible curves (supports single curve row, collapsed/expanded group row with X/Y/Z, or multi-selected rows)
        List<VisibleCurveChannel> visible = GetVisibleCurves();
        const int Steps = 140;
        for (int i = 0; i < visible.Count; i++)
        {
            VisibleCurveChannel ch = visible[i];
            SceneCurveTrack ct = ch.Track;
            if (ct.Curve.Count == 0) continue;

            AnimationCurve curve = ct.Curve;
            bool isPrimaryCurve = visible.Count == 1 || (ch.RowIndex >= 0 && ch.RowIndex == _selectedRowIndex);
            canvas.SetStrokeColor(EditorTheme.ToColor32(ch.Color, 1f));
            canvas.SetStrokeWidth(isPrimaryCurve ? 2f : 1.75f);
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

            // Keyframe dots + interactive Bezier tangent handles on selected keys
            float tToX = w / rangeT;
            float vToY = h / rangeV;
            for (int k = 0; k < curve.Count; k++)
            {
                Keyframe kf = curve[k];
                float kx = x + (kf.Time - _viewMinTime) / rangeT * w;
                float ky = y + h - (kf.Value - _curveMinVal) / rangeV * h;
                bool isSel = IsCurveChannelKeySelected(ch, k);

                if (isSel && kf.Interpolation == CurveInterpolation.CubicSpline)
                {
                    canvas.SetStrokeColor(EditorTheme.ToColor32(EditorTheme.Amber400, 0.85f));
                    canvas.SetStrokeWidth(1.25f);
                    if (k > 0)
                    {
                        (float hx, float hy) = CurveRenderer.GetTangentScreenPos(kx, ky, kf.InTangent, -1f, tToX, vToY);
                        canvas.BeginPath(); canvas.MoveTo(kx, ky); canvas.LineTo(hx, hy); canvas.Stroke();
                        canvas.CircleFilled(hx, hy, 3.5f, EditorTheme.ToColor32(EditorTheme.Amber400, 1f));
                    }
                    if (k < curve.Count - 1)
                    {
                        (float hx, float hy) = CurveRenderer.GetTangentScreenPos(kx, ky, kf.OutTangent, 1f, tToX, vToY);
                        canvas.BeginPath(); canvas.MoveTo(kx, ky); canvas.LineTo(hx, hy); canvas.Stroke();
                        canvas.CircleFilled(hx, hy, 3.5f, EditorTheme.ToColor32(EditorTheme.Amber400, 1f));
                    }
                }

                PaintDiamond(canvas, kx, ky, isSel ? 5.5f : 4.25f, isSel ? EditorTheme.Amber400 : ch.Color, isSel);
            }
        }

        // Top-right channel color legend when 1..8 curves are displayed (makes X/Y/Z or R/G/B/A immediately identifiable)
        if (visible.Count is >= 1 and <= 8)
        {
            float legendRight = x + w - 8f;
            float legendY = y + 6f;
            for (int i = visible.Count - 1; i >= 0; i--)
            {
                VisibleCurveChannel ch = visible[i];
                string shortLbl = ch.Label;
                float pillW = shortLbl.Length * 5.6f + 20f;
                float pillX = legendRight - pillW;
                if (pillX < x + 48f) break;

                canvas.RoundedRectFilled(pillX, legendY, pillW, 17f, 3f, 3f, 3f, 3f, Color32.FromArgb(170, 24, 26, 32));
                canvas.CircleFilled(pillX + 7f, legendY + 8.5f, 3.5f, EditorTheme.ToColor32(ch.Color, 1f));
                canvas.DrawText(shortLbl, pillX + 14f, legendY + 2f, Color32.FromArgb(225, 225, 230, 238), 9.5f, font);
                legendRight = pillX - 5f;
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
                        _clip.Loop = v is SceneClipWrapMode.Loop or SceneClipWrapMode.PingPong;
                        _clip.Invalidate();
                        MarkClipDirty();
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
                        MarkClipDirty();
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

                // Multi-Key / Single-Key inline editor (Selected Events use the spacious Event Setup Drawer above)
                if (_selectedEventIndex >= 0 && _selectedEventIndex < _clip.PropertyEvents.Count)
                {
                    SceneAnimationEvent selEv = _clip.PropertyEvents[_selectedEventIndex];
                    Color evAccent = selEv.IsRange ? EditorTheme.Purple400 : EditorTheme.Blue400;
                    paper.Box("anim_bot_ev_hint").Width(UnitValue.Auto).Height(20).Padding(6, 6, 0, 0)
                        .Rounded(4).BackgroundColor(EditorTheme.WithAlpha(evAccent, 35))
                        .Text($"Editing Event: {FormatEventBadgeText(selEv)}", font)
                        .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
                }
                else if (TotalSelectedKeyCount() > 1)
                {
                    DrawMultiSelectedKeysFooter(paper, font);
                }
                else if (TryGetSingleSelectedCurveKey(out SceneCurveTrack? singleTrack, out int singleKeyIdx) && singleTrack != null)
                {
                    DrawSelectedCurveTrackKeyFooter(paper, font, singleTrack, singleKeyIdx);
                }
                else if (_selectedRowIndex >= 0 && _selectedRowIndex < _rows.Count && _selectedKeyIndex >= 0)
                {
                    DrawSelectedKeyFooter(paper, font, _rows[_selectedRowIndex], _selectedKeyIndex);
                }
            }

            paper.Box("anim_bot_sp").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();
        }
    }

    // ================================================================
    //  Spacious Reflection-Driven Animation Event Setup Drawer
    // ================================================================

    private List<(string RelPath, string Label)> BuildEventTargetChoices(string currentPath)
    {
        var list = new List<(string RelPath, string Label)>();
        if (_targetRoot.IsNotValid()) return list;

        void Walk(GameObject root, GameObject cur, string prefix)
        {
            if (cur.IsNotValid()) return;
            string local = Transform.GetRelativePath(cur.Transform, root.Transform);
            string rel = string.IsNullOrEmpty(prefix) ? local : (string.IsNullOrEmpty(local) ? prefix : $"{prefix}/{local}");
            string display = string.IsNullOrEmpty(rel) ? $"(Root) {cur.Name}" : rel;
            list.Add((rel, display));
            for (int i = 0; i < cur.Children.Count; i++)
                Walk(root, cur.Children[i], prefix);
        }

        Walk(_targetRoot!, _targetRoot!, string.Empty);

        if (_animator.IsValid())
        {
            for (int i = 0; i < _animator!.ObjectBindings.Count; i++)
            {
                AnimatorObjectBinding b = _animator.ObjectBindings[i];
                if (!string.IsNullOrWhiteSpace(b.SlotName) && b.Target.IsValid())
                    Walk(b.Target!, b.Target!, $"@{b.SlotName}");
            }
        }

        if (!string.IsNullOrEmpty(currentPath) && list.FindIndex(x => x.RelPath == currentPath) < 0)
            list.Add((currentPath, $"{currentPath} (Missing!)"));

        return list;
    }

    private void PickEventTargetFromScene(SceneAnimationEvent ev)
    {
        if (!IsClipValid || _targetRoot.IsNotValid()) return;
        SelectorModal.Open("Select Event Target GameObject", typeof(GameObject), SelectorTabs.Scene, picked =>
        {
            if (!IsClipValid || _targetRoot.IsNotValid() || picked is not GameObject go || go.IsNotValid()) return;

            string newRelPath;
            if (GameObject.IsChildOrSameTransform(go, _targetRoot!))
            {
                newRelPath = Transform.GetRelativePath(go.Transform, _targetRoot!.Transform);
            }
            else if (_animator.IsValid())
            {
                string slot = go.Name;
                Undo.RecordObject(_animator!, "Bind External Event Target");
                _animator!.SetObjectBinding(slot, go);
                MarkAnimatorEdited(_animator);
                newRelPath = $"@{slot}";
            }
            else
            {
                return;
            }

            Undo.RecordObject(ClipAsset!, "Change Event Target");
            ev.RelativePath = newRelPath;
            _clip!.Invalidate();
            MarkClipDirty();
        });
    }

    private List<AnimatableEventMethodDescriptor> GetCachedCallableEventMethods(GameObject? target)
    {
        if (target.IsNotValid())
        {
            _cachedEventMethodsTarget = null;
            return [];
        }

        long now = Environment.TickCount64;
        if (!ReferenceEquals(_cachedEventMethodsTarget, target) || now - _cachedEventMethodsTick > 500)
        {
            _cachedEventMethods = SceneClipBinding.GetCallableEventMethods(target!);
            _cachedEventMethodsTarget = target;
            _cachedEventMethodsTick = now;
        }

        return _cachedEventMethods;
    }

    private void DrawSelectedEventInspector(Paper paper, Scribe.FontFile font, float width, float drawerHeight, float desiredHeight, SceneAnimationEvent ev)
    {
        if (!IsClipValid) return;

        bool isRanged = ev.IsRange;
        GameObject? resolvedTarget = _targetRoot.IsValid() ? SceneClipBinding.ResolveGameObject(_targetRoot!, ev.RelativePath) : null;
        List<AnimatableEventMethodDescriptor> methods = GetCachedCallableEventMethods(resolvedTarget);

        const float LabelColWidth = 64f;
        const float MethodDropdownWidth = 240f;
        const float MethodTextWidth = 165f;
        const float RowHeight = 24f;
        const float ControlHeight = 22f;
        const float HeaderHeight = 28f;

        string resolvedSig = string.Empty;
        string resolvedParamName = "Arg";
        Type? expectedParamType = null;
        bool hasEnter = !string.IsNullOrEmpty(ev.FunctionName);
        bool enterValid = _targetRoot.IsValid() && hasEnter &&
            SceneClipBinding.TryValidateEventMethod(_targetRoot!, ev.RelativePath, ev.FunctionName, ev.ArgType, out resolvedSig, out resolvedParamName, out expectedParamType);

        void EnableRangedMode()
        {
            if (ev.IsRange) return;
            Undo.RecordObject(ClipAsset!, "Set Ranged Event Mode");
            ev.EndTime = ev.Time + MathF.Max(0.25f, 1f / MathF.Max(1f, _clip!.FrameRate));
            _clip!.Invalidate();
            MarkClipDirty();
        }

        void DrawEventCallbackRow(
            string rowId,
            string label,
            string currentMethod,
            List<AnimatableEventMethodDescriptor> availableMethods,
            string emptyDropdownLabel,
            string placeholder,
            ProwlActionArgType validationArgType,
            Action<string, AnimatableEventMethodDescriptor?> onMethodChanged,
            Action onTest,
            Action? drawTrailingExtras = null)
        {
            bool hasMethod = !string.IsNullOrEmpty(currentMethod);
            bool hasTarget = _targetRoot.IsValid();
            string sig = string.Empty;
            bool isValid = hasTarget && hasMethod &&
                SceneClipBinding.TryValidateEventMethod(_targetRoot!, ev.RelativePath, currentMethod, validationArgType, out sig, out _, out _);

            using (paper.Row(rowId).Width(UnitValue.Stretch()).Height(RowHeight).Gap(8).AlignItems(LayoutAlignment.Center).Enter())
            {
                paper.Box($"{rowId}_lbl").Width(LabelColWidth).Height(RowHeight).IsNotInteractable()
                    .Text(label, font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                if (hasTarget)
                {
                    var options = new List<string>(availableMethods.Count + 1)
                    {
                        availableMethods.Count > 0 ? emptyDropdownLabel : "(No compatible methods)",
                    };
                    for (int i = 0; i < availableMethods.Count; i++)
                        options.Add(availableMethods[i].DisplayName);

                    int selectedIdx = 0;
                    if (hasMethod && availableMethods.Count > 0)
                    {
                        int found = availableMethods.FindIndex(m => m.MethodName == currentMethod && m.ArgType == validationArgType);
                        if (found < 0) found = availableMethods.FindIndex(m => m.MethodName == currentMethod);
                        if (found >= 0) selectedIdx = found + 1;
                    }

                    using (paper.Box($"{rowId}_dd_w").Width(MethodDropdownWidth).Height(ControlHeight).Enter())
                    {
                        Origami.Dropdown(paper, $"{rowId}_dd", selectedIdx, idx =>
                        {
                            if (idx <= 0 || idx > availableMethods.Count)
                                onMethodChanged(string.Empty, null);
                            else
                                onMethodChanged(availableMethods[idx - 1].MethodName, availableMethods[idx - 1]);
                        }, [.. options]).Placeholder(emptyDropdownLabel).Searchable().Show();
                    }
                }

                using (paper.Box($"{rowId}_txt_w").Width(hasTarget ? MethodTextWidth : 220f).Height(ControlHeight).Enter())
                {
                    Origami.TextField(paper, $"{rowId}_txt", currentMethod, v =>
                    {
                        string trimmed = v.Trim();
                        int match = availableMethods.FindIndex(m => m.MethodName == trimmed);
                        onMethodChanged(trimmed, match >= 0 ? availableMethods[match] : null);
                    }).Placeholder(placeholder).Height(ControlHeight).CommitOnEnter().Show();
                }

                if (hasTarget && hasMethod)
                {
                    Origami.Button(paper, $"{rowId}_test", $"{EditorIcons.Play} Test", onTest)
                        .Small().Height(ControlHeight).Show();
                }

                drawTrailingExtras?.Invoke();

                if (hasTarget && hasMethod)
                {
                    Color pillCol = isValid ? EditorTheme.Green400 : EditorTheme.Red400;
                    string pillText = isValid ? sig : "Method not found";
                    paper.Box($"{rowId}_st").Width(UnitValue.Auto).Height(20).Padding(6, 6, 0, 0)
                        .Rounded(4).BackgroundColor(EditorTheme.WithAlpha(pillCol, 32))
                        .Text(pillText, font).TextColor(pillCol)
                        .FontSize(EditorTheme.FontSizeSmall * 0.9f).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
                }
            }
        }

        using (paper.Column("anim_ev_drawer").Width(width).Height(drawerHeight)
            .BackgroundColor(EditorTheme.Neutral200).Enter())
        {
            // Header Row: Title + Prominent Instant/Ranged Mode Toggle + Target Status + Actions
            using (paper.Row("anim_ev_dr_hdr").Width(width).Height(HeaderHeight).Padding(10, 8, 0, 0).Gap(6)
                .BackgroundColor(EditorTheme.Neutral300).AlignItems(LayoutAlignment.Center).Enter())
            {
                paper.Box("anim_ev_dr_ic").Width(14).Height(HeaderHeight).IsNotInteractable()
                    .Text(isRanged ? EditorIcons.ArrowsLeftRight : EditorIcons.Bookmark, font)
                    .TextColor(isRanged ? EditorTheme.Purple400 : EditorTheme.Blue400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

                paper.Box("anim_ev_dr_title").Width(UnitValue.Auto).Height(HeaderHeight).IsNotInteractable()
                    .Text("Event", EditorTheme.FontSemiBold ?? font)
                    .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                EditorGUI.Chip(paper, "anim_ev_mode_inst", "Instant", !isRanged, () =>
                {
                    if (!isRanged) return;
                    Undo.RecordObject(ClipAsset!, "Set Instant Event Mode");
                    ev.EndTime = ev.Time;
                    _clip!.Invalidate();
                    MarkClipDirty();
                }, height: 20f);

                EditorGUI.Chip(paper, "anim_ev_mode_rng", "Ranged (Enter / Stay / Exit)", isRanged, EnableRangedMode, height: 20f);

                // Overall status hint (stretches & truncates cleanly so right-side buttons never clip)
                string statusText;
                Color statusCol;
                if (_targetRoot.IsNotValid())
                {
                    statusText = "Select scene GameObject to browse component methods";
                    statusCol = EditorTheme.Ink400;
                }
                else if (resolvedTarget.IsNotValid())
                {
                    statusText = $"Target '{ev.RelativePath}' not found";
                    statusCol = EditorTheme.Red400;
                }
                else if (!hasEnter && (!isRanged || (string.IsNullOrEmpty(ev.StayFunctionName) && string.IsNullOrEmpty(ev.ExitFunctionName))))
                {
                    statusText = "Select or type a component method below";
                    statusCol = EditorTheme.Amber400;
                }
                else
                {
                    statusText = $"Target: {resolvedTarget!.Name}";
                    statusCol = EditorTheme.Ink400;
                }

                paper.Box("anim_ev_dr_status").Width(UnitValue.Stretch()).Height(20).Padding(6, 6, 0, 0)
                    .Text(statusText, font).TextColor(statusCol)
                    .FontSize(EditorTheme.FontSizeSmall * 0.92f).Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();

                Origami.Button(paper, "anim_ev_snap_ph", $"@ Playhead ({_currentTime:0.00}s)", () =>
                {
                    Undo.RecordObject(ClipAsset!, "Snap Event to Playhead");
                    float span = ev.Duration;
                    ev.Time = _currentTime;
                    ev.EndTime = isRanged ? _currentTime + span : _currentTime;
                    _clip!.Invalidate();
                    MarkClipDirty();
                }).Small().Height(20).Show();

                Origami.Button(paper, "anim_ev_dup", "Duplicate", () => DuplicateEvent(_selectedEventIndex)).Small().Height(20).Show();

                Origami.Button(paper, "anim_ev_del", "Delete", () =>
                {
                    if (!IsClipValid || _selectedEventIndex < 0 || _selectedEventIndex >= _clip!.PropertyEvents.Count) return;
                    Undo.RecordObject(ClipAsset!, "Delete Animation Event");
                    _clip.PropertyEvents.RemoveAt(_selectedEventIndex);
                    _selectedEventIndex = -1;
                    _clip.Invalidate();
                    MarkClipDirty();
                }).Danger().Small().Height(20).Show();

                EditorGUI.ToolbarIconBtn(paper, "anim_ev_close", EditorIcons.Xmark, "Close Event Inspector", false, () => _selectedEventIndex = -1);
            }

            float bodyAreaHeight = MathF.Max(36f, drawerHeight - HeaderHeight);
            bool needsScroll = drawerHeight < desiredHeight - 1f;

            void DrawDrawerRows()
            {
                using (paper.Column("anim_ev_dr_body").Width(width - (needsScroll ? 12f : 0f)).Height(UnitValue.Auto)
                    .Padding(10, 10, 4, 4).Gap(4).Enter())
                {
                    // Row 1: Timing + Target GameObject Picker
                    using (paper.Row("anim_ev_row1").Width(UnitValue.Stretch()).Height(RowHeight).Gap(8).AlignItems(LayoutAlignment.Center).Enter())
                    {
                        float fps = MathF.Max(1f, _clip!.FrameRate);
                        int startFrame = (int)MathF.Round(ev.Time * fps);

                        paper.Box("anim_ev_t_lbl").Width(LabelColWidth).Height(RowHeight).IsNotInteractable()
                            .Text(isRanged ? "Start:" : "Time:", font).TextColor(EditorTheme.Ink400)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                        using (paper.Box("anim_ev_t0_w").Width(76).Height(ControlHeight).Enter())
                        {
                            Origami.NumericField<float>(paper, "anim_ev_t0", ev.Time, v =>
                            {
                                Undo.RecordObject(ClipAsset!, "Edit Event Start Time");
                                float span = MathF.Max(0.01f, ev.Duration);
                                ev.Time = MathF.Max(0f, v);
                                if (isRanged) ev.EndTime = ev.EndTime > ev.Time ? ev.EndTime : ev.Time + span;
                                else ev.EndTime = ev.Time;
                                _clip.Invalidate();
                                MarkClipDirty();
                            }).Format("0.###").Suffix("s").Height(ControlHeight).Show();
                        }

                        if (isRanged)
                        {
                            paper.Box("anim_ev_end_lbl").Width(30).Height(RowHeight).IsNotInteractable()
                                .Text("End:", font).TextColor(EditorTheme.Ink400)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                            using (paper.Box("anim_ev_t1_w").Width(76).Height(ControlHeight).Enter())
                            {
                                Origami.NumericField<float>(paper, "anim_ev_t1", ev.EndTime, v =>
                                {
                                    Undo.RecordObject(ClipAsset!, "Edit Event End Time");
                                    ev.EndTime = MathF.Max(ev.Time + 0.01f, v);
                                    _clip.Invalidate();
                                    MarkClipDirty();
                                }).Format("0.###").Suffix("s").Height(ControlHeight).Show();
                            }

                            paper.Box("anim_ev_dur_ro").Width(UnitValue.Auto).Height(RowHeight).IsNotInteractable()
                                .Text($"({ev.Duration:0.##}s)", EditorTheme.FontMono ?? font).TextColor(EditorTheme.Ink300)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                        }
                        else
                        {
                            paper.Box("anim_ev_fr_ro").Width(UnitValue.Auto).Height(RowHeight).IsNotInteractable()
                                .Text($"(F{startFrame})", EditorTheme.FontMono ?? font).TextColor(EditorTheme.Ink300)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                        }

                        paper.Box("anim_ev_r1_div").Width(1).Height(16).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

                        paper.Box("anim_ev_tgt_lbl").Width(46).Height(RowHeight).IsNotInteractable()
                            .Text("Target:", font).TextColor(EditorTheme.Ink400)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                        if (_targetRoot.IsValid())
                        {
                            List<(string RelPath, string Label)> targets = BuildEventTargetChoices(ev.RelativePath);
                            string[] targetLabels = [.. targets.Select(t => t.Label)];
                            int curTgtIdx = Math.Max(0, targets.FindIndex(t => t.RelPath == ev.RelativePath));

                            using (paper.Box("anim_ev_tgt_dd_w").Width(MethodDropdownWidth).Height(ControlHeight).Enter())
                            {
                                Origami.Dropdown(paper, "anim_ev_tgt_dd", curTgtIdx, idx =>
                                {
                                    if (idx < 0 || idx >= targets.Count) return;
                                    Undo.RecordObject(ClipAsset!, "Change Event Target");
                                    ev.RelativePath = targets[idx].RelPath;
                                    _clip.Invalidate();
                                    MarkClipDirty();
                                }, targetLabels).Searchable().Show();
                            }

                            Origami.Button(paper, "anim_ev_tgt_pick", "Pick", () => PickEventTargetFromScene(ev)).Small().Height(ControlHeight).Show();
                        }
                        else
                        {
                            using (paper.Box("anim_ev_path_w").Width(220).Height(ControlHeight).Enter())
                            {
                                Origami.TextField(paper, "anim_ev_path", ev.RelativePath, v =>
                                {
                                    Undo.RecordObject(ClipAsset!, "Edit Event Target Path");
                                    ev.RelativePath = v.Trim();
                                    MarkClipDirty();
                                }).Placeholder("(Root GameObject)").Height(ControlHeight).CommitOnEnter().Show();
                            }
                        }
                    }

                    // Row 2: Enter / Instant Callback (uniform row helper)
                    DrawEventCallbackRow(
                        rowId: "anim_ev_row2",
                        label: isRanged ? "Enter:" : "Function:",
                        currentMethod: ev.FunctionName,
                        availableMethods: methods,
                        emptyDropdownLabel: "(None)",
                        placeholder: "Method name...",
                        validationArgType: ev.ArgType,
                        onMethodChanged: (name, picked) =>
                        {
                            Undo.RecordObject(ClipAsset!, "Edit Event Function");
                            ev.FunctionName = name;
                            if (picked.HasValue)
                            {
                                ev.ArgType = picked.Value.ArgType;
                                if (picked.Value.ArgType == ProwlActionArgType.Object && picked.Value.ParameterType != null &&
                                    ev.ObjectParam.IsValid() && !picked.Value.ParameterType.IsInstanceOfType(ev.ObjectParam))
                                {
                                    ev.ObjectParam = null;
                                }
                            }
                            else if (string.IsNullOrEmpty(name))
                            {
                                ev.ArgType = ProwlActionArgType.None;
                            }
                            MarkClipDirty();
                        },
                        onTest: () => SceneClipBinding.InvokeEvent(_targetRoot!, ev),
                        drawTrailingExtras: () =>
                        {
                            if (methods.Count == 0 || (hasEnter && !enterValid))
                            {
                                using (paper.Box("anim_ev_argtype_w").Width(80).Height(ControlHeight).Enter())
                                {
                                    Origami.EnumDropdown(paper, "anim_ev_argtype", ev.ArgType, v =>
                                    {
                                        Undo.RecordObject(ClipAsset!, "Edit Event Arg Type");
                                        ev.ArgType = v;
                                        MarkClipDirty();
                                    }).Show();
                                }
                            }

                            if (ev.ArgType != ProwlActionArgType.None)
                            {
                                paper.Box("anim_ev_arg_lbl").Width(UnitValue.Auto).Height(RowHeight).IsNotInteractable()
                                    .Text($"{resolvedParamName}:", font).TextColor(EditorTheme.Ink400)
                                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                                DrawEventArgumentField(paper, ev, expectedParamType);
                            }

                            if (!isRanged)
                            {
                                paper.Box("anim_ev_r2_sp").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();
                                Origami.Button(paper, "anim_ev_add_exit_btn", $"{EditorIcons.ArrowsLeftRight} + Exit / Range", EnableRangedMode)
                                    .Small().Height(ControlHeight).Show();
                            }
                        });

                    if (isRanged)
                    {
                        // Row 3: Stay Callback (identical layout to Enter & Exit)
                        List<AnimatableEventMethodDescriptor> stayMethods = [.. methods.Where(m => m.ArgType is ProwlActionArgType.Float or ProwlActionArgType.None)];
                        DrawEventCallbackRow(
                            rowId: "anim_ev_row3",
                            label: "Stay:",
                            currentMethod: ev.StayFunctionName,
                            availableMethods: stayMethods,
                            emptyDropdownLabel: "(None)",
                            placeholder: "Method name...",
                            validationArgType: ProwlActionArgType.Float,
                            onMethodChanged: (name, _) =>
                            {
                                Undo.RecordObject(ClipAsset!, "Edit Event Stay Function");
                                ev.StayFunctionName = name;
                                MarkClipDirty();
                            },
                            onTest: () =>
                            {
                                var tmpStay = ev.Clone();
                                tmpStay.FunctionName = ev.StayFunctionName;
                                tmpStay.ArgType = ProwlActionArgType.Float;
                                tmpStay.FloatParam = 0.5f;
                                SceneClipBinding.InvokeEvent(_targetRoot!, tmpStay);
                            },
                            drawTrailingExtras: () =>
                            {
                                if (string.IsNullOrEmpty(ev.StayFunctionName))
                                {
                                    paper.Box("anim_ev_stay_hint").Width(UnitValue.Auto).Height(RowHeight).IsNotInteractable()
                                        .Text("(optional, float 0..1)", font).TextColor(EditorTheme.Ink300)
                                        .FontSize(EditorTheme.FontSizeSmall * 0.9f).Alignment(TextAlignment.MiddleLeft);
                                }
                            });

                        // Row 4: Exit Callback (identical layout to Enter & Stay)
                        List<AnimatableEventMethodDescriptor> exitMethods = [.. methods.Where(m => m.ArgType == ProwlActionArgType.None || m.ArgType == ev.ArgType)];
                        DrawEventCallbackRow(
                            rowId: "anim_ev_row4",
                            label: "Exit:",
                            currentMethod: ev.ExitFunctionName,
                            availableMethods: exitMethods,
                            emptyDropdownLabel: "(None)",
                            placeholder: "Method name...",
                            validationArgType: ev.ArgType,
                            onMethodChanged: (name, _) =>
                            {
                                Undo.RecordObject(ClipAsset!, "Edit Event Exit Function");
                                ev.ExitFunctionName = name;
                                MarkClipDirty();
                            },
                            onTest: () =>
                            {
                                var tmpExit = ev.Clone();
                                tmpExit.FunctionName = ev.ExitFunctionName;
                                SceneClipBinding.InvokeEvent(_targetRoot!, tmpExit);
                            });
                    }
                }
            }

            if (needsScroll)
                Origami.ScrollView(paper, "anim_ev_dr_scroll", width, bodyAreaHeight).Body(DrawDrawerRows);
            else
                DrawDrawerRows();
        }
    }

    private void DrawEventArgumentField(Paper paper, SceneAnimationEvent ev, Type? expectedObjectType)
    {
        switch (ev.ArgType)
        {
            case ProwlActionArgType.Float:
                using (paper.Box("anim_ev_f_w").Width(82).Height(22).Enter())
                    Origami.NumericField<float>(paper, "anim_ev_f", ev.FloatParam, v => { Undo.RecordObject(ClipAsset!, "Edit Event Param"); ev.FloatParam = v; MarkClipDirty(); }).Height(22).Show();
                break;
            case ProwlActionArgType.Int:
                using (paper.Box("anim_ev_i_w").Width(82).Height(22).Enter())
                    Origami.NumericField<int>(paper, "anim_ev_i", ev.IntParam, v => { Undo.RecordObject(ClipAsset!, "Edit Event Param"); ev.IntParam = v; MarkClipDirty(); }).Height(22).Show();
                break;
            case ProwlActionArgType.String:
                using (paper.Box("anim_ev_s_w").Width(140).Height(22).Enter())
                    Origami.TextField(paper, "anim_ev_s", ev.StringParam, v => { Undo.RecordObject(ClipAsset!, "Edit Event Param"); ev.StringParam = v; MarkClipDirty(); }).Placeholder("String argument").Height(22).CommitOnEnter().Show();
                break;
            case ProwlActionArgType.Bool:
                Origami.Checkbox(paper, "anim_ev_b", ev.BoolParam, v => { Undo.RecordObject(ClipAsset!, "Edit Event Param"); ev.BoolParam = v; MarkClipDirty(); }).NoLabel().Show();
                break;
            case ProwlActionArgType.Object:
            {
                Type filterType = expectedObjectType != null && typeof(EngineObject).IsAssignableFrom(expectedObjectType)
                    ? expectedObjectType
                    : typeof(EngineObject);
                SelectorTabs tabs = typeof(Asset).IsAssignableFrom(filterType)
                    ? SelectorTabs.Assets
                    : (filterType == typeof(GameObject) || typeof(Component).IsAssignableFrom(filterType)
                        ? SelectorTabs.Scene
                        : (SelectorTabs.Assets | SelectorTabs.Scene));

                string objLabel = ev.ObjectParam.IsValid()
                    ? $"{ev.ObjectParam!.Name} ({ev.ObjectParam.GetType().Name})"
                    : $"None ({filterType.Name})";

                Origami.Button(paper, "anim_ev_obj", objLabel, () =>
                {
                    SelectorModal.Open($"Select {filterType.Name}", filterType, tabs, picked =>
                    {
                        if (!IsClipValid) return;
                        Undo.RecordObject(ClipAsset!, "Edit Event Object Param");
                        ev.ObjectParam = picked as EngineObject;
                        MarkClipDirty();
                    });
                }).Small().Height(22).Show();
                break;
            }
        }
    }

    private void DrawMultiSelectedKeysFooter(Paper paper, Scribe.FontFile font)
    {
        if (!IsClipValid) return;

        var curveKeys = new Dictionary<SceneCurveTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var objectKeys = new Dictionary<SceneObjectTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var stringKeys = new Dictionary<SceneStringTrack, HashSet<int>>(ReferenceEqualityComparer.Instance);
        ResolveSelectedUnderlyingKeys(curveKeys, objectKeys, stringKeys);

        int totalCount = 0;
        int curveKeyCount = 0;
        float avgVal = 0f;
        CurveInterpolation commonInterp = CurveInterpolation.CubicSpline;
        bool hasInterp = false;

        foreach ((SceneCurveTrack track, HashSet<int> indices) in curveKeys)
        {
            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= track.Curve.Count) continue;
                Keyframe kf = track.Curve[idx];
                avgVal += kf.Value;
                if (!hasInterp) { commonInterp = kf.Interpolation; hasInterp = true; }
                curveKeyCount++;
                totalCount++;
            }
        }
        foreach (HashSet<int> set in objectKeys.Values) totalCount += set.Count;
        foreach (HashSet<int> set in stringKeys.Values) totalCount += set.Count;
        if (totalCount == 0) return;

        paper.Box("anim_mk_badge").Width(UnitValue.Auto).Height(20).Padding(6, 6, 0, 0)
            .Rounded(4).BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Amber400, 45))
            .Text($"{totalCount} Keys", EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Amber400)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();

        // Batch Time Shift (Δt)
        paper.Box("anim_mk_dt_l").Width(UnitValue.Auto).Height(20).IsNotInteractable()
            .Text("Shift T:", font).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
        using (paper.Box("anim_mk_dt_w").Width(58).Height(20).Enter())
        {
            Origami.NumericField<float>(paper, "anim_mk_dt", _batchTimeOffsetInput, dt =>
            {
                if (MathF.Abs(dt) > 1e-5f)
                {
                    ShiftSelectedKeysTime(dt);
                    _batchTimeOffsetInput = 0f;
                }
            }).Format("0.##").Suffix("s").Height(20).Show();
        }

        if (curveKeyCount > 0)
        {
            float displayVal = avgVal / curveKeyCount;

            // Batch Set Value
            paper.Box("anim_mk_val_l").Width(UnitValue.Auto).Height(20).IsNotInteractable()
                .Text("Val:", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            using (paper.Box("anim_mk_val_w").Width(62).Height(20).Enter())
            {
                Origami.NumericField<float>(paper, "anim_mk_val", displayVal, newVal =>
                {
                    ModifySelectedCurveKeys("Set Selected Keyframes Value", k => new Keyframe(k.Time, newVal, k.InTangent, k.OutTangent, k.Interpolation));
                }).Format("0.###").Height(20).Show();
            }

            // Batch Offset Value (Δv)
            paper.Box("anim_mk_dv_l").Width(UnitValue.Auto).Height(20).IsNotInteractable()
                .Text("+/-:", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            using (paper.Box("anim_mk_dv_w").Width(54).Height(20).Enter())
            {
                Origami.NumericField<float>(paper, "anim_mk_dv", _batchValueOffsetInput, dv =>
                {
                    if (MathF.Abs(dv) > 1e-5f)
                    {
                        ModifySelectedCurveKeys("Offset Selected Keyframes Value", k => new Keyframe(k.Time, k.Value + dv, k.InTangent, k.OutTangent, k.Interpolation));
                        _batchValueOffsetInput = 0f;
                    }
                }).Format("0.##").Height(20).Show();
            }

            // Batch Interpolation Mode
            using (paper.Box("anim_mk_interp_w").Width(92).Height(20).Enter())
            {
                Origami.EnumDropdown(paper, "anim_mk_interp", commonInterp, interp =>
                {
                    CurveTangentMode? tangent = interp switch
                    {
                        CurveInterpolation.Linear => CurveTangentMode.Linear,
                        CurveInterpolation.CubicSpline => CurveTangentMode.Auto,
                        _ => null,
                    };
                    ModifySelectedCurveKeys("Change Selected Keyframes Interpolation",
                        k => new Keyframe(k.Time, k.Value, k.InTangent, k.OutTangent, interp), tangent);
                }).Show();
            }
        }

        Origami.Button(paper, "anim_mk_copy", "Copy", CopySelectedKeys).Small().Height(20).Show();
        Origami.Button(paper, "anim_mk_del", $"Delete {totalCount}", DeleteSelectedKeysOrEvent).Danger().Small().Height(20).Show();
    }

    private void DrawSelectedCurveTrackKeyFooter(Paper paper, Scribe.FontFile font, SceneCurveTrack track, int keyIndex)
    {
        if (!IsClipValid || keyIndex < 0 || keyIndex >= track.Curve.Count) return;

        Keyframe kf = track.Curve[keyIndex];
        paper.Box("anim_kf_lbl").Width(UnitValue.Auto).Height(20).IsNotInteractable()
            .Text($"{track.PropertyPath} @ {kf.Time:0.00}s =", font).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

        using (paper.Box("anim_kf_val_w").Width(72).Height(20).Enter())
        {
            Origami.NumericField<float>(paper, "anim_kf_val", kf.Value, newV =>
            {
                Undo.RecordObject(ClipAsset!, "Edit Keyframe Value");
                track.Curve.MoveKey(keyIndex, new Keyframe(kf.Time, newV, kf.InTangent, kf.OutTangent, kf.Interpolation));
                _clip!.Invalidate();
                MarkClipDirty();
                RefreshLiveSample();
            }).Format("0.###").Height(20).Show();
        }

        using (paper.Box("anim_kf_interp_w").Width(92).Height(20).Enter())
        {
            Origami.EnumDropdown(paper, "anim_kf_interp", kf.Interpolation, interp =>
            {
                Undo.RecordObject(ClipAsset!, "Edit Keyframe Interpolation");
                int landed = track.Curve.MoveKey(keyIndex, new Keyframe(kf.Time, kf.Value, kf.InTangent, kf.OutTangent, interp));
                if (interp == CurveInterpolation.Linear) ApplyTangentModeToKey(track.Curve, landed, CurveTangentMode.Linear);
                else if (interp == CurveInterpolation.CubicSpline) ApplyTangentModeToKey(track.Curve, landed, CurveTangentMode.Auto);
                _clip!.Invalidate();
                MarkClipDirty();
                RefreshLiveSample();
            }).Show();
        }

        Origami.Button(paper, "anim_kf_del", "Delete Key", DeleteSelectedKeysOrEvent).Danger().Small().Height(20).Show();
    }

    private void DrawSelectedKeyFooter(Paper paper, Scribe.FontFile font, TrackRow row, int keyIndex)
    {
        if (!IsClipValid) return;

        if (row.Kind == TrackRowKind.Curve && row.CurveTrack != null && keyIndex < row.CurveTrack.Curve.Count)
        {
            DrawSelectedCurveTrackKeyFooter(paper, font, row.CurveTrack, keyIndex);
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
                    MarkClipDirty();
                    RefreshLiveSample();
                }).Height(20).CommitOnEnter().Show();
            }

            Origami.Button(paper, "anim_skf_del", "Delete Key", () => DeleteKeyFromRow(row, keyIndex)).Danger().Small().Height(20).Show();
        }
    }
}
