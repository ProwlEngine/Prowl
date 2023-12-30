using Prowl.Icons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime
{
    [DisallowMultipleComponent, ExecuteAlways]
    [AddComponentMenu($"{FontAwesome6.ArrowsUpDownLeftRight}  Transform")]
    public class Transform : MonoBehaviour, ISerializable
    {
        protected Vector3 position;
        protected Vector3 rotation;
        protected Vector3 scale = Vector3.One;
        protected Vector3 globalPosition;
        protected Quaternion orientation = Quaternion.Identity, globalOrientation;
        protected Matrix4x4 globalPrevious, global, globalInverse;
        protected Matrix4x4 local;

        /// <summary>Gets or sets the local position.</summary>
        public Vector3 Position {
            get => position;
            set {
                if (position == value) return;
                position = value;
                Recalculate();
            }
        }

        /// <summary>Gets or sets the local rotation.</summary>
        /// <remarks>The rotation is in space euler from 0° to 360°(359°)</remarks>
        public Vector3 Rotation {
            get => rotation;
            set {
                if (rotation == value) return;
                rotation = value;
                orientation = value.NormalizeEulerAngleDegrees().ToRad().GetQuaternion();
                Recalculate();
            }
        }

        /// <summary>Gets or sets the local scale.</summary>
        public Vector3 Scale {
            get => scale;
            set {
                if (scale == value) return;
                scale = value;
                Recalculate();
            }
        }

        /// <summary>Gets or sets the local orientation.</summary>
        public Quaternion Orientation {
            get => orientation;
            set {
                if (orientation == value) return;
                orientation = value;
                rotation = value.GetRotation().ToDeg().NormalizeEulerAngleDegrees();
                Recalculate();
            }
        }

        /// <summary>Gets or sets the global (world space) position.</summary>
        public Vector3 GlobalPosition {
            get => globalPosition;
            set {
                if (globalPosition == value) return;
                var parentTransform = GetComponentInParent<Transform>(false);
                if (parentTransform == null)
                    position = value;
                else // Transform because the rotation could modify the position of the child.
                    position = Vector3.Transform(value, parentTransform.globalInverse);
                Recalculate();
            }
        }

        /// <summary>Gets or sets the global (world space) orientation.</summary>
        public Quaternion GlobalOrientation {
            get => globalOrientation;
            set {
                if (globalOrientation == value) return;
                var parentTransform = GetComponentInParent<Transform>(false);
                if (parentTransform == null)
                    orientation = value;
                else // Divide because quaternions are like matrices.
                    orientation = value / parentTransform.globalOrientation;
                rotation = orientation.GetRotation().ToDeg().NormalizeEulerAngleDegrees();
                Recalculate();
            }
        }

        /// <summary>The forward vector in global orientation space</summary>
        public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, globalOrientation);

        /// <summary>The right vector in global orientation space</summary>
        public Vector3 Right => Vector3.Transform(Vector3.UnitX, globalOrientation);

        /// <summary>The up vector in global orientation space</summary>
        public Vector3 Up => Vector3.Transform(Vector3.UnitY, globalOrientation);

        /// <summary>The global transformation matrix of the previous frame.</summary>
        public Matrix4x4 GlobalPrevious => globalPrevious;

        /// <summary>The global transformation matrix</summary>
        public Matrix4x4 Global => global;

        /// <summary>The inverse global transformation matrix</summary>
        public Matrix4x4 GlobalInverse => globalInverse;

        /// <summary>Returns a matrix relative/local to the currently rendering camera, Will throw an error if used outside rendering method</summary>
        public Matrix4x4 GlobalCamRelative {
            get {
                Matrix4x4 matrix = Global;
                matrix.Translation -= Camera.Current.GameObject.Transform?.GlobalPosition ?? Vector3.Zero;
                return matrix;
            }
        }

        /// <summary>The local transformation matrix</summary>
        public Matrix4x4 Local { get => local; set => SetMatrix(value); }

        public void Awake()
        {
            Recalculate();

            GameObject._transform = new (this);
        }

        public void OnDestroy()
        {
            GameObject._transform = null;
        }

        /// <summary>Recalculates all values of the <see cref="Transform"/>.</summary>
        public void Recalculate()
        {
            globalPrevious = global;

            local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(orientation) * Matrix4x4.CreateTranslation(position);
            var parentTransform = GetComponentInParent<Transform>(false);
            if (parentTransform == null)
                global = local;
            else
                global = local * parentTransform.global;

            Matrix4x4.Invert(global, out globalInverse);

            Matrix4x4.Decompose(global, out var globalScale, out globalOrientation, out globalPosition);

            foreach (var child in GameObject.GetComponentsInChildren<Transform>(false))
                child.Recalculate();
        }

        /// <summary>
        /// Reverse calculates the local params from a local matrix.
        /// </summary>
        /// <param name="matrix">Local space matrix</param>
        private void SetMatrix(Matrix4x4 matrix)
        {
            local = matrix;
            Matrix4x4.Decompose(local, out scale, out orientation, out position);
            rotation = orientation.GetRotation().ToDeg();
            Recalculate();
        }

        public static implicit operator Matrix4x4(Transform t) => t.global;

        public CompoundTag Serialize(TagSerializer.SerializationContext ctx)
        {
            CompoundTag compoundTag = new CompoundTag();
            compoundTag.Add("Name", new StringTag(Name));

            compoundTag.Add("Enabled", new ByteTag((byte)(_enabled ? 1 : 0)));
            compoundTag.Add("EnabledInHierarchy", new ByteTag((byte)(_enabledInHierarchy ? 1 : 0)));

            compoundTag.Add("HideFlags", new IntTag((int)hideFlags));

            if (AssetID != Guid.Empty)
                compoundTag.Add("AssetID", new StringTag(AssetID.ToString()));

            compoundTag.Add("PosX", new DoubleTag(position.X));
            compoundTag.Add("PosY", new DoubleTag(position.Y));
            compoundTag.Add("PosZ", new DoubleTag(position.Z));

            compoundTag.Add("RotX", new DoubleTag(rotation.X));
            compoundTag.Add("RotY", new DoubleTag(rotation.Y));
            compoundTag.Add("RotZ", new DoubleTag(rotation.Z));

            compoundTag.Add("ScalX", new DoubleTag(scale.X));
            compoundTag.Add("ScalY", new DoubleTag(scale.Y));
            compoundTag.Add("ScalZ", new DoubleTag(scale.Z));

            return compoundTag;
        }

        public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
        {
            Name = value["Name"].StringValue;
            _enabled = value["Enabled"].ByteValue == 1;
            _enabledInHierarchy = value["EnabledInHierarchy"].ByteValue == 1;
            hideFlags = (HideFlags)value["HideFlags"].IntValue;
            position = new Vector3(value["PosX"].DoubleValue, value["PosY"].DoubleValue, value["PosZ"].DoubleValue);
            rotation = new Vector3(value["RotX"].DoubleValue, value["RotY"].DoubleValue, value["RotZ"].DoubleValue);
            orientation = rotation.NormalizeEulerAngleDegrees().ToRad().GetQuaternion();
            scale = new Vector3(value["ScalX"].DoubleValue, value["ScalY"].DoubleValue, value["ScalZ"].DoubleValue);
        }
    }
}
