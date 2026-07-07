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

    public Float3 Right
    {
        get => Rotation * Float3.UnitX;
        set => Rotation = FromToRotation(Float3.UnitX, value) * Rotation;
    }

    public Float3 Up
    {
        get => Rotation * Float3.UnitY;
        set => Rotation = FromToRotation(Float3.UnitY, value) * Rotation;
    }

    /// <summary>
    /// Facing direction (local +Z). Assigning rotates the transform so Forward points along
    /// the new direction. Up is kept close to world +Y when possible.
    /// </summary>
    public Float3 Forward
    {
        get => Rotation * Float3.UnitZ;
        set
        {
            if (Float3.LengthSquared(value) < float.Epsilon) return;
            Rotation = Quaternion.LookRotation(Float3.Normalize(value), Float3.UnitY);
        }
    }

    /// <summary>
    /// Shortest-arc rotation that maps <paramref name="from"/> onto <paramref name="to"/>.
    /// Both vectors are normalized; returns identity if either is zero or they already align.
    /// </summary>
    private static Quaternion FromToRotation(Float3 from, Float3 to)
    {
        if (Float3.LengthSquared(from) < float.Epsilon || Float3.LengthSquared(to) < float.Epsilon)
            return Quaternion.Identity;

        Float3 a = Float3.Normalize(from);
        Float3 b = Float3.Normalize(to);
        float d = Float3.Dot(a, b);
        if (d >= 1f - float.Epsilon) return Quaternion.Identity;
        if (d <= -1f + float.Epsilon)
        {
            // 180° pick any perpendicular axis
            Float3 axis = Float3.Cross(a, Float3.UnitX);
            if (Float3.LengthSquared(axis) < float.Epsilon)
                axis = Float3.Cross(a, Float3.UnitY);
            return Quaternion.AxisAngle(Float3.Normalize(axis), MathF.PI);
        }
        Float3 c = Float3.Cross(a, b);
        return Quaternion.NormalizeSafe(new Quaternion(c.X, c.Y, c.Z, 1f + d));
    }

    public virtual Float4x4 WorldToLocalMatrix => LocalToWorldMatrix.Invert();

    /// <summary>
    /// World matrix of this transform. Lazily cached: it only rebuilds when this transform's
    /// <see cref="Version"/> changes or an ancestor's world matrix changes, so an unmoving object
    /// walks its ancestor chain doing cheap version comparisons instead of rebuilding matrices.
    /// Every rebuild bumps <see cref="_worldVersion"/> so descendants know to invalidate.
    /// Not thread-safe: transforms are expected to be read on a single thread per frame.
    /// </summary>
    public virtual Float4x4 LocalToWorldMatrix
    {
        get
        {
            Transform parent = Parent;
            if (parent == null)
            {
                if (_cachedLocalVersion != _version)
                {
                    _worldCache = Float4x4.CreateTRS(_localPosition, _localRotation, _localScale);
                    _cachedLocalVersion = _version;
                    _worldVersion++;
                }
                return _worldCache;
            }

            Float4x4 parentWorld = parent.LocalToWorldMatrix;
            if (_cachedLocalVersion != _version || _cachedParentWorldVersion != parent._worldVersion)
            {
                _worldCache = parentWorld * Float4x4.CreateTRS(_localPosition, _localRotation, _localScale);
                _cachedLocalVersion = _version;
                _cachedParentWorldVersion = parent._worldVersion;
                _worldVersion++;
            }
            return _worldCache;
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

    // Lazy world-matrix cache. _cachedLocalVersion starts at 0 (Version starts at 1) so the first
    // access always rebuilds. _worldVersion increments on every rebuild so children can detect an
    // ancestor move without walking further than their direct parent.
    [SerializeIgnore] Float4x4 _worldCache;
    [SerializeIgnore] uint _cachedLocalVersion;
    [SerializeIgnore] uint _cachedParentWorldVersion;
    [SerializeIgnore] uint _worldVersion;

    public GameObject GameObject { get; internal set; }
    #endregion

    public void SetLocalTransform(Float3 position, Quaternion rotation, Float3 scale)
    {
        _localPosition = position;
        _localRotation = rotation;
        _localScale = scale;
        _version++;
    }

    /// <summary>
    /// Bump <see cref="Version"/> to signal the world transform changed for a reason other than a
    /// local setter (e.g. reparenting under a new parent while keeping local values).
    /// </summary>
    public void MarkChanged() => _version++;

    private float MakeSafe(float v) => float.IsNaN(v) ? 0 : v;
    private Float3 MakeSafe(Float3 v) => new(MakeSafe(v.X), MakeSafe(v.Y), MakeSafe(v.Z));
    private Quaternion MakeSafe(Quaternion v) => new(MakeSafe(v.X), MakeSafe(v.Y), MakeSafe(v.Z), MakeSafe(v.W));

    public Transform? Find(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        string[] names = path.Split('/');
        Transform currentTransform = this;

        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name)) return null;

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
        Quaternion q = Quaternion.AxisAngle(axis, angle * Maths.Deg2Rad);
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

    #region Look At

    /// <summary>
    /// Rotate so Forward points at <paramref name="target"/> in world space, with world +Y as up.
    /// </summary>
    public void LookAt(Float3 target) => LookAt(target, Float3.UnitY);

    /// <summary>
    /// Rotate so Forward points at <paramref name="target"/> in world space, using <paramref name="worldUp"/>
    /// to disambiguate roll. No-op if target coincides with our position.
    /// </summary>
    public void LookAt(Float3 target, Float3 worldUp)
    {
        Float3 dir = target - Position;
        if (Float3.LengthSquared(dir) < float.Epsilon) return;
        Rotation = Quaternion.LookRotation(Float3.Normalize(dir), worldUp);
    }

    /// <summary>Convenience: rotate so Forward points at the given Transform.</summary>
    public void LookAt(Transform target) => LookAt(target.Position, Float3.UnitY);

    #endregion

    #region Atomic Setters

    /// <summary>
    /// Set world position and rotation in one call bumps Version once instead of twice
    /// so change detection fires a single time.
    /// </summary>
    public void SetPositionAndRotation(Float3 position, Quaternion rotation)
    {
        if (Parent != null)
        {
            _localPosition = MakeSafe(Parent.InverseTransformPoint(position));
            _localRotation = MakeSafe(Quaternion.NormalizeSafe(Quaternion.Inverse(Parent.Rotation) * rotation));
        }
        else
        {
            _localPosition = MakeSafe(position);
            _localRotation = MakeSafe(Quaternion.NormalizeSafe(rotation));
        }
        _version++;
    }

    /// <summary>Local-space counterpart to <see cref="SetPositionAndRotation"/>.</summary>
    public void SetLocalPositionAndRotation(Float3 localPosition, Quaternion localRotation)
    {
        _localPosition = MakeSafe(localPosition);
        _localRotation = MakeSafe(Quaternion.NormalizeSafe(localRotation));
        _version++;
    }

    /// <summary>
    /// World-space counterpart to <see cref="SetLocalTransform"/>. Rotation/scale are converted
    /// to local space via the parent, then stored.
    /// </summary>
    public void SetWorldTransform(Float3 position, Quaternion rotation, Float3 scale)
    {
        if (Parent != null)
        {
            _localPosition = MakeSafe(Parent.InverseTransformPoint(position));
            _localRotation = MakeSafe(Quaternion.NormalizeSafe(Quaternion.Inverse(Parent.Rotation) * rotation));
            Float3 parentLossy = Parent.LossyScale;
            _localScale = MakeSafe(new Float3(
                scale.X * InverseSafe(parentLossy.X),
                scale.Y * InverseSafe(parentLossy.Y),
                scale.Z * InverseSafe(parentLossy.Z)));
        }
        else
        {
            _localPosition = MakeSafe(position);
            _localRotation = MakeSafe(Quaternion.NormalizeSafe(rotation));
            _localScale = MakeSafe(scale);
        }
        _version++;
    }

    #endregion

    #region Parenting

    /// <summary>
    /// Reparent this transform. When <paramref name="worldPositionStays"/> is true (the default)
    /// the world pose is preserved across the reparent; when false the transform adopts the
    /// parent's frame and keeps its local pose (effectively snapping to the new parent).
    /// </summary>
    public bool SetParent(Transform? parent, bool worldPositionStays = true)
        => GameObject?.SetParent(parent?.GameObject, worldPositionStays) ?? false;

    #endregion

    #region Children

    /// <summary>Number of direct children on the owning GameObject.</summary>
    public int ChildCount => GameObject?.Children.Count ?? 0;

    /// <summary>Direct child transform by index. Throws if out of range.</summary>
    public Transform GetChild(int index) => GameObject.Children[index].Transform;

    /// <summary>Enumerate direct children as Transforms.</summary>
    public IEnumerable<Transform> GetChildren()
    {
        if (GameObject == null) yield break;
        foreach (var child in GameObject.Children)
            yield return child.Transform;
    }

    /// <summary>True if this is a descendant of <paramref name="parent"/> (or is <paramref name="parent"/>).</summary>
    public bool IsChildOf(Transform parent)
    {
        if (parent == null) return false;
        Transform? cur = this;
        while (cur != null)
        {
            if (cur == parent) return true;
            cur = cur.Parent;
        }
        return false;
    }

    /// <summary>Index of this transform within its parent's children list, or -1 at the root.</summary>
    public int GetSiblingIndex()
    {
        var p = Parent;
        if (p == null || p.GameObject == null) return -1;
        return p.GameObject.Children.IndexOf(GameObject);
    }

    /// <summary>
    /// Move this transform to <paramref name="index"/> within its parent's child list. Index is
    /// clamped. No-op if there's no parent.
    /// </summary>
    public void SetSiblingIndex(int index)
    {
        var p = Parent;
        if (p == null || p.GameObject == null) return;
        var siblings = p.GameObject.Children;
        int current = siblings.IndexOf(GameObject);
        if (current < 0) return;
        index = System.Math.Clamp(index, 0, siblings.Count - 1);
        if (index == current) return;
        siblings.RemoveAt(current);
        siblings.Insert(index, GameObject);
        _version++;
    }

    /// <summary>Move this transform to the first position in its parent's child list.</summary>
    public void SetAsFirstSibling() => SetSiblingIndex(0);

    /// <summary>Move this transform to the last position in its parent's child list.</summary>
    public void SetAsLastSibling()
    {
        var p = Parent;
        if (p == null || p.GameObject == null) return;
        SetSiblingIndex(p.GameObject.Children.Count - 1);
    }

    /// <summary>
    /// Unparent every direct child. <paramref name="worldPositionStays"/> controls whether the
    /// children keep their world pose (true) or inherit the new root's identity frame (false).
    /// </summary>
    public void DetachChildren(bool worldPositionStays = true)
    {
        if (GameObject == null) return;
        // Copy because SetParent mutates the Children list.
        var snapshot = GameObject.Children.ToArray();
        foreach (var child in snapshot)
            child.SetParent(null, worldPositionStays);
    }

    #endregion

    #region Change Detection

    /// <summary>
    /// True if <see cref="Version"/> differs from <paramref name="lastVersion"/>; updates the
    /// reference to the current version on return so the next call compares against today's state.
    /// </summary>
    public bool HasChanged(ref uint lastVersion)
    {
        if (_version == lastVersion) return false;
        lastVersion = _version;
        return true;
    }

    #endregion

    #region Bounds

    /// <summary>
    /// Transform a local-space AABB into a world-space AABB. Equivalent to
    /// <c>bounds.TransformBy(LocalToWorldMatrix)</c> provided here for discoverability.
    /// </summary>
    public AABB TransformAABB(AABB localBounds) => localBounds.TransformBy(LocalToWorldMatrix);

    /// <summary>Transform a world-space AABB into this transform's local space.</summary>
    public AABB InverseTransformAABB(AABB worldBounds) => worldBounds.TransformBy(WorldToLocalMatrix);

    #endregion
}
