// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Vector;

public class Transform
{
    #region Properties

    #region Position
    public Double3 position
    {
        get
        {
            if (_isWorldPosDirty)
            {
                if (parent != null)
                    _cachedWorldPosition = Maths.TransformPoint(m_LocalPosition, parent.localToWorldMatrix);
                else
                    _cachedWorldPosition = m_LocalPosition;
                _isWorldPosDirty = false;
            }
            return MakeSafe(_cachedWorldPosition);
        }
        set
        {
            Double3 newPosition = value;
            if (parent != null)
                newPosition = parent.InverseTransformPoint(newPosition);

            if (!m_LocalPosition.Equals(newPosition))
            {
                m_LocalPosition = MakeSafe(newPosition);
                InvalidateTransformCache();
            }
        }
    }

    public Double3 localPosition
    {
        get => MakeSafe(m_LocalPosition);
        set
        {
            if (!m_LocalPosition.Equals(value))
            {
                m_LocalPosition = MakeSafe(value);
                InvalidateTransformCache();
            }
        }
    }
    #endregion

    #region Rotation
    public Quaternion rotation
    {
        get
        {
            if (_isWorldRotDirty)
            {
                Quaternion worldRot = m_LocalRotation;
                Transform p = parent;
                while (p != null)
                {
                    worldRot = p.m_LocalRotation * worldRot;
                    p = p.parent;
                }
                _cachedWorldRotation = worldRot;
                _isWorldRotDirty = false;
            }
            return MakeSafe(_cachedWorldRotation);
        }
        set
        {
            var newVale = Quaternion.Identity;
            if (parent != null)
                newVale = MakeSafe(Maths.NormalizeSafe(Maths.Inverse(parent.rotation) * value));
            else
                newVale = MakeSafe(Maths.NormalizeSafe(value));
            if(localRotation != newVale)
            {
                localRotation = newVale;
                InvalidateTransformCache();
            }
        }
    }

    public Quaternion localRotation
    {
        get => MakeSafe(m_LocalRotation);
        set
        {

            if (m_LocalRotation != value)
            {
                m_LocalRotation = MakeSafe(value);
                InvalidateTransformCache();
            }
        }
    }

    public Double3 eulerAngles
    {
        get => MakeSafe(rotation.eulerAngles);
        set
        {
            rotation = MakeSafe(Quaternion.FromEuler((Float3)value));
            InvalidateTransformCache();
        }
    }

    public Double3 localEulerAngles
    {
        get => MakeSafe(m_LocalRotation.eulerAngles);
        set
        {
            m_LocalRotation.eulerAngles = (Float3)MakeSafe(value);
            InvalidateTransformCache();
        }
    }
    #endregion

    #region Scale

    public Double3 localScale
    {
        get => MakeSafe(m_LocalScale);
        set
        {
            if (!m_LocalScale.Equals(value))
            {
                m_LocalScale = MakeSafe(value);
                InvalidateTransformCache();
            }
        }
    }

    public Double3 lossyScale
    {
        get
        {
            if (_isWorldScaleDirty)
            {
                Double3 scale = localScale;
                Transform p = parent;
                while (p != null)
                {
                    scale = p.localScale * scale;
                    p = p.parent;
                }
                _cachedLossyScale = scale;
                _isWorldScaleDirty = false;
            }
            return MakeSafe(_cachedLossyScale);
        }
    }

    #endregion

    public Double3 right { get => rotation * Float3.UnitX; }     // TODO: Setter
    public Double3 up { get => rotation * Float3.UnitY; }           // TODO: Setter
    public Double3 forward { get => rotation * Float3.UnitZ; } // TODO: Setter

    public Double4x4 worldToLocalMatrix => localToWorldMatrix.Invert();

