using Prowl.Vector;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// A default value that a ShaderDef markdown file requests for a given uniform or resource.
/// </summary>
public struct ShaderProperty
{
    /// <summary>
    /// The name for the resource or uniform in shader source to bind to.
    /// </summary>
    public string Name;

    /// <summary>
    /// The display name of the resource to show for debugging or editor purposes.
    /// </summary>
    public string DisplayName;

    /// <summary>
    /// The resource type this property targets.
    /// </summary>
    public ShaderPropertyType PropertyType;

    /// <summary>
    /// The backing float value of this property.
    /// <para
    /// >If the property was created with
    /// <see cref="ShaderPropertyType.Float"/>
    /// or <see cref="ShaderPropertyType.Integer"/>,
    /// this will resolve the value to its first element.
    /// </para>
    /// <para>
    /// If the property was created with
    /// <see cref="ShaderPropertyType.Color"/>
    /// or <see cref="ShaderPropertyType.Vector"/>,
    /// this will resolve the entire value to the set value.
    /// </para>
    /// </summary>
    public Float4 Value;

    /// <summary>
    /// The backing matrix value of this property. Set when the property is of the type <see cref="ShaderPropertyType.Matrix"/>
    /// </summary>
    public Float4x4 MatrixValue;

    /// <summary>
    /// The string-based name of the default texture value to use for this property.
    /// Set when the property is of any of the texture <see cref="ShaderPropertyType"/>s
    /// </summary>
    public string TextureValue;
}


/// <summary>
/// The backing type a property was created with.
/// </summary>
public enum ShaderPropertyType
{
    /// <summary>
    /// Single-dimensional scalar float value.
    /// </summary>
    Float,

    /// <summary>
    /// Single-dimensional scalar int value.
    /// </summary>
    Integer,

    /// <summary>
    /// Color value. Principally similar to 'Vector', but provides different parsing overloads.
    /// </summary>
    Color,

    /// <summary>
    /// Vector value. Actual resource type is unknown, and defaults to float for all values.
    /// Parsed as a 4-float length list in the format:
    /// <code>(0,1,2,3)</code>
    /// </summary>
    Vector,

    /// <summary>
    /// 4x4 matrix value. Actual resource type is unknown, and defaults to float for all values.
    /// Parsed as a 4-vector length list in the format:
    /// <code>
    /// (
    ///     (00,01,02,03),
    ///     (10,11,12,13),
    ///     (20,21,22,23),
    ///     (30,31,32,33)
    /// )
    /// </code>
    /// </summary>
    Matrix,

    /// <summary>
    /// Two-dimensional texture value. Parsed as: <code>"texture name" {}</code>
    /// </summary>
    Texture2D,

    /// <summary>
    /// Array of two-dimensional texture values. Parsed as: <code>"texture name" {}</code>
    /// </summary>
    Texture2DArray,

    /// <summary>
    /// Three-dimensional texture value. Parsed as: <code>"texture name" {}</code>
    /// </summary>
    Texture3D,

    /// <summary>
    /// Cubemap texture value. Parsed as: <code>"texture name" {}</code>
    /// </summary>
    TextureCubemap,

    /// <summary>
    /// Cubemap array texture value. Parsed as: <code>"texture name" {}</code>
    /// </summary>
    TextureCubemapArray
}
