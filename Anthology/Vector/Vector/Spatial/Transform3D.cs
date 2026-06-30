using System;
using System.Runtime.CompilerServices;

namespace Prowl.Vector.Spatial
{
    /// <summary>
    /// Represents a 3D transformation matrix. Provides a rich API for manipulating
    /// position, rotation, and scale, suitable for 3D object transformations.
    /// </summary>
    public struct Transform3D : IEquatable<Transform3D>
    {
        /// <summary>A transform with default values (position 0, no rotation, scale 1).</summary>
        public static readonly Transform3D Identity = new Transform3D(Float3.Zero, Quaternion.Identity, Float3.One);


        /// <summary>The position of the transform in 3D space.</summary>
        public Float3 position;
        
        /// <summary>The rotation of the transform as a standard float-based quaternion.</summary>
        public Quaternion rotation;

        /// <summary>The scale of the transform.</summary>
        public Float3 scale;


        /// <summary>
        /// Initializes a new transform with specified position, rotation, and scale.
        /// </summary>
        public Transform3D(Float3 position, Quaternion rotation, Float3 scale)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }


        #region Properties
        
        /// <summary>
        /// The rotation as Euler angles in degrees (order ZYXr).
        /// This is useful for inspector UIs and simple rotational adjustments.
        /// </summary>
        public Float3 EulerAngles
        {
            
            get => rotation.EulerAngles;
            
            set => rotation.EulerAngles = value;
        }

        /// <summary>The forward direction of this transform (+Z axis in local space).</summary>
        public Float3 Forward => Quaternion.Forward(rotation);

        /// <summary>The up direction of this transform (+Y axis in local space).</summary>
        public Float3 Up => Quaternion.Up(rotation);

        /// <summary>The right direction of this transform (+X axis in local space).</summary>
        public Float3 Right => Quaternion.Right(rotation);

        #endregion


        #region Public Methods

        /// <summary>Transforms a point from local space to world space.</summary>
        public Float3 TransformPoint(Float3 point) => position + (rotation * (scale * point));

        /// <summary>Transforms a point from world space to local space.</summary>
        public Float3 InverseTransformPoint(Float3 point)
        {
            var invRot = Quaternion.Inverse(rotation);
            var invScale = new Float3(1.0f / scale.X, 1.0f / scale.Y, 1.0f / scale.Z);
            return invScale * (invRot * (point - position));
        }

        /// <summary>Transforms a direction from local space to world space (unaffected by scale or position).</summary>
        public Float3 TransformDirection(Float3 direction) => rotation * direction;

        /// <summary>Transforms a direction from world space to local space (unaffected by scale or position).</summary>
        public Float3 InverseTransformDirection(Float3 direction) => Quaternion.Inverse(rotation) * direction;
        /// <summary>Transforms a vector from local space to world space (affected by rotation and scale, but not position).</summary>
        public Float3 TransformVector(Float3 vector) => rotation * (scale * vector);

        /// <summary>Transforms a vector from world space to local space (affected by rotation and scale, but not position).</summary>
        public Float3 InverseTransformVector(Float3 vector)
        {
            var invRot = Quaternion.Inverse(rotation);
            var invScale = new Float3(1.0f / scale.X, 1.0f / scale.Y, 1.0f / scale.Z);
            return invScale * (invRot * vector);
        }
        
        /// <summary>Moves the transform in the direction and distance of translation.</summary>
        public void Translate(Float3 translation, bool relativeToSelf = true)
        {
            if (relativeToSelf)
                position += rotation * translation;
            else
                position += translation;
        }

        /// <summary>Applies a rotation of eulerAngles (in degrees) around the z, x and y axes, in that order.</summary>
        public void Rotate(Float3 eulerAngles, bool relativeToSelf = true) => Rotate(Quaternion.FromEuler(eulerAngles), relativeToSelf);

        /// <summary>Applies a rotation of angle (in degrees) around an axis.</summary>
        public void Rotate(Quaternion rot, bool relativeToSelf = true)
        {
            if (relativeToSelf)
                rotation = rotation * rot;
            else
                rotation = rot * rotation;
        }