    public Double4x4 localToWorldMatrix
    {
        get
        {
            if (_isLocalToWorldMatrixDirty)
            {
                Double4x4 t = Double4x4.CreateTRS(m_LocalPosition, m_LocalRotation, m_LocalScale);
                _cachedLocalToWorldMatrix = parent != null ? Maths.Mul(parent.localToWorldMatrix, t) : t;
                _isLocalToWorldMatrixDirty = false;
            }
            return _cachedLocalToWorldMatrix;
            //Matrix4x4 t = Matrix4x4.TRS(m_LocalPosition, m_LocalRotation, m_LocalScale);
            //return parent != null ? parent.localToWorldMatrix * t : t;
        }
    }

    public Transform parent
    {
        get => gameObject?.parent?.Transform;
        set => gameObject?.SetParent(value.gameObject, true);
    }

    // https://forum.unity.com/threads/transform-haschanged-would-be-better-if-replaced-by-a-version-number.700004/
    // Replacement for hasChanged
    public uint version
    {
        get => _version;
        set => _version = value;
    }

    public Transform root => parent == null ? this : parent.root;


    #endregion

    #region Fields

    [SerializeField] Double3 m_LocalPosition;
    [SerializeField] Double3 m_LocalScale = Double3.One;
    [SerializeField] Quaternion m_LocalRotation = Quaternion.Identity;

    [SerializeIgnore]
    uint _version = 1;

    [SerializeIgnore] private Double3 _cachedWorldPosition;
    [SerializeIgnore] private Quaternion _cachedWorldRotation;
    [SerializeIgnore] private Double3 _cachedLossyScale;
    [SerializeIgnore] private Double4x4 _cachedLocalToWorldMatrix;
    [SerializeIgnore] private bool _isLocalToWorldMatrixDirty = true;
    [SerializeIgnore] private bool _isWorldPosDirty = true;
    [SerializeIgnore] private bool _isWorldRotDirty = true;
    [SerializeIgnore] private bool _isWorldScaleDirty = true;

    public GameObject gameObject { get; internal set; }
    #endregion

    private void InvalidateTransformCache()
    {
        _isLocalToWorldMatrixDirty = true;
        _isWorldPosDirty = true;
        _isWorldRotDirty = true;
        _isWorldScaleDirty = true;
        _version++;

        // Invalidate children
        foreach (GameObject child in gameObject.children)
            child.Transform.InvalidateTransformCache();
    }

    public void SetLocalTransform(Double3 position, Quaternion rotation, Double3 scale)
    {
        m_LocalPosition = position;
        m_LocalRotation = rotation;
        m_LocalScale = scale;
        InvalidateTransformCache();
    }

    private double MakeSafe(double v) => double.IsNaN(v) ? 0 : v;
    private Double3 MakeSafe(Double3 v) => new Double3(MakeSafe(v.X), MakeSafe(v.Y), MakeSafe(v.Z));
    private Quaternion MakeSafe(Quaternion v) => new Quaternion((float)MakeSafe(v.X), (float)MakeSafe(v.Y), (float)MakeSafe(v.Z), (float)MakeSafe(v.W));

    public Transform? Find(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path, nameof(path));

        var names = path.Split('/');
        var currentTransform = this;

        foreach (var name in names)
        {
            ArgumentException.ThrowIfNullOrEmpty(path, nameof(path));

            var childTransform = FindImmediateChild(currentTransform, name);
            if (childTransform == null)
                return null;

            currentTransform = childTransform;
        }

