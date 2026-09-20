using System.Linq;
using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>
/// The runtime humanoid rig: the resolved binding of the standard humanoid bones onto a concrete
/// skeleton, plus the data retargeting needs (scale for proportion normalization, the bind body frame,
/// the per bone axis frames measured on the T pose and the muscle ranges). Built by <see cref="AvatarBuilder"/>
/// from a <see cref="HumanDescription"/>, which is copied so later edits to it do not affect the rig.
/// </summary>
public sealed class HumanoidRig
{
    private readonly Skeleton _skeleton;
    private readonly HumanDescription _description;
    private readonly HumanoidFrames _frames;
    private readonly int[] _boneIndices;
    private readonly int[] _humanBones;
    private readonly float[] _muscleMin;
    private readonly float[] _muscleMax;
    private readonly HumanBodyBone[] _presentParents;
    private readonly Float3 _mirrorNormal;
    private readonly int[] _mirrorBones;
    private readonly int[] _codecOrder;
    private readonly bool[] _bodyBones;

    internal HumanoidRig(Skeleton skeleton, HumanDescription description, HumanoidFrames frames)
    {
        _skeleton = skeleton;
        _description = description.Clone();
        _frames = frames;

        _boneIndices = new int[HumanTrait.BoneCount];
        foreach (HumanBodyBone bone in HumanTrait.AllBones)
            _boneIndices[(int)bone] = _description.GetSkeletonBoneIndex(bone);

        _humanBones = new int[skeleton.BoneCount];
        Array.Fill(_humanBones, -1);
        foreach (HumanBodyBone bone in HumanTrait.AllBones)
            if (HasBone(bone))
                _humanBones[_boneIndices[(int)bone]] = (int)bone;

        _presentParents = new HumanBodyBone[HumanTrait.BoneCount];
        foreach (HumanBodyBone bone in HumanTrait.AllBones)
        {
            HumanBodyBone? parent = HumanTrait.GetParentBone(bone);
            while (parent is not null && !HasBone(parent.Value))
                parent = HumanTrait.GetParentBone(parent.Value);
            _presentParents[(int)bone] = parent ?? HumanBodyBone.Hips;
        }

        _muscleMin = new float[HumanTrait.MuscleCount];
        _muscleMax = new float[HumanTrait.MuscleCount];
        for (int i = 0; i < _muscleMin.Length; i++)
            (_muscleMin[i], _muscleMax[i]) = _description.GetMuscleRange(i);

        ArmStretch = _description.ArmStretch;
        LegStretch = _description.LegStretch;
        UpperArmTwist = _description.UpperArmTwist;
        LowerArmTwist = _description.LowerArmTwist;
        UpperLegTwist = _description.UpperLegTwist;
        LowerLegTwist = _description.LowerLegTwist;
        FeetSpacing = _description.FeetSpacing;

        _mirrorNormal = Float3.Normalize(frames.BodyBind * new Float3(1f, 0f, 0f));
        _mirrorBones = PoseMirror.BuildMirrorBones(this);
        _codecOrder = BuildCodecOrder();
        _bodyBones = new bool[skeleton.BoneCount];
        foreach (int index in _codecOrder)
        {
            int parent = skeleton.SanitizedParentIndices[index];
            _bodyBones[index] = _humanBones[index] >= 0 || parent != Skeleton.InvalidIndex && _bodyBones[parent];
        }
    }

    public Skeleton Skeleton => _skeleton;

    /// <summary>A copy of the description this rig was built from. Editing it does not change the rig.</summary>
    public HumanDescription Description => _description.Clone();

    public bool HasBone(HumanBodyBone bone) => _boneIndices[(int)bone] != Skeleton.InvalidIndex;

    /// <summary>Skeleton bone index playing the humanoid bone, or <see cref="Skeleton.InvalidIndex"/>.</summary>
    public int GetSkeletonBoneIndex(HumanBodyBone bone) => _boneIndices[(int)bone];

    /// <summary>The id of the skeleton bone playing the humanoid bone.</summary>
    public StringID GetSkeletonBoneId(HumanBodyBone bone)
    {
        int index = _boneIndices[(int)bone];
        return index == Skeleton.InvalidIndex ? StringID.Invalid : _skeleton.GetBoneID(index);
    }

