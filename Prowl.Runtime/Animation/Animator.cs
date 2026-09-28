// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Motion;
using Prowl.Runtime.AnimationNodes;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using MotionAvatar = Prowl.Motion.Avatar;
using MotionClip = Prowl.Motion.AnimationClipBase;
using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime;

/// <summary>Plays clips with cross fades, or an animation graph, on a rig. Runs between Update and LateUpdate.</summary>
[AddComponentMenu("Animation/Animator")]
[ComponentIcon("")] // Film
public class Animator : MonoBehaviour
{
    /// <summary>The rig the clips play on. Usually the avatar the model was imported with.</summary>
    public AssetRef<Avatar> Avatar;

    /// <summary>The graph to run. Without one the animator plays <see cref="Clips"/>.</summary>
    public AssetRef<AnimationGraph> Graph;

    /// <summary>Clips this animator can play by name, and the first of which it plays on enable.</summary>
    public List<AssetRef<AnimationClip>> Clips = new();

    public bool PlayAutomatically = true;

    public float Speed = 1f;

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
    [NonSerialized] private readonly Dictionary<string, ParameterValue> _pendingParameters = new();
    [NonSerialized] private readonly Dictionary<string, PluggedGraph> _slots = new();
    [NonSerialized] private readonly Dictionary<string, Pose> _externalPoses = new();
    [NonSerialized] private AnimationGraph? _boundGraph;
    [NonSerialized] private int _boundVersion;
    [NonSerialized] private int _seenEdits = -1;

    // The graph version that failed to bind, so it is not retried until it changes.
    [NonSerialized] private AnimationGraph? _failedGraph;
    [NonSerialized] private int _failedVersion;

    // An avatar that could not be loaded, so binding does not block on it again every frame.
    [NonSerialized] private Guid _missingAvatar;

    /// <summary>How long a graph has to stop changing before a running animator picks the edit up.</summary>
    private const long SettleMilliseconds = 150;

    /// <summary>The clip playing now, or the one being faded to.</summary>
    public AnimationClip? CurrentClip { get; private set; }

    public bool IsBound => _animator != null;

    public bool IsGraph => _graph != null;

    /// <summary>The pose the animator produced this frame, or null before it is bound.</summary>
    public Pose? Pose => _animator?.Pose;

    /// <summary>The root motion the last update produced, in character space.</summary>
    public Transform3D RootMotionDelta => _animator?.RootMotionDelta ?? Transform3D.Identity;

    /// <summary>The running graph, for reading node state. Null while clips are playing.</summary>
    public AnimationGraphInstance? GraphInstance => _graph?.Graph;

    public SampledEventsBuffer? Events => _graph?.Events;

    public override void OnEnable()
    {
        Rebind();
        _autoPlayPending = PlayAutomatically;
        if (Scene.IsValid()) Scene!.Animation.Register(this);
    }

    public override void OnDisable()
    {
        if (Scene.IsValid()) Scene!.Animation.Unregister(this);
        Rebind();
    }

