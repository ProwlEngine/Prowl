// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Motion;
using Prowl.Runtime.AnimationNodes;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using MotionAvatar = Prowl.Motion.Avatar;
using MotionClip = Prowl.Motion.AnimationClipBase;
using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime;

/// <summary>When and with which clock an <see cref="Animator"/> advances its playback.</summary>
public enum AnimatorUpdateMode
{
    /// <summary>Advances every frame using scaled <see cref="Time.DeltaTime"/>.</summary>
    Normal = 0,
    /// <summary>Advances in FixedUpdate before physics using <see cref="Time.FixedDeltaTime"/> (ideal for moving platforms and kinematic bodies).</summary>
    AnimatePhysics = 1,
    /// <summary>Advances every frame using <see cref="Time.UnscaledDeltaTime"/> (keeps animating when <see cref="Time.TimeScale"/> is 0, ideal for pause/UI).</summary>
    UnscaledTime = 2,
}

/// <summary>Controls whether an <see cref="Animator"/> continues evaluating when its renderers are culled or disabled.</summary>
public enum AnimatorCullingMode
{
    /// <summary>Always evaluates animation every tick regardless of visibility.</summary>
    AlwaysAnimate = 0,
    /// <summary>Skips evaluation when no attached renderer is enabled.</summary>
    CullCompletely = 1,
}

/// <summary>
/// Maps a named slot (referenced in <see cref="AnimationClip"/> tracks via <c>"@SlotName"</c> or <c>"@SlotName/ChildPath"</c>)
/// to a specific <see cref="GameObject"/> in the scene (Timeline / Sequencer style).
/// </summary>
[Serializable]
public sealed class AnimatorObjectBinding
{
    public string SlotName = string.Empty;
    public GameObject? Target;

    public AnimatorObjectBinding() { }

    public AnimatorObjectBinding(string slotName, GameObject? target)
    {
        SlotName = slotName ?? string.Empty;
        Target = target;
    }
}

/// <summary>Plays clips with cross fades, or an animation graph, on a rig. Runs between Update and LateUpdate.</summary>
[AddComponentMenu("Animation/Animator")]
[ComponentIcon("")] // Film
public class Animator : Component
{
    /// <summary>The rig the clips play on. Usually the avatar the model was imported with.</summary>
    public Avatar? Avatar;

    /// <summary>The graph to run. Without one the animator plays <see cref="Clips"/>.</summary>
    public AnimationGraph? Graph;

    /// <summary>Clips this animator can play by name, and the first of which it plays on enable.</summary>
    public List<AnimationClip> Clips = [];

    /// <summary>
    /// Optional scene object bindings allowing tracks with <c>"@SlotName"</c> paths to drive external
    /// <see cref="GameObject"/>s outside this Animator's child hierarchy.
    /// </summary>
    public List<AnimatorObjectBinding> ObjectBindings = [];

    public bool PlayAutomatically = true;

    public float Speed = 1f;

    /// <summary>Which update loop and clock drives this animator (<see cref="AnimatorUpdateMode.Normal"/>, <see cref="AnimatorUpdateMode.AnimatePhysics"/>, or <see cref="AnimatorUpdateMode.UnscaledTime"/>).</summary>
    public AnimatorUpdateMode UpdateMode = AnimatorUpdateMode.Normal;

    /// <summary>Whether evaluation runs unconditionally or skips when renderers are inactive.</summary>
    public AnimatorCullingMode CullingMode = AnimatorCullingMode.AlwaysAnimate;

    /// <summary>Move the GameObject by the root motion the animation describes.</summary>
    public bool ApplyRootMotion;

    /// <summary>What the graph's ground probe is allowed to hit.</summary>
    public LayerMask GroundLayers = LayerMask.Everything;

    [NonSerialized] private AnimatorBase? _animator;
    [NonSerialized] private ProwlClipAnimator? _clips;
    [NonSerialized] private ProwlGraphAnimator? _graph;
    [NonSerialized] private MotionAvatar? _boundAvatar;
    [NonSerialized] private AnimatorRagdoll? _ragdoll;

    internal AnimatorRagdoll? Ragdoll => _ragdoll;
    [NonSerialized] private AnimationClip? _pending;
    [NonSerialized] private bool _autoPlayPending;
    [NonSerialized] private readonly Dictionary<string, ParameterValue> _pendingParameters = [];
    [NonSerialized] private readonly Dictionary<string, PluggedGraph> _slots = [];
    [NonSerialized] private readonly Dictionary<string, Pose> _externalPoses = [];
    [NonSerialized] private AnimationGraph? _boundGraph;
    [NonSerialized] private int _boundVersion;
    [NonSerialized] private int _seenEdits = -1;

    // The graph version that failed to bind, so it is not retried until it changes.
    [NonSerialized] private AnimationGraph? _failedGraph;
    [NonSerialized] private int _failedVersion;

    // The avatar content the binding was built from, so a reimported rig rebinds.
    [NonSerialized] private Avatar? _boundAvatarAsset;
    [NonSerialized] private int _boundAvatarContent;

    // Property track bindings and playback cursor for clips that carry CurveTracks, ObjectTracks, StringTracks, or PropertyEvents.
    private sealed class CachedPropertyBinding
    {
        public required AnimationClipBinding Binding;
        public required int Version;
    }

