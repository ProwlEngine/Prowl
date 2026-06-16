using System;

using Prowl.Vector;

using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


public struct ShaderProperty
{
    public string Name;
    public string DisplayName;
    public ShaderPropertyType PropertyType;

    public Float4 Value;
    public Float4x4 MatrixValue;
    public string TextureValue;
}


public enum ShaderPropertyType
{
    Float,
    Integer,
    Color,
    Vector,
    Matrix,
    Texture2D,
    Texture2DArray,
    Texture3D,
    TextureCubemap,
    TextureCubemapArray
}


public static class ParsedProperty
{
    static object PropertyValue(ref Tokenizer<ShaderToken> t, ShaderPropertyType type)
    {
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


    // Parses a single property: Name("Display Name", Type) = Value
    public static ShaderProperty Parse(ref Tokenizer<ShaderToken> t)
    {
        Token<ShaderToken> nameToken = t.Expect(ShaderToken.Identifier);
        string name = t.Slice(nameToken).ToString();

        t.Expect(ShaderToken.OpenParen);
        string display = ParserUtility.QuotedString(ref t);
        t.Expect(ShaderToken.Comma);
        ShaderPropertyType type = ParserUtility.Keywords<ShaderPropertyType>(ref t);
        t.Expect(ShaderToken.CloseParen);

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
