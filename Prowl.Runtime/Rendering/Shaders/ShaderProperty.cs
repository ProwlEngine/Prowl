using Prowl.Echo;

using Texture2D = Prowl.Runtime.Resources.Texture2D;

namespace Prowl.Runtime.Rendering.Shaders
{
    public enum ShaderPropertyType { Float, Vector2, Vector3, Vector4, Color, Matrix, Texture2D }

    public class ShaderProperty
    {
        public string Name;
        public string DisplayName;

        [field: SerializeField]
        public ShaderPropertyType PropertyType { get; private set; }

        public Vector4 Value;
        public Matrix4x4 MatrixValue;

        public AssetRef<Texture2D> Texture2DValue;

        public ShaderProperty() { }

        public void Set(ShaderProperty other)
        {
            if (other.PropertyType != PropertyType)
                return;

            Value = other.Value;
            MatrixValue = other.MatrixValue;
            Texture2DValue = other.Texture2DValue;
        }

        public ShaderProperty(double value)
        {
            Value = new(value);
            PropertyType = ShaderPropertyType.Float;
        }

        public static implicit operator ShaderProperty(double value)
            => new ShaderProperty(value);

        public static implicit operator double(ShaderProperty value)
            => value.Value.x;

        public ShaderProperty(Vector2 value)
        {
            Value = new(value);
            PropertyType = ShaderPropertyType.Vector2;
        }

        public static implicit operator ShaderProperty(Vector2 value)
            => new ShaderProperty(value);

        public static implicit operator Vector2(ShaderProperty value)
            => new Vector2(value.Value.x, value.Value.y);

        public ShaderProperty(Vector3 value)
        {
            Value = new(value);
            PropertyType = ShaderPropertyType.Vector3;
        }

        public static implicit operator ShaderProperty(Vector3 value)
            => new ShaderProperty(value);

        public static implicit operator Vector3(ShaderProperty value)
            => new Vector3(value.Value.x, value.Value.y, value.Value.z);

        public ShaderProperty(Vector4 value)
        {
            Value = value;
            PropertyType = ShaderPropertyType.Vector4;
        }

        public static implicit operator ShaderProperty(Vector4 value)
            => new ShaderProperty(value);

        public static implicit operator Vector4(ShaderProperty value)
            => value.Value;

        public ShaderProperty(Color value)
        {
            Value = new(value.r, value.g, value.b, value.a);
            PropertyType = ShaderPropertyType.Color;
        }

        public static implicit operator ShaderProperty(Color value)
            => new ShaderProperty(value);

        public static implicit operator Color(ShaderProperty value)
            => new Color((float)value.Value.x, (float)value.Value.y, (float)value.Value.z, (float)value.Value.w);

        public ShaderProperty(Matrix4x4 value)
        {
            MatrixValue = value;
            PropertyType = ShaderPropertyType.Matrix;
        }

        public static implicit operator ShaderProperty(Matrix4x4 value)
            => new ShaderProperty(value);

        public static implicit operator Matrix4x4(ShaderProperty value)
            => value.MatrixValue;

        public ShaderProperty(AssetRef<Texture2D> value)
        {
            Texture2DValue = value;
            PropertyType = ShaderPropertyType.Texture2D;
        }

        public static implicit operator ShaderProperty(AssetRef<Texture2D> value)
            => new ShaderProperty(value);

        public static implicit operator AssetRef<Texture2D>(ShaderProperty value)
            => value.Texture2DValue;
    }
}