    /// <summary>
    /// Character scale used to normalize translations during retargeting: the height of the body's centre
    /// of mass above the floor in the T pose.
    /// </summary>
    public float Scale => _frames.Scale;

    // The leg length in the T pose. Goals are measured from the hip centre in units of it, so feet land the
    // same way on rigs whose centre of mass sits differently over the legs.
    internal float LegLength => _frames.LegLength;

    /// <summary>The body frame in the T pose: X points to the character's right, Y up and Z forward.</summary>
    public Quaternion BodyBindRotation => _frames.BodyBind;

    /// <summary>
    /// The bone's axis frame relative to the bone: the bone's model rotation times this gives a frame whose
    /// X axis runs down the bone, the frame the bone's muscles rotate in.
    /// </summary>
    public Quaternion GetAxisFrame(HumanBodyBone bone)
        => HasBone(bone) ? _frames.Post[(int)bone] : throw new KeyNotFoundException($"{bone} is not mapped.");

    /// <summary>The angle range in degrees this rig maps muscle values -1 and +1 to.</summary>
    public (float Min, float Max) GetMuscleRange(int muscle) => (_muscleMin[muscle], _muscleMax[muscle]);

    public float ArmStretch { get; }
    public float LegStretch { get; }
    public float UpperArmTwist { get; }
    public float LowerArmTwist { get; }
    public float UpperLegTwist { get; }
    public float LowerLegTwist { get; }
    public float FeetSpacing { get; }

    // The nearest mapped humanoid ancestor (the hips for the hips itself).
    internal HumanBodyBone GetPresentParent(HumanBodyBone bone) => _presentParents[(int)bone];

    // Parents first, with a mapped bone also after the bone it is measured against.
    private int[] BuildCodecOrder()
    {
        var order = new List<int>(_skeleton.BoneCount);
        var placed = new bool[_skeleton.BoneCount];
        void Place(int index)
        {
            if (index == Skeleton.InvalidIndex || placed[index])
                return;
            placed[index] = true;
            Place(_skeleton.SanitizedParentIndices[index]);
            if (TryGetHumanBone(index, out HumanBodyBone bone))
                Place(_frames.FrameParent[(int)bone]);
            order.Add(index);
        }
        foreach (int index in _skeleton.EvaluationOrder)
            Place(index);
        return order.ToArray();
    }

    internal int[] CodecOrder => _codecOrder;

    // The bones that turn with the body: the mapped ones and everything under them.
    internal bool[] BodyBones => _bodyBones;

    // Turns body values from the axes of a rig facing +Z into the rig's bind body frame, where mirroring happens.
    internal Quaternion FacingToBody => Quaternion.Normalize(Quaternion.Inverse(_frames.Facing) * _frames.BodyBind);

    // The skeleton bone a mapped bone's muscles are measured against.
    internal int GetFrameParent(HumanBodyBone bone) => _frames.FrameParent[(int)bone];

    // Each body segment's share of the mass, indexed by body bone.
    internal float[] SegmentMass => _frames.SegmentMass;

    // The floor point the body position is measured from, in model space.
    internal Float3 Floor => _frames.Floor;

    // The humanoid bone a skeleton bone plays, if any.
    internal bool TryGetHumanBone(int skeletonIndex, out HumanBodyBone bone)
    {
        int human = _humanBones[skeletonIndex];
        bone = (HumanBodyBone)Math.Max(human, 0);
        return human >= 0;
    }

    // The model space normal of the sagittal plane poses are mirrored across.
    internal Float3 MirrorNormal => _mirrorNormal;

    // The skeleton bone a skeleton bone mirrors onto, or Skeleton.InvalidIndex.
    internal int GetMirrorBoneIndex(int skeletonIndex) => _mirrorBones[skeletonIndex];

    // The yaw turning +Z onto the way the rig faces. Body values are stored relative to it.
    internal Quaternion Facing => _frames.Facing;

    internal Quaternion GetPre(HumanBodyBone bone) => _frames.Pre[(int)bone];

    internal Quaternion GetPost(HumanBodyBone bone) => _frames.Post[(int)bone];

    // The bone's local rotation in the T pose the rig was measured in.
    internal Quaternion GetTPoseLocal(int skeletonIndex) => _frames.TPoseLocal[skeletonIndex];

    internal float MuscleMin(int muscle) => _muscleMin[muscle];

    internal float MuscleMax(int muscle) => _muscleMax[muscle];
}