    /// <summary>Binds the bones again. Call after the hierarchy under the animator changes.</summary>
    public void Rebind()
    {
        _animator = null;
        _clips = null;
        _graph = null;
        _boundAvatar = null;
        _missingAvatar = Guid.Empty;
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
        if (_graph != null)
        {
            TryWrite(name, value);
            return;
        }

        if (_animator != null && Graph.IsExplicitNull) return;
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

    /// <summary>Plays a clip from the start, cancelling any cross fade.</summary>
    /// <summary>
    /// The pose the External Pose node of this name plays, on its source skeleton, for the game to write
    /// into each frame. Null when the running graph has no such node. From the first call the node plays
    /// this pose instead of the reference pose.
    /// </summary>
    public Pose? GetExternalPose(string name)
    {
        if (FindExternalPose(name) is not { } node) return null;
        node.HasPose = true;
        _externalPoses[name] = node.Source;
        return node.Source;
    }

    /// <summary>Puts the External Pose node of this name back on the reference pose.</summary>
    public void ClearExternalPose(string name)
    {
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
        PluggedGraph? plugged = graph.IsValid() ? new PluggedGraph(new AssetRef<AnimationGraph>(graph!)) : null;
        if (plugged != null) _slots[slot] = plugged;
        else _slots.Remove(slot);

        if (_graph != null) Plug(slot, plugged);
    }

    // What a slot plays, and the version of it that is plugged in, so an edit to it is picked up like one to the animator's own graph.
    private sealed class PluggedGraph(AssetRef<AnimationGraph> graph)
    {
        public AssetRef<AnimationGraph> Graph = graph;
        public AnimationGraph? Bound;
        public int Version;
        public int SeenEdits = -1;

        public bool OutOfDate
        {
            get
            {
                AnimationGraph? asset = Graph.Res;
                if (!ReferenceEquals(asset, Bound)) return true;
                if (asset.IsNotValid() || SeenEdits == AnimationGraph.Edits) return false;

                if (asset!.DeepVersion == Version)
                {
                    SeenEdits = AnimationGraph.Edits;
                    return false;
                }
                return asset.SinceChanged >= SettleMilliseconds;
            }
        }
    }

    private void Plug(string slot, PluggedGraph? plugged)
    {
        AnimationGraph? graph = plugged?.Graph.Res;
        if (plugged != null)
        {
            plugged.Bound = graph.IsValid() ? graph : null;
            plugged.Version = graph.IsValid() ? graph!.DeepVersion : 0;
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
        AnimationGraph? asset = Graph.Res;
        if (_graph == null || asset.IsNotValid() || !asset!.TryGetCompiledMaps(_graph.Graph.Graph, out var nodes, out _)) return null;

        foreach (GraphNodeRecord record in asset.Nodes)
            if (record.Type == AnimationNodeIds.ExternalPose && record.Get(ExternalPoseNode.NameSetting) == name
                && nodes.TryGetValue(record.Id, out int index))
                return _graph.Graph.TryGetNodeInstance(index) as ExternalPoseInstance;
        return null;
    }

    public void Play(AnimationClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (_graph != null)
            Debug.LogWarningOnce($"Animator.PlayWithGraph.{GameObject.Name}",
                $"[Animator] '{GameObject.Name}' runs a graph, so Play only takes effect if the graph is removed.");
        CurrentClip = clip;
        _pending = clip;
        _autoPlayPending = false;
        if (_clips != null && Resolve(clip) is { } runtime)
        {
            _clips.Play(runtime, clip.Loop);
            _pending = null;
        }
    }

    public void Play(string clipName)
    {
        // By span, so AssetRef caches its load on the list's own entry rather than a copy.
        Span<AssetRef<AnimationClip>> clips = CollectionsMarshal.AsSpan(Clips);
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip? clip = clips[i].Res;
            if (clip.IsValid() && clip!.Name == clipName) { Play(clip); return; }
        }
        Debug.LogWarning($"[Animator] '{GameObject.Name}' has no clip named '{clipName}'.");
    }

    /// <summary>Blends to a clip over <paramref name="seconds"/>, from whatever is playing now.</summary>
    public void CrossFade(AnimationClip clip, float seconds)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (_clips == null || Resolve(clip) is not { } runtime) { Play(clip); return; }

        CurrentClip = clip;
        _autoPlayPending = false;
        _clips.CrossFade(runtime, seconds, clip.Loop);
    }

    /// <summary>Advances the animation and pushes the pose onto the hierarchy.</summary>
    internal void Tick(float deltaTime)
    {
        if (_animator != null && GraphOutOfDate(out AnimationGraph? edited))
        {
            KeepParameters();
            Rebind();
        }

        if (_graph != null)
            foreach ((string slot, PluggedGraph plugged) in _slots)
                if (plugged.OutOfDate) Plug(slot, plugged);

        if (!EnsureBound()) return;

        _animator!.Speed = Speed;
        _animator.ApplyRootMotionToEngine = ApplyRootMotion;

        if (_clips != null) StartPending();
        _animator.Update(deltaTime);
    }