        return currentTransform;
    }

    private Transform? FindImmediateChild(Transform parent, string name)
    {
        foreach (var child in parent.gameObject.children)
            if (child.Name == name)
                return child.Transform;
        return null;
    }

    public Transform? DeepFind(string name)
    {
        if (name == null) return null;
        if (name == gameObject.Name) return this;
        foreach (var child in gameObject.children)
        {
            var t = child.Transform.DeepFind(name);
            if (t != null) return t;
        }
        return null;
    }

    public static string GetPath(Transform target, Transform root)
    {
        string path = target.gameObject.Name;
        while (target.parent != null)
        {
            target = target.parent;
            path = target.gameObject.Name + "/" + path;
            if (target == root)
                break;
        }
        return path;
    }

    public void Translate(Double3 translation, Transform? relativeTo = null)
    {
        if (relativeTo != null)
            position += relativeTo.TransformDirection(translation);
        else
            position += translation;
    }

    public void Rotate(Double3 eulerAngles, bool relativeToSelf = true)
    {
        Quaternion eulerRot = Quaternion.FromEuler((Float3)eulerAngles);
        if (relativeToSelf)
            localRotation = localRotation * eulerRot;
        else
            rotation = rotation * (Maths.Inverse(rotation) * eulerRot * rotation);
    }

    public void Rotate(Double3 axis, double angle, bool relativeToSelf = true)
    {
        RotateAroundInternal(relativeToSelf ? TransformDirection(axis) : axis, angle * Maths.Deg2Rad);
    }

    public void RotateAround(Double3 point, Double3 axis, double angle)
    {
        Double3 worldPos = position;
        Quaternion q = Maths.AxisAngle((Float3)axis, (float)angle);
        Double3 dif = worldPos - point;
        dif = q * (Float3)dif;
        worldPos = point + dif;
        position = worldPos;
        RotateAroundInternal(axis, angle * Maths.Deg2Rad);
    }

    internal void RotateAroundInternal(Double3 worldAxis, double rad)
    {
        Double3 localAxis = InverseTransformDirection(worldAxis);
        if (localAxis.LengthSquared > double.Epsilon)
        {
            localAxis = Maths.Normalize(localAxis);
            Quaternion q = Maths.AxisAngle((Float3)localAxis, (float)rad);
            m_LocalRotation = Maths.NormalizeSafe(m_LocalRotation * q);
        }
    }


    #region Transform

    public Double3 TransformPoint(Double3 inPosition) => Maths.TransformPoint(new Double4(inPosition, 1.0), localToWorldMatrix).XYZ;
    public Double3 InverseTransformPoint(Double3 inPosition) => Maths.TransformPoint(new Double4(inPosition, 1.0), worldToLocalMatrix).XYZ;
    public Quaternion InverseTransformRotation(Quaternion worldRotation) => Maths.Inverse(rotation) * worldRotation;

    public Double3 TransformDirection(Double3 inDirection) => rotation * (Float3)inDirection;
    public Double3 InverseTransformDirection(Double3 inDirection) => Maths.Inverse(rotation) * (Float3)inDirection;

    public Double3 TransformVector(Double3 inVector)
    {
        Double3 worldVector = inVector;

        Transform cur = this;
        while (cur != null)
        {
            worldVector = worldVector * cur.m_LocalScale;
            worldVector = cur.m_LocalRotation * (Float3)worldVector;

            cur = cur.parent;
        }
        return worldVector;
    }
    public Double3 InverseTransformVector(Double3 inVector)
    {
        Double3 newVector, localVector;
        if (parent != null)
            localVector = parent.InverseTransformVector(inVector);
        else
            localVector = inVector;

        newVector = Maths.Inverse(m_LocalRotation) * (Float3)localVector;
        if (!m_LocalScale.Equals(Double3.One))
            newVector = newVector * InverseSafe(m_LocalScale);

        return newVector;
    }

    public Quaternion TransformRotation(Quaternion inRotation)
    {
        Quaternion worldRotation = inRotation;

        Transform cur = this;
        while (cur != null)
        {
            worldRotation = cur.m_LocalRotation * worldRotation;
            cur = cur.parent;
        }
        return worldRotation;
    }

    #endregion

    public Double4x4 GetWorldRotationAndScale()
    {
        Double4x4 ret = Double4x4.CreateTRS(new Double3(0, 0, 0), m_LocalRotation, m_LocalScale);
        if (parent != null)
        {
            Double4x4 parentTransform = parent.GetWorldRotationAndScale();
            ret = Maths.Mul(parentTransform, ret);
        }
        return ret;
    }

    static double InverseSafe(double f) => Maths.Abs(f) > double.Epsilon ? 1.0F / f : 0.0F;
    static Double3 InverseSafe(Double3 v) => new Double3(InverseSafe(v.X), InverseSafe(v.Y), InverseSafe(v.Z));
}
