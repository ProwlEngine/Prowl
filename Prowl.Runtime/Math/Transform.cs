// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Vector;

public class Transform
{
    #region Properties

    #region Position
    public Float3 Position
    {
        get
        {
            if (Parent != null)
                return MakeSafe(Float4x4.TransformPoint(_localPosition, Parent.LocalToWorldMatrix));
            else
                return MakeSafe(_localPosition);
        }
        set
        {
            Float3 newPosition = value;
            if (Parent != null)
                newPosition = Parent.InverseTransformPoint(newPosition);

            if (!_localPosition.Equals(newPosition))
            {
                _localPosition = MakeSafe(newPosition);
                _version++;
            }
        }
    }

    public Float3 LocalPosition
    {
        get => MakeSafe(_localPosition);
        set
        {
            if (!_localPosition.Equals(value))
            {
                _localPosition = MakeSafe(value);
                _version++;
            }
        }
    }
    #endregion

    #region Rotation
    public Quaternion Rotation
    {
        get
        {
            Quaternion worldRot = _localRotation;
            Transform p = Parent;
            while (p != null)
            {
                worldRot = p._localRotation * worldRot;
                p = p.Parent;
            }
            return MakeSafe(worldRot);
        }
        set
        {
            Quaternion newVale;
            if (Parent != null)
                newVale = MakeSafe(Quaternion.NormalizeSafe(Quaternion.Inverse(Parent.Rotation) * value));
            else
                newVale = MakeSafe(Quaternion.NormalizeSafe(value));

            if (LocalRotation != newVale)
            {
                LocalRotation = newVale;
            }
        }
    }

    public Quaternion LocalRotation
    {
        get => MakeSafe(_localRotation);
        set
        {
            if (_localRotation != value)
            {
                _localRotation = MakeSafe(value);
                _version++;
            }
        }
    }

    public Float3 EulerAngles
    {
        get => MakeSafe(Rotation.EulerAngles);
        set
        {
            Rotation = MakeSafe(Quaternion.FromEuler(value));
        }
    }

    public Float3 LocalEulerAngles
    {
        get => MakeSafe(_localRotation.EulerAngles);
        set
        {
            _localRotation = MakeSafe(Quaternion.FromEuler(value));
            _version++;
        }
    }
    #endregion

    #region Scale

    public Float3 LocalScale
    {
        get => MakeSafe(_localScale);
        set
        {
            if (!_localScale.Equals(value))
            {
                _localScale = MakeSafe(value);
                _version++;
            }
        }
    }

    public Float3 LossyScale
    {
        get
        {
            Float3 scale = LocalScale;
            Transform p = Parent;
            while (p != null)
            {
                scale = p.LocalScale * scale;
                p = p.Parent;
            }
            return MakeSafe(scale);
        }
    }

    #endregion

    public Float3 Right { get => Rotation * Float3.UnitX; }     // TODO: Setter
    public Float3 Up { get => Rotation * Float3.UnitY; }           // TODO: Setter
    public Float3 Forward { get => Rotation * Float3.UnitZ; } // TODO: Setter

    public Float4x4 WorldToLocalMatrix => LocalToWorldMatrix.Invert();

    public Float4x4 LocalToWorldMatrix
    {
        get
        {
            Float4x4 t = Float4x4.CreateTRS(_localPosition, _localRotation, _localScale);
            return Parent != null ? (Parent.LocalToWorldMatrix * t) : t;
        }
    }

    public Transform Parent
    {
        get => GameObject?.Parent?.Transform;
        set => GameObject?.SetParent(value?.GameObject, true);
    }

    // https://forum.unity.com/threads/transform-haschanged-would-be-better-if-replaced-by-a-version-number.700004/
    // Replacement for hasChanged
    public uint Version
    {
        get => _version;
        set => _version = value;
    }

    public Transform Root => Parent == null ? this : Parent.Root;


    #endregion

    #region Fields

    [SerializeField] Float3 _localPosition;
    [SerializeField] Float3 _localScale = Float3.One;
    [SerializeField] Quaternion _localRotation = Quaternion.Identity;

    [SerializeIgnore]
    uint _version = 1;

    public GameObject GameObject { get; internal set; }
    #endregion

    public void SetLocalTransform(Float3 position, Quaternion rotation, Float3 scale)
    {
        _localPosition = position;
        _localRotation = rotation;
        _localScale = scale;
        _version++;
    }

    private float MakeSafe(float v) => float.IsNaN(v) ? 0 : v;
    private Float3 MakeSafe(Float3 v) => new(MakeSafe(v.X), MakeSafe(v.Y), MakeSafe(v.Z));
    private Quaternion MakeSafe(Quaternion v) => new(MakeSafe(v.X), MakeSafe(v.Y), MakeSafe(v.Z), MakeSafe(v.W));

    public Transform? Find(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path, nameof(path));