    // Binding loads the rig, the graph and everything the graph reads, so an animator starts on its first update.
    // Whatever still fails to load is missing, and the graph plays without it.
    private bool EnsureBound()
    {
        if (_animator != null) return true;

        if (Avatar.AssetID != _missingAvatar) Avatar.EnsureLoaded();
        Avatar? avatar = Avatar.Res;
        if (avatar.IsNotValid())
        {
            if (Avatar.AssetID != Guid.Empty && Avatar.AssetID != _missingAvatar)
            {
                _missingAvatar = Avatar.AssetID;
                Debug.LogWarning($"[Animator] '{GameObject.Name}' could not load its avatar, so it does not animate.");
            }
            return false;
        }
        if (avatar!.Skeleton == null) return false;

        MotionAvatar? runtime = avatar.Runtime;
        if (runtime == null) return false;

        Graph.EnsureLoaded();
        AnimationGraph? graphAsset = Graph.Res;
        if (graphAsset.IsValid()) graphAsset!.LoadDependencies();
        else if (!Graph.IsExplicitNull)
            Debug.LogWarning($"[Animator] '{GameObject.Name}' could not load its graph, so it plays its clips.");

        var binding = new AnimatorBinding(Transform, avatar.Skeleton);
        if (binding.UnboundBones > 0)
            Debug.LogWarningOnce($"Animator.Unbound.{GameObject.Name}",
                $"[Animator] '{GameObject.Name}' could not find {binding.UnboundBones} of the rig's bones in its hierarchy. Those bones will not animate.");

        _boundAvatar = runtime;
        _ragdoll = new AnimatorRagdoll(this, binding, avatar.Skeleton, runtime.Humanoid);

        _boundGraph = graphAsset.IsValid() ? graphAsset : null;
        _boundVersion = graphAsset.IsValid() ? graphAsset!.DeepVersion : 0;

        if (graphAsset.IsValid() && TryBindGraph(graphAsset!, binding, avatar.Skeleton, runtime))
        {
            ReplayPending();
            KeepExternalPoses();
            foreach ((string slot, PluggedGraph plugged) in _slots) Plug(slot, plugged);
            return true;
        }

        // Clips play when there is no graph or it failed. An edit that fixes the graph takes over again.
        _clips = new ProwlClipAnimator(this, binding, avatar.Skeleton, runtime);
        _animator = _clips;
        if (graphAsset.IsNotValid()) _pendingParameters.Clear();
        return true;
    }

    /// <summary>Compiles and binds the graph. A failure is logged once per graph version.</summary>
    private bool TryBindGraph(AnimationGraph asset, AnimatorBinding binding, MotionSkeleton skeleton, MotionAvatar runtime)
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
            AnimationClip? first = Clips.Count > 0 ? CollectionsMarshal.AsSpan(Clips)[0].Res : null;
            if (first.IsNotValid()) return;
            _autoPlayPending = false;
            Play(first!);
            return;
        }

        if (_pending == null) return;
        if (Resolve(_pending) is not { } runtime) return;

        _clips!.Play(runtime, _pending.Loop);
        _pending = null;
    }

    private MotionClip? Resolve(AnimationClip clip) => clip.GetClip(_boundAvatar);

    // An edit is picked up once it stops changing, since rebinding restarts every state machine.
    private bool GraphOutOfDate(out AnimationGraph? asset)
    {
        asset = Graph.Res;
        if (asset.IsNotValid()) return _boundGraph != null && Graph.IsExplicitNull;
        if (!ReferenceEquals(asset, _boundGraph)) return true;
        if (_seenEdits == AnimationGraph.Edits) return false;

        if (asset!.DeepVersion == _boundVersion)
        {
            _seenEdits = AnimationGraph.Edits;
            return false;
        }
        return asset.SinceChanged >= SettleMilliseconds;
    }

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
        AnimationGraph? asset = Graph.Res;
        if (asset.IsValid()) asset!.Invalidate();
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

        if (Scene.IsNotValid()) return false;

        // The ragdoll stands where the feet are, so the probe would find it as the ground.
        var filter = new QueryFilter(GroundLayers);
        if (_ragdoll != null) filter = filter.Ignoring(_ragdoll.Bodies);

        if (!Scene!.Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, filter))
            return false;

        point = hit.Point;
        normal = hit.Normal;
        return true;
    }
}