    [NonSerialized] private readonly Dictionary<AnimationClip, CachedPropertyBinding> _propertyBindings = new(ReferenceEqualityComparer.Instance);
    [NonSerialized] private readonly Dictionary<int, int> _graphNodeLastLoopCount = [];
    [NonSerialized] private readonly HashSet<int> _activeGraphClipNodes = [];
    [NonSerialized] private Motion.AnimationGraph? _cachedGraphParentsFor;
    [NonSerialized] private Dictionary<int, int>? _cachedGraphParents;
    [NonSerialized] private float _propertyElapsedTime;
    [NonSerialized] private float _propertyClipTime;
    [NonSerialized] private float _pendingNormalizedStartTime;
    [NonSerialized] private bool _propertyPlaying;
    [NonSerialized] private bool _propertyPaused;
    [NonSerialized] private MotionSkeleton? _fallbackSkeleton;

    /// <summary>How long a graph has to stop changing before a running animator picks the edit up.</summary>
    private const long SettleMilliseconds = 150;

    /// <summary>The clip playing now, or the one being faded to.</summary>
    public AnimationClip? CurrentClip { get; private set; }

    /// <summary>The normalized (0-1) playback time of the currently playing clip (<see cref="CurrentClip"/>), or
    /// <c>0</c> if <see cref="IsGraph"/> is <see langword="true"/> or nothing is playing.</summary>
    public float NormalizedTime => _graph != null
        ? 0f
        : (_clips is { CurrentClip: not null }
            ? _clips.NormalizedTime
            : (_propertyPlaying && CurrentClip.IsValid() && CurrentClip!.Duration > 1e-6f
                ? Math.Clamp(_propertyClipTime / CurrentClip.Duration, 0f, 1f)
                : 0f));

    /// <summary>The playback time (in seconds) of the currently playing clip (<see cref="CurrentClip"/>), or
    /// <c>0</c> if <see cref="IsGraph"/> is <see langword="true"/> or nothing is playing.</summary>
    public float CurrentTime => _graph != null
        ? 0f
        : (_clips is { CurrentClip: not null } ? _clips.CurrentTime : (_propertyPlaying ? _propertyClipTime : 0f));

    /// <summary>Current evaluated time within <see cref="CurrentClip"/> in seconds (for direct clip playback).</summary>
    public float Time
    {
        get => _propertyPlaying ? _propertyClipTime : (_clips?.CurrentTime ?? _propertyClipTime);
        set
        {
            AssertOwner();
            _propertyElapsedTime = value;
            _propertyClipTime = CurrentClip.IsValid() ? CurrentClip!.WrapTime(value) : MathF.Max(0f, value);
        }
    }

    /// <summary>Whether direct clip playback is currently paused.</summary>
    public bool IsPaused => _propertyPaused;

    public bool IsBound => _animator != null;

    public bool IsGraph => _graph != null;

    /// <summary>The pose the animator produced this frame, or null before it is bound.</summary>
    public Pose? Pose => _animator?.Pose;

    /// <summary>The root motion the last update produced, in character space.</summary>
    public Transform3D RootMotionDelta => _animator?.RootMotionDelta ?? Transform3D.Identity;

    /// <summary>The running graph, for reading node state. Null while clips are playing.</summary>
    public AnimationGraphInstance? GraphInstance => _graph?.Graph;

    public SampledEventsBuffer? Events => _graph?.Events;

    // The world this animator is registered with, kept so it leaves even during teardown, when it no longer reads as in one.
    private Scene? _registeredIn;

    public override void OnEnable()
    {
        Rebind();
        _autoPlayPending = PlayAutomatically;
        if (!IsInWorld) return;
        _registeredIn = Scene;
        _registeredIn.Animation.Register(this);
    }

    public override void OnDisable()
    {
        if (!ReferenceEquals(_registeredIn, null)) _registeredIn.Animation.Unregister(this);
        _registeredIn = null;
        Rebind();
    }

    /// <summary>Binds the bones again. Call after the hierarchy under the animator changes.</summary>
    public void Rebind()
    {
        AssertOwner();
        foreach (CachedPropertyBinding cached in _propertyBindings.Values)
            cached.Binding.ResetActiveEvents(fireExitCallbacks: false);
        _animator = null;
        _clips = null;
        _graph = null;
        _boundAvatar = null;
        _boundAvatarAsset = null;
        _fallbackSkeleton = null;
        _propertyBindings.Clear();
        _graphNodeLastLoopCount.Clear();
        _activeGraphClipNodes.Clear();
        _cachedGraphParentsFor = null;
        _cachedGraphParents = null;
        _ragdoll?.Release();
        _ragdoll = null;
    }

    /// <summary>Sets a graph parameter. A write made before the graph is bound is applied once it is.</summary>
    public void SetFloat(string name, float value) => SetParameter(name, ParameterValue.FromFloat(value));

    public void SetBool(string name, bool value) => SetParameter(name, ParameterValue.FromBool(value));

    public void SetInt(string name, int value) => SetParameter(name, ParameterValue.FromInt(value));

    /// <summary>Sets a trigger, which stays set until a state machine transition fires on it.</summary>
    public void SetTrigger(string name) => SetBool(name, true);

    /// <summary>Clears a trigger no transition has used yet.</summary>
    public void ResetTrigger(string name) => SetBool(name, false);

    public void SetVector(string name, Float3 value) => SetParameter(name, ParameterValue.FromVector(value));

    public void SetTarget(string name, Target value) => SetParameter(name, ParameterValue.FromTarget(value));

    public void SetId(string name, StringID value) => SetParameter(name, ParameterValue.FromId(value));

    /// <summary>Reads a graph parameter. Before the graph is bound it reads what was set, and an unknown name reads as zero.</summary>
    public float GetFloat(string name) => GetParameter(name).AsFloat();

    public bool GetBool(string name) => GetParameter(name).AsBool();

    public int GetInt(string name) => GetParameter(name).AsInt();