        string[] names = path.Split('/');
        Transform currentTransform = this;

        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));

            Transform? childTransform = FindImmediateChild(currentTransform, name);
            if (childTransform == null)
                return null;

            currentTransform = childTransform;
        }

        return currentTransform;
    }

    private Transform? FindImmediateChild(Transform parent, string name)
    {
        foreach (GameObject child in parent.GameObject.Children)
            if (child.Name == name)
                return child.Transform;
        return null;
    }

    public Transform? DeepFind(string name)
    {
        if (name == null) return null;
        if (name == GameObject.Name) return this;
        foreach (GameObject child in GameObject.Children)
        {
            Transform? t = child.Transform.DeepFind(name);
            if (t != null) return t;
        }
        return null;
    }

    public static string GetPath(Transform target, Transform root)
    {
        string path = target.GameObject.Name;
        while (target.Parent != null)
        {
            target = target.Parent;
            path = target.GameObject.Name + "/" + path;
            if (target == root)
                break;
        }
        return path;
    }

    /// <summary>
    /// Gets the path from root to target, excluding root's own name.
    /// E.g. if root is "Model" and target is "Model/Armature/Hips/Spine",
    /// returns "Armature/Hips/Spine". Compatible with Transform.Find().
    /// </summary>
    public static string GetRelativePath(Transform target, Transform root)
    {
        if (target == root) return "";

        var parts = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            parts.Add(current.GameObject.Name);
            current = current.Parent;
        }

        if (current != root) return target.GameObject.Name; // fallback: not a descendant

        parts.Reverse();
        return string.Join("/", parts);
    }

    public void Translate(Float3 translation, Transform? relativeTo = null)
    {
        if (relativeTo != null)
            Position += relativeTo.TransformDirection(translation);
        else
            Position += translation;
    }

    public void Rotate(Float3 eulerAngles, bool relativeToSelf = true)
    {
        Quaternion eulerRot = Quaternion.FromEuler(eulerAngles);
        if (relativeToSelf)
            LocalRotation *= eulerRot;
        else
            Rotation *= (Quaternion.Inverse(Rotation) * eulerRot * Rotation);
    }

    public void Rotate(Float3 axis, float angle, bool relativeToSelf = true)
    {
        RotateAroundInternal(relativeToSelf ? TransformDirection(axis) : axis, angle * Maths.Deg2Rad);
    }

    public void RotateAround(Float3 point, Float3 axis, float angle)
    {
        Float3 worldPos = Position;
        Quaternion q = Quaternion.AxisAngle(axis, angle);
        Float3 dif = worldPos - point;
        dif = q * dif;
        worldPos = point + dif;
        Position = worldPos;
        RotateAroundInternal(axis, angle * Maths.Deg2Rad);
    }

    internal void RotateAroundInternal(Float3 worldAxis, float rad)
    {
        Float3 localAxis = InverseTransformDirection(worldAxis);
        if (Float3.LengthSquared(localAxis) > float.Epsilon)
        {
            localAxis = Float3.Normalize(localAxis);
            Quaternion q = Quaternion.AxisAngle(localAxis, rad);
            _localRotation = Quaternion.NormalizeSafe(_localRotation * q);
        }
    }


    #region Transform

    public Float3 TransformPoint(Float3 inPosition) => Float4x4.TransformPoint(new Float4(inPosition, 1.0f), LocalToWorldMatrix).XYZ;
    public Float3 InverseTransformPoint(Float3 inPosition) => Float4x4.TransformPoint(new Float4(inPosition, 1.0f), WorldToLocalMatrix).XYZ;
    public Quaternion InverseTransformRotation(Quaternion worldRotation) => Quaternion.Inverse(Rotation) * worldRotation;

    public Float3 TransformDirection(Float3 inDirection) => Rotation * inDirection;
    public Float3 InverseTransformDirection(Float3 inDirection) => Quaternion.Inverse(Rotation) * inDirection;

    public Float3 TransformVector(Float3 inVector)
    {
        Float3 worldVector = inVector;

        Transform cur = this;
        while (cur != null)
        {
            worldVector *= cur._localScale;
            worldVector = cur._localRotation * worldVector;

            cur = cur.Parent;
        }
        return worldVector;
    }
    public Float3 InverseTransformVector(Float3 inVector)
    {
        Float3 newVector, localVector;
        if (Parent != null)
            localVector = Parent.InverseTransformVector(inVector);
        else
            localVector = inVector;

        newVector = Quaternion.Inverse(_localRotation) * localVector;
        if (!_localScale.Equals(Float3.One))
            newVector *= InverseSafe(_localScale);

        return newVector;
    }

    public Quaternion TransformRotation(Quaternion inRotation)
    {
        Quaternion worldRotation = inRotation;

        Transform cur = this;
        while (cur != null)
        {
            worldRotation = cur._localRotation * worldRotation;
            cur = cur.Parent;
        }
        return worldRotation;
    }

    #endregion

    public Float4x4 GetWorldRotationAndScale()
    {
        Float4x4 ret = Float4x4.CreateTRS(new Float3(0, 0, 0), _localRotation, _localScale);
        if (Parent != null)
        {
            Float4x4 parentTransform = Parent.GetWorldRotationAndScale();
            ret = (parentTransform * ret);
        }
        return ret;
    }

    static float InverseSafe(float f) => Maths.Abs(f) > float.Epsilon ? 1.0f / f : 0.0f;
    static Float3 InverseSafe(Float3 v) => new(InverseSafe(v.X), InverseSafe(v.Y), InverseSafe(v.Z));
}
