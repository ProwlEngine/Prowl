// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Echo;
using Prowl.Motion;

using MotionAvatar = Prowl.Motion.Avatar;
using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime;

/// <summary>Whether a rig is played as authored, or through the humanoid muscle space.</summary>
public enum AvatarRigType
{
    /// <summary>Bones are driven as the file names them. Clips only play on this rig.</summary>
    Generic,
    /// <summary>Bones are mapped to the human body, so clips play on any humanoid rig.</summary>
    Humanoid,
}

/// <summary>
/// A rig: the skeleton a model was authored with, plus an optional humanoid mapping. The Motion avatar
/// is built from them on first use.
/// </summary>
public sealed class Avatar : Asset, ISerializable
{
    private MotionSkeleton? _skeleton;
    private HumanDescription? _description;
    private AvatarRigType _rigType;
    private int _rootBoneIndex;

    [NonSerialized] private MotionAvatar? _runtime;
    [NonSerialized] private bool _buildFailed;

    public Avatar() : base("Avatar") { }

    /// <summary>The skeleton the rig describes.</summary>
    public MotionSkeleton? Skeleton { get { EnsureLoaded(); return _skeleton; } }

    public AvatarRigType RigType { get { EnsureLoaded(); return _rigType; } }

    /// <summary>The bone the rig hangs from, which root motion is measured against.</summary>
    public int RootBoneIndex { get { EnsureLoaded(); return _rootBoneIndex; } }

    /// <summary>The human body mapping, or null on a generic rig. Call <see cref="Invalidate"/> after editing it.</summary>
    public HumanDescription? Description { get { EnsureLoaded(); return _description; } }

    /// <summary>True once a humanoid rig has mapped successfully.</summary>
    public bool IsHuman => Runtime is { IsHuman: true };

    /// <summary>The Motion avatar, built on first use. A humanoid whose mapping fails is built as a generic rig. Null without a skeleton.</summary>
    public MotionAvatar? Runtime
    {
        get
        {
            EnsureLoaded();
            if (_runtime != null || _skeleton == null || _buildFailed)
                return _runtime;

            if (_rigType == AvatarRigType.Humanoid && _description != null)
            {
                try
                {
                    _runtime = AvatarBuilder.BuildHumanoid(_skeleton, _description);
                    return _runtime;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Avatar] '{Name}' has a humanoid mapping that does not work, so it plays as a generic rig: {ex.Message}");
                }
            }

            try
            {
                _runtime = AvatarBuilder.BuildGeneric(_skeleton, _rootBoneIndex);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Avatar] '{Name}' could not be built: {ex.Message}");
                _buildFailed = true;
            }
            return _runtime;
        }
    }

    /// <summary>Drops the built avatar so the next use rebuilds it from the current description.</summary>
    public void Invalidate() { EnsureLoaded(); _runtime = null; _buildFailed = false; }

    public static Avatar CreateGeneric(MotionSkeleton skeleton, int rootBoneIndex = 0, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        return new Avatar
        {
            Name = name ?? "Avatar",
            _skeleton = skeleton,
            _rigType = AvatarRigType.Generic,
            _rootBoneIndex = rootBoneIndex,
        };
    }

    public static Avatar CreateHumanoid(MotionSkeleton skeleton, HumanDescription description, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(description);
        return new Avatar
        {
            Name = name ?? "Avatar",
            _skeleton = skeleton,
            _rigType = AvatarRigType.Humanoid,
            _description = description,
        };
    }

    /// <summary>Maps the skeleton to the human body by bone name and shape, reporting what it could not match.</summary>
    public static Avatar CreateAutomatic(MotionSkeleton skeleton, out HumanoidMapResult report, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        MotionAvatar built = AvatarBuilder.BuildAutomatic(skeleton, out report);
        return new Avatar
        {
            Name = name ?? "Avatar",
            _skeleton = skeleton,
            _rigType = built.IsHuman ? AvatarRigType.Humanoid : AvatarRigType.Generic,
            _description = built.Humanoid?.Description,
            _runtime = built,
        };
    }

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        SerializeHeader(value);
        value.Add("RigType", new EchoObject((int)_rigType));
        value.Add("RootBone", new EchoObject(_rootBoneIndex));

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(_skeleton != null);
            if (_skeleton != null) MotionBinary.Write(writer, _skeleton);
            writer.Write(_description != null);
            if (_description != null) MotionBinary.Write(writer, _description);
        }
        value.Add("Rig", new EchoObject(stream.ToArray()));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        DeserializeHeader(value);
        _rigType = (AvatarRigType)(value.Get("RigType")?.IntValue ?? 0);
        _rootBoneIndex = value.Get("RootBone")?.IntValue ?? 0;
        _runtime = null;

        byte[]? rig = value.Get("Rig")?.ByteArrayValue;
        if (rig == null || rig.Length == 0) return;

        using var stream = new MemoryStream(rig);
        using var reader = new BinaryReader(stream);
        _skeleton = reader.ReadBoolean() ? MotionBinary.ReadSkeleton(reader) : null;
        _description = reader.ReadBoolean() ? MotionBinary.ReadHumanDescription(reader) : null;
    }
}
