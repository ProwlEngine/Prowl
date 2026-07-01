using System;

using Prowl.Crumb;
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


/// <summary>
/// A static parser to parse a shaderdef property.
/// </summary>
public static class ParsedProperty
{
    // Rejects a value whose leading token can't begin the property's type, so a mismatch reports
    // the expected shape rather than a generic "expected number/(" from the underlying parser.
    static void ValidateValueShape(ref Tokenizer<ShaderToken> t, ShaderPropertyType type)
    {
        Token<ShaderToken> value = t.Peek();

        (bool ok, string expected) = type switch
        {
            ShaderPropertyType.Float or
            ShaderPropertyType.Integer =>
                (value.Kind is ShaderToken.Number or ShaderToken.Minus, "a scalar number"),

            ShaderPropertyType.Color or
            ShaderPropertyType.Vector =>
                (value.Kind == ShaderToken.OpenParen, "a 4-component vector like (x, y, z, w)"),

            ShaderPropertyType.Matrix =>
                (value.Kind == ShaderToken.OpenParen, "a 4x4 matrix like ((..)(..)(..)(..))"),

            _ => (value.Kind == ShaderToken.String, "a texture name like \"name\" {}")
        };

        if (!ok)
            throw Exceptions.PropertyValue(type.ToString(), expected, ParserUtility.Found(ref t, value), value);
    }


    static object PropertyValue(ref Tokenizer<ShaderToken> t, ShaderPropertyType type)
    {
        ValidateValueShape(ref t, type);

        return type switch
        {
            ShaderPropertyType.Float => ParserUtility.Float(ref t),
            ShaderPropertyType.Integer => (float)ParserUtility.Integer(ref t),
            ShaderPropertyType.Color or
            ShaderPropertyType.Vector => ParserUtility.Vector(ref t),
            ShaderPropertyType.Matrix => ParserUtility.Matrix(ref t),
            ShaderPropertyType.Texture2D or
            ShaderPropertyType.Texture2DArray or
            ShaderPropertyType.Texture3D or
            ShaderPropertyType.TextureCubemap or
            ShaderPropertyType.TextureCubemapArray => ParserUtility.Texture(ref t),

            _ => throw new NotSupportedException($"Unsupported type {type}")
        };
    }


    /// <summary>
    /// Parses a single property: Name("Display Name", Type) = Value
    /// </summary>
    public static ShaderProperty Parse(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return Parse(ref t);
    }


    internal static ShaderProperty Parse(ref Tokenizer<ShaderToken> t)
    {
        Token<ShaderToken> nameToken = ParserUtility.Expect(ref t, ShaderToken.Identifier, "property name");
        string name = t.Slice(nameToken).ToString();

        ParserUtility.Expect(ref t, ShaderToken.OpenParen);
        string display = ParserUtility.QuotedString(ref t);
        ParserUtility.Expect(ref t, ShaderToken.Comma);
        ShaderPropertyType type = ParserUtility.Keywords<ShaderPropertyType>(ref t);
        ParserUtility.Expect(ref t, ShaderToken.CloseParen);

        object? value = null;
        if (t.TryConsume(ShaderToken.Equals))
            value = PropertyValue(ref t, type);

        return new ShaderProperty
        {
            Name = name,
            DisplayName = display,
            PropertyType = type,

            Value = value switch
            {
                float f => new Float4(f, 0, 0, 0),
                Float4 v => v,
                _ => Float4.Zero
            },

            MatrixValue = value is Float4x4 m ? m : Float4x4.Zero,

            TextureValue = value as string ?? ""
        };
    }
}