    public Float3 GetVector(string name) => GetParameter(name).Vector;

    public Target GetTarget(string name) => GetParameter(name).Target;

    public StringID GetId(string name) => GetParameter(name).AsId();

    private ParameterValue GetParameter(string name)
    {
        if (_graph == null) return _pendingParameters.TryGetValue(name, out ParameterValue pending) ? pending : default;
        if (_graph.Graph.GetParameterIndex(name) >= 0) return Read(_graph.Graph, name);

        Debug.LogWarningOnce($"Animator.Parameter.{GameObject.Name}.{name}",
            $"[Animator] '{GameObject.Name}' read the parameter '{name}', which its graph does not have.");
        return default;
    }

    private void SetParameter(string name, ParameterValue value)
    {
        AssertOwner();
        if (_graph != null)
        {
            TryWrite(name, value);
            return;
        }

        if (_animator != null && Graph.IsNotValid()) return;
        _pendingParameters[name] = value;
    }

    /// <summary>Writes a parameter, warning once instead of throwing when the graph cannot take it.</summary>
    private void TryWrite(string name, ParameterValue value)
    {
        try
        {
            _graph!.Graph.SetParameterValue(name, value);
        }
        catch (Exception ex)
        {
            Debug.LogWarningOnce($"Animator.Parameter.{GameObject.Name}.{name}",
                $"[Animator] '{GameObject.Name}' set the parameter '{name}', which its graph cannot take: {ex.Message}");
        }
    }

    /// <summary>
    /// The pose the External Pose node of this name plays, on its source skeleton, for the game to write
    /// into each frame. Null when the running graph has no such node. From the first call the node plays
    /// this pose instead of the reference pose.
    /// </summary>
    public Pose? GetExternalPose(string name)
    {
        AssertOwner();
        if (FindExternalPose(name) is not { } node) return null;
        node.HasPose = true;
        _externalPoses[name] = node.Source;
        return node.Source;
    }

    /// <summary>Puts the External Pose node of this name back on the reference pose.</summary>
    public void ClearExternalPose(string name)
    {
        AssertOwner();
        _externalPoses.Remove(name);
        if (FindExternalPose(name) is { } node) node.HasPose = false;
    }

    // A rebind makes new nodes, which carry on from the poses the old ones were showing.
    private void KeepExternalPoses()
    {
        foreach ((string name, Pose old) in new List<KeyValuePair<string, Pose>>(_externalPoses))
        {
            if (FindExternalPose(name) is not { } node || !ReferenceEquals(node.Source.Skeleton, old.Skeleton))
            {
                _externalPoses.Remove(name);
                continue;
            }
            node.Source.CopyFrom(old);
            node.HasPose = true;
            _externalPoses[name] = node.Source;
        }
    }

    /// <summary>
    /// Plays a graph in the Graph Slot of this name, in place of the slot's fallback, or takes it out with
    /// null. The graph stays plugged in across rebinds, and one set before binding is plugged in once bound.
    /// </summary>
    public void SetGraphSlot(string slot, AnimationGraph? graph)
    {
        AssertOwner();
        PluggedGraph? plugged = graph.IsValid() ? new PluggedGraph(graph!) : null;
        if (plugged != null) _slots[slot] = plugged;
        else _slots.Remove(slot);

        if (_graph != null) Plug(slot, plugged);
    }

    // What a slot plays, and the version of it that is plugged in, so an edit to it is picked up like one to the animator's own graph.
    private sealed class PluggedGraph(AnimationGraph graph)
    {
        public readonly AnimationGraph Graph = graph;
        public int Version;
        public int SeenEdits = -1;

        public bool OutOfDate
        {
            get
            {
                if (SeenEdits == AnimationGraph.Edits) return false;

                if (Graph.DeepVersion == Version)
                {
                    SeenEdits = AnimationGraph.Edits;
                    return false;
                }
                return Graph.SinceChanged >= SettleMilliseconds;
            }
        }
    }

    private void Plug(string slot, PluggedGraph? plugged)
    {
        AnimationGraph? graph = plugged?.Graph;
        if (plugged != null)
        {
            graph!.LoadDependencies();
            plugged.Version = graph.DeepVersion;
        }

        try
        {
            AnimationGraphInstance? instance = null;
            if (graph.IsValid() && _boundAvatar != null && graph!.Compile(_boundAvatar.Skeleton, _boundAvatar) is { } compiled)
                instance = compiled.CreateInstance(_boundAvatar);
            _graph!.SetExternalGraph(slot, instance);
        }
        catch (Exception ex)
        {
            Debug.LogWarningOnce($"Animator.Slot.{GameObject.Name}.{slot}",
                $"[Animator] '{GameObject.Name}' could not play a graph in the slot '{slot}': {ex.Message}");
        }
    }

    private ExternalPoseInstance? FindExternalPose(string name)
    {
        AnimationGraph? asset = Graph;
        if (_graph == null || asset.IsNotValid() || !asset.TryGetCompiledMaps(_graph.Graph.Graph, out IReadOnlyDictionary<string, int>? nodes, out _)) return null;

        foreach (GraphNodeRecord record in asset.Nodes)
            if (record.Type == AnimationNodeIds.ExternalPose && record.Get(ExternalPoseNode.NameSetting) == name
                && nodes.TryGetValue(record.Id, out int index))
                return _graph.Graph.TryGetNodeInstance(index) as ExternalPoseInstance;
        return null;
    }