        /// <summary>Rotates the transform around a point in world space.</summary>
        /// <param name="point">The world-space point to rotate around.</param>
        /// <param name="axis">The axis to rotate around.</param>
        /// <param name="angleDegrees">The angle in degrees.</param>
        public void RotateAround(Float3 point, Float3 axis, float angleDegrees)
        {
            var rot = Quaternion.AxisAngle(Float3.Normalize(axis), (float)(angleDegrees * Maths.Deg2Rad));
            var vector = position - point;
            vector = rot * vector;
            position = point + vector;
            rotation = rot * rotation;
        }

        /// <summary>
        /// Rotates the transform so the forward vector points at the target's current position.
        /// </summary>
        /// <param name="target">The target position to look at.</param>
        /// <param name="worldUp">The vector that defines "up" in world space.</param>
        public void LookAt(Float3 target, Float3 worldUp) => rotation = Quaternion.LookRotation(target - position, worldUp);

        #endregion


        #region Static Methods

        /// <summary>Linearly interpolates between two transforms.</summary>
        public static Transform3D Lerp(Transform3D a, Transform3D b, float t)
        {
            t = Maths.Clamp(t, 0.0f, 1.0f);
            return new Transform3D(
                Maths.Lerp(a.position, b.position, t),
                Quaternion.Nlerp(a.rotation, b.rotation, (float)t),
                Maths.Lerp(a.scale, b.scale, t)
            );
        }

        /// <summary>
        /// Gets the 4x4 matrix representing this transform (local to world).
        /// </summary>
        public Float4x4 ToMatrix() => Float4x4.CreateTRS(position, rotation, scale);

        /// <summary>
        /// Gets the inverse 4x4 matrix representing this transform (world to local).
        /// </summary>
        public Float4x4 ToInverseMatrix()
        {
            // Inverse TRS is inv(T) * inv(R) * inv(S)
            var invScale = new Float3(1.0f / scale.X, 1.0f / scale.Y, 1.0f / scale.Z);
            var invRot = Quaternion.Inverse(rotation);
            var invPos = -(invRot * position);

            return Float4x4.CreateTRS(invPos, invRot, invScale);
        }

        /// <summary>
        /// Creates a transform from a 4x4 matrix.
        /// Note: This decomposition assumes the matrix is a valid TRS matrix and does not support shear.
        /// </summary>
        public static Transform3D FromMatrix(Float4x4 m)
        {
            Float3 scale = new Float3(
                Float3.Length(new Float3(m.c0.X, m.c0.Y, m.c0.Z)),
                Float3.Length(new Float3(m.c1.X, m.c1.Y, m.c1.Z)),
                Float3.Length(new Float3(m.c2.X, m.c2.Y, m.c2.Z))
            );

            // Handle negative scale by flipping and adjusting rotation
            if (Float4x4.Determinant(m) < 0.0)
            {
                scale.X = -scale.X;
            }

            var invScale = new Float3(1.0f / scale.X, 1.0f / scale.Y, 1.0f / scale.Z);
            var rotMatrix = new Float3x3(
                m.c0.XYZ * invScale.X,
                m.c1.XYZ * invScale.Y,
                m.c2.XYZ * invScale.Z
            );

            return new Transform3D(m.c3.XYZ, Quaternion.FromMatrix(rotMatrix), scale);
        }

        #endregion


        #region Equals and GetHashCode

        public static bool operator ==(Transform3D left, Transform3D right) => left.Equals(right);
        public static bool operator !=(Transform3D left, Transform3D right) => !left.Equals(right);
        public bool Equals(Transform3D other) => position.Equals(other.position) &&
                   rotation.Equals(other.rotation) &&
                   scale.Equals(other.scale);
        public override bool Equals(object? obj) => obj is Transform3D other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(position, rotation, scale);

        #endregion


        public override string ToString() => $"Transform3DD(Position: {position}, Rotation: {rotation}, Scale: {scale})";
    }
}
