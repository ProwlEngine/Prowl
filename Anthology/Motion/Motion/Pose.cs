using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// A skeletal pose: parent-space transforms (the authority) plus a lazily computed model-space
/// cache, a non-owning skeleton reference, and a <see cref="PoseState"/>.
/// </summary>
public sealed class Pose
{
    private readonly Skeleton _skeleton;
    private readonly Transform3D[] _local;
    private readonly Transform3D[] _model;
    private readonly float[] _floats;
    private ModelSpaceCache _cache;
    private PoseState _state;

    private enum ModelSpaceCache : byte { None, LowLod, All }

    /// <summary>Creates an unset pose sized for the given skeleton.</summary>
    public Pose(Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        _skeleton = skeleton;
        _local = new Transform3D[skeleton.BoneCount];
        _model = new Transform3D[skeleton.BoneCount];
        _floats = new float[skeleton.FloatChannelCount];
        _state = PoseState.Unset;
    }

    public Skeleton Skeleton => _skeleton;

    public int BoneCount => _local.Length;

    public PoseState State => _state;

    public bool IsValid => _state != PoseState.Unset;

    /// <summary>True if the model-space cache is currently valid.</summary>
    public bool HasModelSpaceTransforms => _cache == ModelSpaceCache.All;

    /// <summary>The parent-space transforms (the authoritative data).</summary>
    public IReadOnlyList<Transform3D> Transforms => _local;

    /// <summary>Gets a bone's parent-space (local) transform.</summary>
    public Transform3D GetTransform(int boneIndex) => _local[boneIndex];

    /// <summary>Number of scalar channels, matching the skeleton.</summary>
    public int FloatChannelCount => _floats.Length;

    /// <summary>The scalar channel values carried alongside the bones.</summary>
    public IReadOnlyList<float> Floats => _floats;

    public float GetFloat(int channelIndex) => _floats[channelIndex];

    public void SetFloat(int channelIndex, float value) => _floats[channelIndex] = value;

    /// <summary>
    /// Sets a bone's parent space transform. The pose becomes a regular <see cref="PoseState.Pose"/>,
    /// or stays additive when it was additive or a zero pose.
    /// </summary>
    public void SetTransform(int boneIndex, Transform3D transform)
    {
        _local[boneIndex] = transform;
        _state = _state is PoseState.AdditivePose or PoseState.ZeroPose ? PoseState.AdditivePose : PoseState.Pose;
        _cache = ModelSpaceCache.None;
    }

    /// <summary>Gets a bone's model-space (global) transform, computing the cache on demand.</summary>
    public Transform3D GetModelSpaceTransform(int boneIndex)
    {
        bool cached = _cache == ModelSpaceCache.All || (_cache == ModelSpaceCache.LowLod && _skeleton.IsInLowLodSet(boneIndex));
        if (!cached)
            CalculateModelSpaceTransforms();
        return _model[boneIndex];
    }

    /// <summary>Computes the model-space cache from the parent-space transforms.</summary>
    public void CalculateModelSpaceTransforms()
    {
        TransformOps.ComputeModelSpace(_local, _skeleton.EvaluationOrder, _skeleton.SanitizedParentIndices, _model);
        _cache = ModelSpaceCache.All;
    }

    /// <summary>
    /// Computes the model-space cache for only the bones at the given LOD (plus their ancestors), so
    /// low LOD characters can skip computing distal bones. Reading a bone outside that set later
    /// computes the full cache on demand.
    /// </summary>
    public void CalculateModelSpaceTransforms(SkeletonLOD lod)
    {
        if (lod == SkeletonLOD.High)
        {
            CalculateModelSpaceTransforms();
            return;
        }
        TransformOps.ComputeModelSpace(_local, _skeleton.LowLodEvaluationOrder, _skeleton.SanitizedParentIndices, _model);
        _cache = ModelSpaceCache.LowLod;
    }

    /// <summary>Invalidates the model-space cache.</summary>
    public void ClearModelSpaceTransforms() => _cache = ModelSpaceCache.None;

    /// <summary>Sets the pose to the skeleton's reference pose.</summary>
    public void SetToReferencePose(bool calculateModelSpace = true)
    {
        IReadOnlyList<Transform3D> reference = _skeleton.ParentSpaceReferencePose;
        for (int i = 0; i < _local.Length; i++)
            _local[i] = reference[i];
        Array.Clear(_floats);

        _state = PoseState.ReferencePose;
        _cache = ModelSpaceCache.None;
        if (calculateModelSpace)
            CalculateModelSpaceTransforms();
    }

    /// <summary>Sets every bone to the additive identity: no rotation, no translation and a zero scale delta.</summary>
    public void SetToZeroPose(bool calculateModelSpace = true)
    {
        var zero = new Transform3D(Float3.Zero, Quaternion.Identity, Float3.Zero);
        for (int i = 0; i < _local.Length; i++)
            _local[i] = zero;
        Array.Clear(_floats);

        _state = PoseState.ZeroPose;
        _cache = ModelSpaceCache.None;
        if (calculateModelSpace)
            CalculateModelSpaceTransforms();
    }

    /// <summary>Resets to the unset state.</summary>
    public void Reset()
    {
        _state = PoseState.Unset;
    }

    /// <summary>Copies all transforms and state from another pose with the same skeleton.</summary>
    public void CopyFrom(Pose other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other._local.Length != _local.Length)
            throw new ArgumentException("Pose bone counts must match.", nameof(other));

        Array.Copy(other._local, _local, _local.Length);

        // Channel counts may differ; anything the source does not have reads as unset, never as stale.
        int shared = Math.Min(other._floats.Length, _floats.Length);
        Array.Copy(other._floats, _floats, shared);
        if (shared < _floats.Length)
            Array.Clear(_floats, shared, _floats.Length - shared);
        _state = other._state;
        _cache = other._cache;
        if (_cache != ModelSpaceCache.None)
            Array.Copy(other._model, _model, _model.Length);
    }

    // Internal bulk-write helpers used by the clip sampler and the blender (no per-bone state churn).
    internal void WriteLocal(int boneIndex, Transform3D transform) => _local[boneIndex] = transform;

    internal void WriteFloat(int channelIndex, float value) => _floats[channelIndex] = value;

    internal float[] FloatsArray => _floats;

    internal void FinishWrite(PoseState state)
    {
        _state = state;
        _cache = ModelSpaceCache.None;
    }
}