    /// <summary>Plays a clip from the start, cancelling any cross fade.</summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="normalizedStartTime">The time (normalized to 0-1) in <paramref name="clip"/> to start playing.</param>
    public void Play(AnimationClip clip, float normalizedStartTime = 0)
    {
        AssertOwner();
        ArgumentNullException.ThrowIfNull(clip);
        if (_graph != null)
            Debug.LogWarningOnce($"Animator.PlayWithGraph.{GameObject.Name}",
                $"[Animator] '{GameObject.Name}' runs a graph, so Play only takes effect if the graph is removed.");
        bool restartingSameActiveClip = ReferenceEquals(CurrentClip, clip) && _propertyPlaying;
        if (CurrentClip.IsValid() && _propertyBindings.TryGetValue(CurrentClip!, out CachedPropertyBinding? prevBinding))
        {
            prevBinding.Binding.ResetActiveEvents(fireExitCallbacks: true);
            if ((!ReferenceEquals(CurrentClip, clip) || restartingSameActiveClip) && CurrentClip!.CompletionMode == AnimationClipCompletionMode.RestoreState)
                prevBinding.Binding.RestoreBaseline();
        }
        if (_propertyBindings.TryGetValue(clip, out CachedPropertyBinding? nextBinding))
        {
            // When restarting the same active KeepState clip mid-playback, preserve its initial baseline rather than
            // capturing mid-animation values (which would corrupt Additive tracks).
            if (!restartingSameActiveClip || clip.CompletionMode == AnimationClipCompletionMode.RestoreState)
                nextBinding.Binding.CaptureBaseline();
        }
        CurrentClip = clip;
        _pending = clip;
        _pendingNormalizedStartTime = normalizedStartTime;
        _autoPlayPending = false;
        float startSeconds = normalizedStartTime > 0f
            ? Math.Clamp(normalizedStartTime, 0f, 1f) * clip.Duration
            : (Speed >= 0f ? 0f : clip.Duration);
        _propertyElapsedTime = startSeconds;
        _propertyClipTime = clip.WrapTime(startSeconds);
        _propertyPlaying = true;
        _propertyPaused = false;
        if (_clips != null && Resolve(clip) is { } runtime)
        {
            _clips.Play(runtime, normalizedStartTime, clip.EffectiveLoop);
            _pending = null;
            _pendingNormalizedStartTime = 0f;
        }
    }

    public void Play(string clipName, float normalizedStartTime = 0)
    {
        foreach (AnimationClip clip in Clips)
            if (clip.IsValid() && clip.Name == clipName) { Play(clip, normalizedStartTime); return; }
        Debug.LogWarning($"[Animator] '{GameObject.Name}' has no clip named '{clipName}'.");
    }

    /// <summary>Stops direct clip playback and resets the playhead to zero.</summary>
    public void Stop()
    {
        AssertOwner();
        if (CurrentClip.IsValid() && _propertyBindings.TryGetValue(CurrentClip!, out CachedPropertyBinding? prevBinding))
        {
            prevBinding.Binding.ResetActiveEvents(fireExitCallbacks: true);
            if (CurrentClip!.CompletionMode == AnimationClipCompletionMode.RestoreState)
                prevBinding.Binding.RestoreBaseline();
        }
        _propertyPlaying = false;
        _propertyPaused = false;
        _autoPlayPending = false;
        _pending = null;
        _pendingNormalizedStartTime = 0f;
        _propertyElapsedTime = 0f;
        _propertyClipTime = 0f;
    }

    /// <summary>Pauses direct clip playback.</summary>
    public void Pause()
    {
        AssertOwner();
        _propertyPaused = true;
    }

    /// <summary>Resumes paused direct clip playback.</summary>
    public void Resume()
    {
        AssertOwner();
        _propertyPaused = false;
    }

    /// <summary>Adds a clip to <see cref="Clips"/> if it is not already present.</summary>
    public void AddClip(AnimationClip clip)
    {
        AssertOwner();
        ArgumentNullException.ThrowIfNull(clip);
        if (!Clips.Contains(clip))
            Clips.Add(clip);
    }

    /// <summary>Binds a named external object slot (referenced by <c>"@SlotName"</c> tracks) to a scene <see cref="GameObject"/>.</summary>
    public void SetObjectBinding(string slotName, GameObject? target)
    {
        AssertOwner();
        if (string.IsNullOrWhiteSpace(slotName)) return;
        string clean = slotName.Trim().TrimStart('@');
        for (int i = 0; i < ObjectBindings.Count; i++)
        {
            if (ObjectBindings[i].SlotName.Equals(clean, StringComparison.OrdinalIgnoreCase))
            {
                ObjectBindings[i].Target = target;
                _propertyBindings.Clear();
                return;
            }
        }
        ObjectBindings.Add(new AnimatorObjectBinding(clean, target));
        _propertyBindings.Clear();
    }

