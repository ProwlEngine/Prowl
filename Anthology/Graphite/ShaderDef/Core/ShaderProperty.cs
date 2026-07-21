using Prowl.Vector;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Default value a ShaderDef markdown file wants for a uniform or resource.
/// </summary>
public struct ShaderProperty
{
    /// <summary>
    /// Name in shader source to bind to.
    /// </summary>
    public string Name;

    /// <summary>
    /// Display name for debug/editor UI.
    /// </summary>
    public string DisplayName;

    /// <summary>
    /// Resource type this targets.
    /// </summary>
    public ShaderPropertyType PropertyType;

    /// <summary>
    /// Backing float value.
    /// <para>Float or Integer: uses first element only.</para>
    /// <para>Color or Vector: uses the whole value.</para>
    /// </summary>
    public Float4 Value;

    /// <summary>
    /// Backing matrix value. Only set for Matrix type.
    /// </summary>
    public Float4x4 MatrixValue;

    /// <summary>
    /// Default texture name. Only set for texture types.
    /// </summary>
    public string TextureValue;
}


/// <summary>
/// Backing type a property was created with.
/// </summary>
public enum ShaderPropertyType
{
    /// <summary>
    /// Scalar float.
    /// </summary>
    Float,

    /// <summary>
    /// Scalar int.
    /// </summary>
    Integer,

    /// <summary>
    /// Color. Basically Vector but with different parsing overloads.
    /// </summary>
    Color,

    /// <summary>
    /// Vector, type unknown, defaults to float. Parsed as 4 floats:
    /// <code>(0,1,2,3)</code>
    /// </summary>
    Vector,

    /// <summary>
    /// 4x4 matrix, type unknown, defaults to float. Parsed as 4 vectors:
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
    /// 2D texture. Parsed as: <code>"texture name" {}</code>
    /// </summary>
    Texture2D,

    /// <summary>
    /// Array of 2D textures. Parsed as: <code>"texture name" {}</code>
    /// </summary>
    Texture2DArray,

    /// <summary>
    /// 3D texture. Parsed as: <code>"texture name" {}</code>
    /// </summary>
    Texture3D,

    /// <summary>
    /// Cubemap texture. Parsed as: <code>"texture name" {}</code>
    /// </summary>
    TextureCubemap,

    /// <summary>
    /// Cubemap array texture. Parsed as: <code>"texture name" {}</code>
    /// </summary>
    TextureCubemapArray
}
