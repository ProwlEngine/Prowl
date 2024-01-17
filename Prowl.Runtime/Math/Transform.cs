using System;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    public class Transform
    {
        #region Properties

        #region Position
        public Vector3 position {
            get {

                if (parent != null)
                    return MakeSafe(parent.localToWorldMatrix.MultiplyPoint(m_LocalPosition));
                else
                    return MakeSafe(m_LocalPosition);
            }
            set {
                Vector3 newPosition = value;
                Transform p = parent;
                if (p != null)
                    newPosition = p.InverseTransformPoint(newPosition);

                localPosition = MakeSafe(newPosition);
            }
        }

        public Vector3 localPosition {
            get => MakeSafe(m_LocalPosition);
            set {
                if (m_LocalPosition != value)
                    m_LocalPosition = MakeSafe(value);
            }
        }
        #endregion

        #region Rotation
        public Quaternion rotation {
            get {
                Quaternion worldRot = m_LocalRotation;
                Transform p = parent;
                while (p != null)
                {
                    worldRot = p.m_LocalRotation * worldRot;
                    p = p.parent;
                }
                return MakeSafe(worldRot);
            }
            set {
                if (parent != null)
                    localRotation = MakeSafe(Quaternion.NormalizeSafe(Quaternion.Inverse(parent.rotation) * value));
                else
                    localRotation = MakeSafe(Quaternion.NormalizeSafe(value));
            }
        }

        public Quaternion localRotation {
            get => MakeSafe(m_LocalRotation);
            set {

                if (m_LocalRotation != value)
                    m_LocalRotation = MakeSafe(value);
            }
        }

        public Vector3 eulerAngles { get => MakeSafe(rotation.eulerAngles); set => rotation = MakeSafe(Quaternion.Euler(value)); }

        public Vector3 localEulerAngles {
            get => MakeSafe(m_LocalRotation.eulerAngles);
            set => m_LocalRotation.eulerAngles = MakeSafe(value);
        }
        #endregion

        #region Scale

        public Vector3 localScale {
            get => MakeSafe(m_LocalScale);
            set {
                if (m_LocalScale != value)
                    m_LocalScale = MakeSafe(value);
            }
        }

        public Vector3 lossyScale {
            get {
                Matrix4x4 invRotation = Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(rotation));
                Matrix4x4 scaleAndRotation = GetWorldRotationAndScale();
                Matrix4x4 rot = invRotation * scaleAndRotation;
                return new Vector3(rot[0, 0], rot[1, 1], rot[2, 2]);
            }
        }

        #endregion

        public Vector3 right { get => rotation * Vector3.right; } // TODO: Setter
        public Vector3 up { get => rotation * Vector3.up; } // TODO: Setter
        public Vector3 forward { get => rotation * Vector3.forward; } // TODO: Setter

        public Matrix4x4 worldToLocalMatrix => localToWorldMatrix.Invert();
        public Matrix4x4 localToWorldMatrix {
            get {
                Matrix4x4 t = Matrix4x4.TRS(m_LocalPosition, m_LocalRotation, m_LocalScale);
                return parent != null ? parent.localToWorldMatrix * t : t;
            }
        }

        public Transform parent {
            get => gameObject._parent?.transform;
            set => gameObject.SetParent(value.gameObject, true);
        }

        public bool hasChanged {
            get => _hasChanged;
            set => _hasChanged = value;
        }

        public Transform root => parent == null ? this : parent.root;


        #endregion

        #region Fields

        [SerializeField] Vector3 m_LocalPosition;
        [SerializeField] Vector3 m_LocalScale = Vector3.one;
        [SerializeField] Quaternion m_LocalRotation = Quaternion.identity;

        [NonSerialized]
        bool _hasChanged = false;

        public GameObject gameObject { get; internal set; }
        #endregion

        private double MakeSafe(double v) => double.IsNaN(v) ? 0 : v;
        private Vector3 MakeSafe(Vector3 v) => new Vector3(MakeSafe(v.x), MakeSafe(v.y), MakeSafe(v.z));
        private Quaternion MakeSafe(Quaternion v) => new Quaternion(MakeSafe(v.x), MakeSafe(v.y), MakeSafe(v.z), MakeSafe(v.w));

        public Transform? Find(string name)
        {
            if (name == null) return null;
            if (name == gameObject.Name) return this;
            foreach (var child in gameObject.children)
            {
                var t = child.transform.Find(name);
                if (t != null) return t;
            }
            return null;
        }

        public void Translate(Vector3 translation, Transform? relativeTo = null)
        {
            if (relativeTo != null)
                position += relativeTo.TransformDirection(translation);
            else
                position += translation;
        }

        // Applies a rotation of /eulerAngles.z/ degrees around the z axis, /eulerAngles.x/ degrees around the x axis, and /eulerAngles.y/ degrees around the y axis (in that order).
        public void Rotate(Vector3 eulerAngles, bool relativeToSelf = true)
        {
            Quaternion eulerRot = Quaternion.Euler(eulerAngles.x, eulerAngles.y, eulerAngles.z);
            if (relativeToSelf)
                localRotation = localRotation * eulerRot;
            else
                rotation = rotation * (Quaternion.Inverse(rotation) * eulerRot * rotation);
        }

        // Rotates the transform around /axis/ by /angle/ degrees.
        public void Rotate(Vector3 axis, double angle, bool relativeToSelf = true)
        {
            RotateAroundInternal(relativeToSelf ? TransformDirection(axis) : axis, angle * Mathf.Deg2Rad);
        }

        // Rotates the transform about /axis/ passing through /point/ in world coordinates by /angle/ degrees.
        public void RotateAround(Vector3 point, Vector3 axis, double angle)
        {
            Vector3 worldPos = position;
            Quaternion q = Quaternion.AngleAxis(angle, axis);
            Vector3 dif = worldPos - point;
            dif = q * dif;
            worldPos = point + dif;
            position = worldPos;
            RotateAroundInternal(axis, angle * Mathf.Deg2Rad);
        }

        internal void RotateAroundInternal(Vector3 worldAxis, double rad)
        {
            Vector3 localAxis = InverseTransformDirection(worldAxis);
            if (localAxis.sqrMagnitude > Mathf.Epsilon)
            {
                localAxis.Normalize();
                Quaternion q = Quaternion.AngleAxis(rad, localAxis);
                m_LocalRotation = Quaternion.NormalizeSafe(m_LocalRotation * q);
            }
        }

        public void LookAt(Vector3 worldPosition, Vector3 worldUp)
        {
            // Cheat using Matrix4x4.CreateLookAt
            Matrix4x4 m = Matrix4x4.CreateLookAt(position, worldPosition, worldUp);
            m_LocalRotation = Quaternion.NormalizeSafe(Quaternion.MatrixToQuaternion(m));
        }


        #region Transform 

        public Vector3 TransformPoint(Vector3 inPoint) => localToWorldMatrix.MultiplyPoint(inPoint);
        public Vector3 InverseTransformPoint(Vector3 inPosition)
        {
            Vector3 newPosition, localPosition;
            if (parent != null)
                localPosition = parent.InverseTransformPoint(inPosition);
            else
                localPosition = inPosition;

            localPosition -= m_LocalPosition;
            newPosition = Quaternion.Inverse(m_LocalRotation) * localPosition;
            if (m_LocalScale != Vector3.one)
                newPosition.Scale(InverseSafe(m_LocalScale));

            return newPosition;
        }

        public Vector3 TransformDirection(Vector3 inDirection) => rotation * inDirection;
        public Vector3 InverseTransformDirection(Vector3 inDirection) => Quaternion.Inverse(rotation) *  inDirection;

        public Vector3 TransformVector(Vector3 inVector)
        {
            Vector3 worldVector = inVector;

            Transform cur = this;
            while (cur != null)
            {
                worldVector.Scale(cur.m_LocalScale);
                worldVector = cur.m_LocalRotation * worldVector;

                cur = cur.parent;
            }
            return worldVector;
        }
        public Vector3 InverseTransformVector(Vector3 inVector)
        {
            Vector3 newVector, localVector;
            if (parent != null)
                localVector = parent.InverseTransformVector(inVector);
            else
                localVector = inVector;

            newVector = Quaternion.Inverse(m_LocalRotation) * localVector;
            if (m_LocalScale != Vector3.one)
                newVector.Scale(InverseSafe(m_LocalScale));

            return newVector;
        }

        #endregion

        public Matrix4x4 GetWorldRotationAndScale()
        {
            Matrix4x4 ret = Matrix4x4.TRS(new Vector3(0, 0, 0), m_LocalRotation, m_LocalScale);
            if (parent != null)
            {
                Matrix4x4 parentTransform = parent.GetWorldRotationAndScale();
                ret = parentTransform * ret;
            }
            return ret;
        }

        static double InverseSafe(double f) => Mathf.Abs(f) > Mathf.Epsilon ? 1.0F / f : 0.0F;
        static Vector3 InverseSafe(Vector3 v) => new Vector3(InverseSafe(v.x), InverseSafe(v.y), InverseSafe(v.z));
    }
}
