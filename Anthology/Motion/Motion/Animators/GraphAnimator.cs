using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// An animator driven by an <see cref="AnimationGraph"/> (state machines, blends, IK, sub-graphs).
/// Set control parameters, then <see cref="AnimatorBase.Update"/> evaluates the graph and pushes the
/// pose to the engine. Subclass it and implement the <see cref="AnimatorBase"/> hooks to bind it.
/// </summary>
public abstract class GraphAnimator : AnimatorBase
{
    private readonly AnimationGraphInstance _instance;

    /// <summary>Creates an animator from a graph bound to a plain skeleton.</summary>
    protected GraphAnimator(AnimationGraph graph, Skeleton skeleton) : base(skeleton, null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _instance = graph.CreateInstance(skeleton);
    }

    /// <summary>Creates an animator from a graph bound to an avatar (enables humanoid nodes and foot IK).</summary>
    protected GraphAnimator(AnimationGraph graph, Avatar avatar) : base(avatar?.Skeleton!, avatar)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(avatar);
        _instance = graph.CreateInstance(avatar);
    }

    /// <summary>The underlying graph instance (for advanced access / inspection).</summary>
    public AnimationGraphInstance Graph => _instance;

    /// <summary>Events sampled during the most recent update.</summary>
    public SampledEventsBuffer Events => _instance.Events;

    public void SetFloat(string name, float value) => _instance.SetFloat(name, value);
    public void SetBool(string name, bool value) => _instance.SetBool(name, value);
    public void SetInt(string name, int value) => _instance.SetInt(name, value);
    public void SetVector(string name, Float3 value) => _instance.SetVector(name, value);
    public void SetTarget(string name, Target value) => _instance.SetTarget(name, value);
    public void SetId(string name, StringID value) => _instance.SetId(name, value);

    public float GetFloat(string name) => _instance.GetFloat(name);
    public bool GetBool(string name) => _instance.GetBool(name);
    public int GetInt(string name) => _instance.GetInt(name);

    /// <summary>Plugs a runtime graph into a named external slot (or clears it with null).</summary>
    public void SetExternalGraph(string slotName, AnimationGraphInstance? graph) => _instance.SetExternalGraph(slotName, graph);

    protected override void Evaluate(float scaledDeltaTime, out Transform3D rootMotionDelta)
    {
        _instance.Update(scaledDeltaTime, RootWorldTransform);
        Pose.CopyFrom(_instance.Pose);
        rootMotionDelta = _instance.RootMotionDelta;
    }
}