    /// <summary>Returns the scene <see cref="GameObject"/> bound to <paramref name="slotName"/>, or null if unbound.</summary>
    public GameObject? GetObjectBinding(string slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName)) return null;
        string clean = slotName.Trim().TrimStart('@');
        for (int i = 0; i < ObjectBindings.Count; i++)
        {
            if (ObjectBindings[i].SlotName.Equals(clean, StringComparison.OrdinalIgnoreCase))
                return ObjectBindings[i].Target.IsValid() ? ObjectBindings[i].Target : null;
        }
        return null;
    }

    /// <summary>Samples a specific <see cref="AnimationClip"/> at <paramref name="timeSeconds"/> (both skeletal pose and property tracks) onto the hierarchy immediately.</summary>
    public void Sample(AnimationClip clip, float timeSeconds)
    {
        AssertOwner();
        ArgumentNullException.ThrowIfNull(clip);
        if (GameObject.IsNotValid()) return;

        if (clip.State is not (AssetState.Missing or AssetState.Failed))
            clip.Load();

        CurrentClip = clip;
        _propertyElapsedTime = timeSeconds;
        _propertyClipTime = clip.WrapTime(timeSeconds);

        // 1. Sample skeletal/humanoid pose if available
        Avatar? avatar = Avatar.IsValid() ? Avatar : clip.Avatar;
        if (avatar.IsValid())
        {
            if (avatar!.State is not (AssetState.Missing or AssetState.Failed))
                avatar.Load();
            if (avatar.IsLoaded && avatar.Skeleton != null)
            {
                MotionClip? runtime = clip.GetClip(avatar.Runtime);
                if (runtime != null)
                {
                    var binding = new AnimatorBinding(Transform, avatar.Skeleton);
                    var pose = new Pose(avatar.Skeleton);
                    pose.SetToReferencePose(false);
                    float norm = clip.Duration > 1e-6f ? Math.Clamp(_propertyClipTime / clip.Duration, 0f, 1f) : 0f;
                    runtime.GetPose(norm, pose);
                    for (int b = 0; b < pose.BoneCount; b++)
                        binding.ApplyBone(b, pose.GetTransform(b));
                    for (int c = 0; c < pose.FloatChannelCount; c++)
                        binding.ApplyChannel(c, pose.GetFloat(c));
                }
            }
        }

        // 2. Sample property tracks layered on top
        if (clip.HasPropertyTracks)
        {
            AnimationClipBinding propBinding = GetOrCreatePropertyBinding(clip);
            propBinding.Sample(_propertyClipTime);
        }
    }

    /// <summary>
    /// Blends to a clip over <paramref name="fadeDuration"/>, from whatever is playing now.
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="normalizedStartTime">The time (normalized to 0-1) in <paramref name="clip"/> to start playing.</param>
    /// <param name="fadeDuration">The duration of the cross fade, in seconds.</param>
    public void CrossFade(AnimationClip clip, float normalizedStartTime = 0, float fadeDuration = 0)
    {
        AssertOwner();
        ArgumentNullException.ThrowIfNull(clip);
        if (_clips == null || Resolve(clip) is not { } runtime) { Play(clip, normalizedStartTime); return; }

        bool restartingSameActiveClip = ReferenceEquals(CurrentClip, clip) && _propertyPlaying;
        if (CurrentClip.IsValid() && _propertyBindings.TryGetValue(CurrentClip!, out CachedPropertyBinding? prevBinding))
        {
            prevBinding.Binding.ResetActiveEvents(fireExitCallbacks: true);
            if ((!ReferenceEquals(CurrentClip, clip) || restartingSameActiveClip) && CurrentClip!.CompletionMode == AnimationClipCompletionMode.RestoreState)
                prevBinding.Binding.RestoreBaseline();
        }
        if (_propertyBindings.TryGetValue(clip, out CachedPropertyBinding? nextBinding))
        {
            if (!restartingSameActiveClip || clip.CompletionMode == AnimationClipCompletionMode.RestoreState)
                nextBinding.Binding.CaptureBaseline();
        }

        CurrentClip = clip;
        _autoPlayPending = false;
        float startSeconds = normalizedStartTime > 0f
            ? Math.Clamp(normalizedStartTime, 0f, 1f) * clip.Duration
            : (Speed >= 0f ? 0f : clip.Duration);
        _propertyElapsedTime = startSeconds;
        _propertyClipTime = clip.WrapTime(startSeconds);
        _propertyPlaying = true;
        _propertyPaused = false;
        _clips.CrossFade(runtime, normalizedStartTime, fadeDuration, clip.EffectiveLoop);
    }

    /// <summary>Advances the animation and pushes the pose and property tracks onto the hierarchy.</summary>
    internal void Tick(float deltaTime)
    {
        if (_animator != null && (GraphOutOfDate(out AnimationGraph? edited) || AvatarChanged()))
        {
            KeepParameters();
            Rebind();
        }

        if (_graph != null)
            foreach ((string slot, PluggedGraph plugged) in _slots)
                if (plugged.OutOfDate) Plug(slot, plugged);

        if (!EnsureBound()) return;

        float effectiveSpeed = _propertyPaused ? 0f : Speed;
        _animator!.Speed = effectiveSpeed;
        _animator.ApplyRootMotionToEngine = ApplyRootMotion;

        if (_clips != null) StartPending();
        _animator.Update(deltaTime);

        if (_graph != null)
            EvaluateGraphPropertyTracks();
        else
            EvaluateClipPropertyTracks(deltaTime);
    }

    // Binding loads the rig (or synthesizes a minimal one for rigless property animations), the graph and everything the graph reads,
    // so an animator starts on its first update. Whatever still fails to load is missing, and the graph plays without it.
    private bool EnsureBound()
    {
        if (_animator != null) return true;

        Avatar? avatar = Avatar;
        if (avatar.IsNotValid())
        {
            // Check if any assigned clip carries its own Avatar
            for (int i = 0; i < Clips.Count; i++)
            {
                if (Clips[i].IsValid() && Clips[i].Avatar.IsValid())
                {
                    avatar = Clips[i].Avatar;
                    break;
                }
            }
            if (avatar.IsNotValid() && CurrentClip.IsValid() && CurrentClip!.Avatar.IsValid())
                avatar = CurrentClip.Avatar;
        }

        MotionSkeleton? skeleton = null;
        MotionAvatar? runtime = null;

        if (avatar.IsValid())
        {
            if (avatar!.State is not (AssetState.Missing or AssetState.Failed)) avatar.Load();
            if (!avatar.IsLoaded)
            {
                Debug.LogWarningOnce($"Animator.Avatar.{GameObject.InstanceID}.{avatar.AssetID}",
                    $"[Animator] '{GameObject.Name}' could not load its avatar, so it does not animate.");
                return false;
            }
            skeleton = avatar.Skeleton;
            runtime = avatar.Runtime;
        }

        // If no Avatar is assigned, create a minimal root-only skeleton so Animator and AnimationGraph can drive property clips on rigless objects!
        if (skeleton == null)
        {
            _fallbackSkeleton ??= new MotionSkeleton(
                [new StringID(GameObject.Name)],
                [MotionSkeleton.InvalidIndex],
                [new Transform3D(Transform.LocalPosition, Transform.LocalRotation, Transform.LocalScale)]);
            skeleton = _fallbackSkeleton;
        }

        AnimationGraph? graphAsset = Graph;
        if (graphAsset.IsValid())
        {
            if (graphAsset.State is not (AssetState.Missing or AssetState.Failed)) graphAsset.LoadDependencies();
            if (!graphAsset.IsLoaded)
            {
                Debug.LogWarningOnce($"Animator.Graph.{GameObject.InstanceID}.{graphAsset.AssetID}",
                    $"[Animator] '{GameObject.Name}' could not load its graph, so it plays its clips.");
                graphAsset = null;
            }
        }

        var binding = new AnimatorBinding(Transform, skeleton);
        if (avatar.IsValid() && binding.UnboundBones > 0)
            Debug.LogWarningOnce($"Animator.Unbound.{GameObject.Name}",
                $"[Animator] '{GameObject.Name}' could not find {binding.UnboundBones} of the rig's bones in its hierarchy. Those bones will not animate.");

        _boundAvatar = runtime;
        _boundAvatarAsset = Avatar;
        _boundAvatarContent = Avatar.IsValid() ? Avatar!.ContentVersion : 0;
        if (avatar.IsValid() && runtime != null)
            _ragdoll = new AnimatorRagdoll(this, binding, skeleton, runtime.Humanoid);

        // The graph as assigned, even one that failed to load, so the next frames do not keep retrying it.
        _boundGraph = Graph;
        _boundVersion = Graph is { } bound ? bound.DeepVersion : 0;

        if (graphAsset.IsValid() && TryBindGraph(graphAsset!, binding, skeleton, runtime))
        {
            ReplayPending();
            KeepExternalPoses();
            foreach ((string slot, PluggedGraph plugged) in _slots) Plug(slot, plugged);
            return true;
        }

        // Clips play when there is no graph or it failed. An edit that fixes the graph takes over again.
        _clips = new ProwlClipAnimator(this, binding, skeleton, runtime);
        _animator = _clips;
        if (graphAsset.IsNotValid()) _pendingParameters.Clear();
        return true;
    }

    /// <summary>Compiles and binds the graph. A failure is logged once per graph version.</summary>
    private bool TryBindGraph(AnimationGraph asset, AnimatorBinding binding, MotionSkeleton skeleton, MotionAvatar? runtime)
    {
        if (ReferenceEquals(asset, _failedGraph) && _boundVersion == _failedVersion) return false;

        try
        {
            if (asset.Compile(skeleton, runtime) is not { } compiled) return false;

            _graph = new ProwlGraphAnimator(this, binding, compiled, skeleton, runtime);
            _animator = _graph;
            _failedGraph = null;
            return true;
        }
        catch (Exception ex)
        {
            _graph = null;
            _failedGraph = asset;
            _failedVersion = _boundVersion;
            Debug.LogError($"[Animator] '{GameObject.Name}' could not run the graph '{asset.Name}': {ex.Message}. " +
                "It plays clips until the graph is changed.");
            return false;
        }
    }

    private void ReplayPending()
    {
        foreach ((string name, ParameterValue value) in _pendingParameters) TryWrite(name, value);
        _pendingParameters.Clear();
    }

    private void StartPending()
    {
        if (_autoPlayPending)
        {
            AnimationClip? first = Clips.Count > 0 ? Clips[0] : null;
            if (first.IsNotValid()) return;
            _autoPlayPending = false;
            Play(first!);
            return;
        }

        if (_pending.IsNotValid())
        {
            _pending = null;
            _pendingNormalizedStartTime = 0f;
            return;
        }
        if (Resolve(_pending!) is not { } runtime) return;

        _clips!.Play(runtime, _pendingNormalizedStartTime, _pending!.EffectiveLoop);
        _pending = null;
        _pendingNormalizedStartTime = 0f;
    }

    private MotionClip? Resolve(AnimationClip clip) => clip.GetClip(_boundAvatar, _animator?.Skeleton ?? _fallbackSkeleton);

    private AnimationClipBinding GetOrCreatePropertyBinding(AnimationClip clip)
    {
        if (_propertyBindings.TryGetValue(clip, out CachedPropertyBinding? cached))
        {
            if (cached.Version == clip.Version)
                return cached.Binding;

            cached.Binding.ResetActiveEvents(fireExitCallbacks: false);
            if (_propertyPlaying || _graph != null)
                cached.Binding.RestoreBaseline();
        }

        var binding = new AnimationClipBinding(GameObject, clip);
        _propertyBindings[clip] = new CachedPropertyBinding { Binding = binding, Version = clip.Version };
        return binding;
    }

    private void EvaluateClipPropertyTracks(float deltaTime)
    {
        AnimationClip? clip = CurrentClip;
        if (clip.IsNotValid() || !_propertyPlaying || _propertyPaused) return;
        if (clip!.State is not (AssetState.Missing or AssetState.Failed))
            clip.Load();
        if (!clip.IsLoaded || !clip.HasPropertyTracks) return;

        AnimationClipBinding propBinding = GetOrCreatePropertyBinding(clip);
        float duration = clip.Duration;
        if (duration <= 1e-6f)
        {
            _propertyClipTime = 0f;
            propBinding.Sample(0f);
            return;
        }

        float dt = deltaTime * Speed;
        float prevClipTime = _propertyClipTime;
        float prevElapsed = _propertyElapsedTime;
        _propertyElapsedTime += dt;

        bool forward = dt >= 0f;
        bool looped = false;
        bool finishedOnce = false;

        AnimationClipWrapMode wrap = clip.EffectiveWrapMode;

        switch (wrap)
        {
            case AnimationClipWrapMode.Once:
                if (forward && _propertyElapsedTime >= duration)
                {
                    _propertyElapsedTime = duration;
                    _propertyClipTime = duration;
                    finishedOnce = true;
                }
                else if (!forward && _propertyElapsedTime <= 0f)
                {
                    _propertyElapsedTime = 0f;
                    _propertyClipTime = 0f;
                    finishedOnce = true;
                }
                else
                {
                    _propertyClipTime = Math.Clamp(_propertyElapsedTime, 0f, duration);
                }
                break;

            case AnimationClipWrapMode.ClampForever:
                _propertyClipTime = Math.Clamp(_propertyElapsedTime, 0f, duration);
                break;

            case AnimationClipWrapMode.Loop:
            {
                int prevCycle = (int)MathF.Floor(prevElapsed / duration);
                int newCycle = (int)MathF.Floor(_propertyElapsedTime / duration);
                looped = prevCycle != newCycle;
                float wrapped = _propertyElapsedTime % duration;
                if (wrapped < 0f) wrapped += duration;
                _propertyClipTime = wrapped;
                break;
            }

            case AnimationClipWrapMode.PingPong:
            {
                _propertyClipTime = clip.WrapTime(_propertyElapsedTime);
                forward = _propertyClipTime >= prevClipTime;
                break;
            }
        }

        propBinding.Sample(_propertyClipTime);

        if (prevElapsed == 0f && MathF.Abs(dt) > 1e-7f && forward)
            propBinding.FireEvents(-1e-5f, _propertyClipTime, forward: true, looped: false);
        else if (MathF.Abs(prevElapsed - duration) <= 1e-6f && MathF.Abs(dt) > 1e-7f && !forward)
            propBinding.FireEvents(duration + 1e-5f, _propertyClipTime, forward: false, looped: false);
        else if (MathF.Abs(dt) > 1e-7f)
            propBinding.FireEvents(prevClipTime, _propertyClipTime, forward, looped);

        if (finishedOnce)
        {
            propBinding.ResetActiveEvents(fireExitCallbacks: true);
            if (clip.CompletionMode == AnimationClipCompletionMode.RestoreState)
                propBinding.RestoreBaseline();
            _propertyPlaying = false;
        }
    }

    private void EvaluateGraphPropertyTracks()
    {
        AnimationGraph? asset = Graph;
        AnimationGraphInstance? instance = _graph?.Graph;
        if (asset.IsNotValid() || instance == null) return;
        if (!asset!.TryGetCompiledClips(instance.Graph, out IReadOnlyList<(int NodeIndex, AnimationClip Clip)> clips) || clips.Count == 0)
            return;

        Dictionary<int, int>? parentNodes = GetOrBuildGraphParentMap(asset, instance.Graph);

        for (int i = 0; i < clips.Count; i++)
        {
            (int nodeIndex, AnimationClip clip) = clips[i];
            if (clip.IsNotValid() || !clip.HasPropertyTracks) continue;
            if (instance.TryGetNodeInstance(nodeIndex) is not PoseNodeInstance poseNode || !poseNode.IsInitialized)
            {
                DeactivateGraphClipNode(nodeIndex, clip);
                continue;
            }

            float weight = ComputeGraphNodeWeight(instance, parentNodes, nodeIndex);
            if (weight <= 1e-5f)
            {
                DeactivateGraphClipNode(nodeIndex, clip);
                continue;
            }

            AnimationClipBinding propBinding = GetOrCreatePropertyBinding(clip);
            if (_activeGraphClipNodes.Add(nodeIndex))
                propBinding.CaptureBaseline();

            float duration = clip.Duration;
            float currTime = poseNode.NormalizedTime * duration;
            float prevTime = poseNode.PreviousTime * duration;
            bool forward = !poseNode.PlayingBackward;

            int prevLoops = _graphNodeLastLoopCount.TryGetValue(nodeIndex, out int lastLoops) ? lastLoops : 0;
            bool looped = poseNode.LoopCount != prevLoops || (forward ? currTime < prevTime - 1e-5f : currTime > prevTime + 1e-5f);
            _graphNodeLastLoopCount[nodeIndex] = poseNode.LoopCount;

            propBinding.SampleWeighted(currTime, weight);

            if (weight >= 0.5f)
            {
                if (MathF.Abs(currTime - prevTime) > 1e-6f)
                    propBinding.FireEvents(prevTime, currTime, forward, looped);
            }
            else if (propBinding.HasActiveRangedEvents)
            {
                propBinding.ResetActiveEvents(fireExitCallbacks: true);
            }
        }
    }

    private void DeactivateGraphClipNode(int nodeIndex, AnimationClip clip)
    {
        bool wasActive = _activeGraphClipNodes.Remove(nodeIndex);
        if (!_propertyBindings.TryGetValue(clip, out CachedPropertyBinding? cached)) return;
        if (cached.Binding.HasActiveRangedEvents)
            cached.Binding.ResetActiveEvents(fireExitCallbacks: true);
        if (wasActive && clip.CompletionMode == AnimationClipCompletionMode.RestoreState)
            cached.Binding.RestoreBaseline();
    }

    private Dictionary<int, int>? GetOrBuildGraphParentMap(AnimationGraph asset, Motion.AnimationGraph running)
    {
        if (ReferenceEquals(_cachedGraphParentsFor, running) && _cachedGraphParents != null)
            return _cachedGraphParents;

        if (!asset.TryGetCompiledMaps(running, out IReadOnlyDictionary<string, int>? nodes, out _))
            return null;

        var map = new Dictionary<int, int>();
        for (int i = 0; i < asset.Nodes.Count; i++)
        {
            GraphNodeRecord rec = asset.Nodes[i];
            if (!nodes.TryGetValue(rec.Id, out int parentIdx) || parentIdx < 0) continue;

            for (int j = 0; j < rec.Inputs.Count; j++)
            {
                string childId = rec.Inputs[j].Node;
                if (childId.Length > 0 && nodes.TryGetValue(childId, out int childIdx) && childIdx >= 0 && childIdx != parentIdx && !map.ContainsKey(childIdx))
                    map[childIdx] = parentIdx;
            }

            for (int j = 0; j < rec.States.Count; j++)
            {
                GraphStateRecord state = rec.States[j];
                if (state.IsAny || state.UsesAsset) continue;
                GraphNodeRecord? outRec = asset.OwnedNode(state.Id, AnimationNodeIds.StateOutput);
                if (outRec != null && nodes.TryGetValue(outRec.Id, out int outIdx) && outIdx >= 0 && outIdx != parentIdx && !map.ContainsKey(outIdx))
                    map[outIdx] = parentIdx;
            }
        }

        _cachedGraphParentsFor = running;
        _cachedGraphParents = map;
        return map;
    }

    private static float ComputeGraphNodeWeight(AnimationGraphInstance instance, IReadOnlyDictionary<int, int>? parentNodes, int targetNodeIndex)
    {
        float weight = 1f;
        int cur = targetNodeIndex;
        int hops = 0;
        while (parentNodes != null && parentNodes.TryGetValue(cur, out int parent) && parent >= 0 && hops++ < 64)
        {
            GraphNodeInstance? parentInst = instance.TryGetNodeInstance(parent);
            if (parentInst == null || !parentInst.IsInitialized)
                return 0f;

            if (parentInst is IBlendWeights blend)
            {
                weight *= blend.WeightOf(cur);
                if (weight <= 1e-5f)
                    return 0f;
            }
            cur = parent;
        }
        return weight;
    }

    // An edit is picked up once it stops changing, since rebinding restarts every state machine.
    private bool GraphOutOfDate(out AnimationGraph? asset)
    {
        asset = Graph;
        if (ReferenceEquals(asset, null) || !ReferenceEquals(asset, _boundGraph)) return !ReferenceEquals(asset, _boundGraph);
        if (_seenEdits == AnimationGraph.Edits) return false;

        if (asset!.DeepVersion == _boundVersion)
        {
            _seenEdits = AnimationGraph.Edits;
            return false;
        }
        return asset.SinceChanged >= SettleMilliseconds;
    }

    private bool AvatarChanged()
        => !ReferenceEquals(Avatar, _boundAvatarAsset) || (Avatar is { } avatar && avatar.ContentVersion != _boundAvatarContent);

    /// <summary>Carries the running parameter values across a rebind.</summary>
    private void KeepParameters()
    {
        AnimationGraphInstance? running = _graph?.Graph;
        if (running == null) return;

        foreach (ControlParameterDefinition parameter in running.Graph.Parameters)
            _pendingParameters[parameter.Name] = Read(running, parameter.Name);
    }

    private static ParameterValue Read(AnimationGraphInstance graph, string name) => graph.GetParameterType(graph.GetParameterIndex(name)) switch
    {
        AnimationValueType.Bool => ParameterValue.FromBool(graph.GetBool(name)),
        AnimationValueType.Int => ParameterValue.FromInt(graph.GetInt(name)),
        AnimationValueType.Vector => ParameterValue.FromVector(graph.GetVector(name)),
        AnimationValueType.Target => ParameterValue.FromTarget(graph.GetTarget(name)),
        AnimationValueType.Id => ParameterValue.FromId(graph.GetId(name)),
        _ => ParameterValue.FromFloat(graph.GetFloat(name)),
    };

    /// <summary>Rebuilds the graph and rebinds. Call after editing the graph asset.</summary>
    public void RecompileGraph()
    {
        AssertOwner();
        if (Graph is { } asset) asset.Invalidate();
        Rebind();
    }

    internal Transform3D RootWorld => new(Transform.Position, Transform.Rotation, Transform.LossyScale);

    internal void ApplyRootMotionDelta(in Transform3D delta)
    {
        Transform3D moved = RootMotionUtil.Apply(RootWorld, delta);
        Transform.Position = moved.position;
        Transform.Rotation = moved.rotation;
    }

    internal bool RaycastGround(Float3 origin, Float3 direction, float maxDistance, out Float3 point, out Float3 normal)
    {
        point = default;
        normal = new Float3(0f, 1f, 0f);

        if (!IsInWorld) return false;

        // The ragdoll stands where the feet are, so the probe would find it as the ground.
        var filter = new QueryFilter(GroundLayers);
        if (_ragdoll != null) filter = filter.Ignoring(_ragdoll.Bodies);

        if (!Scene.Physics.Raycast(origin, direction, maxDistance, out RaycastHit hit, filter))
            return false;

        point = hit.Point;
        normal = hit.Normal;
        return true;
    }
}
