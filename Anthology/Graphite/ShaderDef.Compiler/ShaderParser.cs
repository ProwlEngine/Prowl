using System;
using System.Collections.Generic;

using Prowl.Crumb;
using Prowl.Vector;

using Prowl.Graphite.ShaderDef;


namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// A static parser for shaderdef markdown files, producing the core data types.
/// </summary>
public static class ShaderParser
{
    // ==================== ShaderDefinition ====================

    // Parses a property block: Properties { ... }
    static ShaderProperty[] ParsePropertiesBlock(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Properties");
        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        List<ShaderProperty> properties = new();
        HashSet<string> names = new();

        while (t.Peek().Kind == ShaderToken.Identifier)
        {
            Token<ShaderToken> nameToken = t.Peek();
            ShaderProperty property = ParseProperty(ref t);

            if (!names.Add(property.Name))
                throw Exceptions.Duplicate("property", property.Name, nameToken);

            properties.Add(property);
        }

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);
        return properties.ToArray();
    }


    // The main parser for a shader markup language inspired by ShaderLab.
    // Contains a property block, pass blocks, and a fallback.
    static ShaderDefinition ParseShader(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Shader");
        string name = ParserUtility.QuotedString(ref t);

        if (string.IsNullOrWhiteSpace(name))
            throw new ParseException("Shader must contain non-empty name", t.Line, t.Column);

        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        ShaderProperty[] properties = [];
        if (ParserUtility.PeekKeyword(ref t, "Properties"))
            properties = ParsePropertiesBlock(ref t);

        List<ShaderPass> passes = new();
        HashSet<string> passNames = new();
        while (ParserUtility.PeekKeyword(ref t, "Pass"))
        {
            Token<ShaderToken> passToken = t.Peek();
            ShaderPass pass = ParsePass(ref t);

            // Pass names are optional, so only named passes are checked for collisions.
            if (pass.Name.Length > 0 && !passNames.Add(pass.Name))
                throw Exceptions.Duplicate("pass name", pass.Name, passToken);

            passes.Add(pass);
        }

        if (passes.Count == 0)
            throw new ParseException("Shader must contain at least one Pass", t.Line, t.Column);

        string fallback = "";
        if (ParserUtility.PeekKeyword(ref t, "Fallback"))
        {
            t.Next();
            fallback = ParserUtility.QuotedString(ref t);

            if (string.IsNullOrWhiteSpace(name))
                throw new ParseException("Fallback must contain non-empty name", t.Line, t.Column);
        }

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);

        Token<ShaderToken> trailing = t.Peek();
        if (trailing.Kind != ShaderToken.EndOfFile)
            throw Exceptions.TrailingContent(ParserUtility.Text(ref t, trailing), trailing);

        return new ShaderDefinition
        {
            Name = name,
            Properties = properties,
            Passes = passes.ToArray(),
            Fallback = fallback,
        };
    }


    /// <summary>
    /// Parses a full .shaderdef markdown file.
    /// </summary>
    public static ShaderDefinition Parse(string source)
    {
        Tokenizer<ShaderToken> tokenizer = ShaderTokenizer.Create(source);
        return ParseShader(ref tokenizer);
    }


    // ==================== ShaderPass ====================

    static string ParsePassName(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Name");
        return ParserUtility.QuotedString(ref t);
    }


    static Dictionary<string, string> ParsePassTags(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Tags");
        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        Dictionary<string, string> tags = new();

        while (t.Peek().Kind == ShaderToken.String)
        {
            Token<ShaderToken> keyToken = t.Peek();
            string key = ParserUtility.QuotedString(ref t);

            if (tags.ContainsKey(key))
                throw Exceptions.Duplicate("tag key", key, keyToken);

            ParserUtility.Expect(ref t, ShaderToken.Equals);
            string value = ParserUtility.QuotedString(ref t);
            tags[key] = value;
        }

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);
        return tags;
    }


    /// <summary>
    /// Parses a pass block.
    /// </summary>
    public static ShaderPass ParsePass(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return ParsePass(ref t);
    }


    internal static ShaderPass ParsePass(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.ExpectKeyword(ref t, "Pass");

        // Optional pass index.
        if (t.Peek().Kind is ShaderToken.Number or ShaderToken.Minus)
            ParserUtility.Integer(ref t);

        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        string name = "";
        if (ParserUtility.PeekKeyword(ref t, "Name"))
            name = ParsePassName(ref t);

        Dictionary<string, string>? tags = null;
        if (ParserUtility.PeekKeyword(ref t, "Tags"))
            tags = ParsePassTags(ref t);

        PassState state = ParsePassState(ref t);

        // State parsing stops at the first identifier it doesn't recognize. A SLANGPROGRAM block is
        // a block token, not an identifier, so a leftover identifier here is a misspelled command.
        Token<ShaderToken> afterState = t.Peek();
        if (afterState.Kind == ShaderToken.Identifier)
            throw Exceptions.UnknownCommand(ParserUtility.Text(ref t, afterState), afterState);

        string inlineSlang = ParserUtility.SlangProgram(ref t);

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);

        return new ShaderPass
        {
            Name = name,
            Tags = tags,
            State = state,
            InlineSlang = inlineSlang
        };
    }


    // ==================== PassState ====================

    static Dictionary<string, bool> OnStateMap = new()
    {
        { "On", true },
        { "Off", false }
    };


    static Dictionary<string, FaceCullMode> FaceCullModeMap = new()
    {
        { "Back", FaceCullMode.Back },
        { "Front", FaceCullMode.Front },
        { "Off", FaceCullMode.None }
    };


    static ColorWriteMask ParseMask(ref Tokenizer<ShaderToken> t, Token<ShaderToken> maskToken)
    {
        ColorWriteMask result = 0;

        foreach (char c in t.Slice(maskToken))
        {
            result |= c switch
            {
                'R' => ColorWriteMask.Red,
                'G' => ColorWriteMask.Green,
                'B' => ColorWriteMask.Blue,
                'A' => ColorWriteMask.Alpha,
                _ => throw new ParseException($"Invalid channel {c}. Expected any of [R, G, B, A]", maskToken.Line, maskToken.Column)
            };
        }

        return result;
    }


    // Consumes and parses a single stencil command if the identifier names one.
    // Returns false (without consuming) for any unrecognized identifier, e.g. the closing brace.
    static bool TryParseStencilCommand(ref Tokenizer<ShaderToken> t, string name, out PassState state)
    {
        switch (name)
        {
            case "Ref":
                t.Next();
                state = new() { StencilRef = ParserUtility.Integer(ref t) };
                return true;

            case "ReadMask":
                t.Next();
                state = new() { StencilReadMask = (uint)ParserUtility.Integer(ref t) };
                return true;

            case "WriteMask":
                t.Next();
                state = new() { StencilWriteMask = (uint)ParserUtility.Integer(ref t) };
                return true;

            case "Comp":
                t.Next();
                ComparisonKind comp = ParserUtility.Keywords<ComparisonKind>(ref t);
                state = new() { StencilBackFunc = comp, StencilFrontFunc = comp };
                return true;
            case "CompBack":
                t.Next();
                state = new() { StencilBackFunc = ParserUtility.Keywords<ComparisonKind>(ref t) };
                return true;
            case "CompFront":
                t.Next();
                state = new() { StencilFrontFunc = ParserUtility.Keywords<ComparisonKind>(ref t) };
                return true;

            case "Pass":
                t.Next();
                StencilOperation pass = ParserUtility.Keywords<StencilOperation>(ref t);
                state = new() { StencilBackPassOp = pass, StencilFrontPassOp = pass };
                return true;
            case "PassBack":
                t.Next();
                state = new() { StencilBackPassOp = ParserUtility.Keywords<StencilOperation>(ref t) };
                return true;
            case "PassFront":
                t.Next();
                state = new() { StencilFrontPassOp = ParserUtility.Keywords<StencilOperation>(ref t) };
                return true;

            case "Fail":
                t.Next();
                StencilOperation fail = ParserUtility.Keywords<StencilOperation>(ref t);
                state = new() { StencilBackFailOp = fail, StencilFrontFailOp = fail };
                return true;
            case "FailBack":
                t.Next();
                state = new() { StencilBackFailOp = ParserUtility.Keywords<StencilOperation>(ref t) };
                return true;
            case "FailFront":
                t.Next();
                state = new() { StencilFrontFailOp = ParserUtility.Keywords<StencilOperation>(ref t) };
                return true;

            case "ZFail":
                t.Next();
                StencilOperation zfail = ParserUtility.Keywords<StencilOperation>(ref t);
                state = new() { StencilBackDepthFailOp = zfail, StencilFrontDepthFailOp = zfail };
                return true;
            case "ZFailBack":
                t.Next();
                state = new() { StencilBackDepthFailOp = ParserUtility.Keywords<StencilOperation>(ref t) };
                return true;
            case "ZFailFront":
                t.Next();
                state = new() { StencilFrontDepthFailOp = ParserUtility.Keywords<StencilOperation>(ref t) };
                return true;

            default:
                state = default!;
                return false;
        }
    }


    static PassState ParseStencil(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        List<PassState> states = new();

        while (t.Peek().Kind == ShaderToken.Identifier)
        {
            string name = t.Slice(t.Peek()).ToString();
            if (!TryParseStencilCommand(ref t, name, out PassState state))
                break;
            states.Add(state);
        }

        // The loop only stops on an identifier when it names no stencil command; a stencil block
        // otherwise ends at its closing brace, so a leftover identifier is a misspelled command.
        Token<ShaderToken> after = t.Peek();
        if (after.Kind == ShaderToken.Identifier)
            throw Exceptions.UnknownCommand(ParserUtility.Text(ref t, after), after);

        ParserUtility.Expect(ref t, ShaderToken.CloseBrace);
        return FromSeveral(states);
    }


    // Consumes and parses a single render-state command if the identifier names one.
    // Returns false (without consuming) for any unrecognized identifier, which terminates the
    // command loop, e.g. on reaching the SLANGPROGRAM block.
    static bool TryParseRenderCommand(ref Tokenizer<ShaderToken> t, string name, out PassState state)
    {
        switch (name)
        {
            case "AlphaToMask":
                t.Next();
                state = new() { AlphaToMask = ParserUtility.Keywords(ref t, OnStateMap) };
                return true;

            case "BlendOp":
                t.Next();
                BlendFunction blendop = ParserUtility.Keywords<BlendFunction>(ref t);
                state = new() { BlendFunctionRgb = blendop, BlendFunctionAlpha = blendop };
                return true;

            case "Cull":
                t.Next();
                state = new() { CullMode = ParserUtility.Keywords(ref t, FaceCullModeMap) };
                return true;

            case "ZClip":
                t.Next();
                state = new() { EnableDepthClamp = !ParserUtility.Keywords(ref t, OnStateMap) };
                return true;

            case "ZTest":
                t.Next();
                if (ParserUtility.PeekKeyword(ref t, "Disabled"))
                {
                    t.Next();
                    state = new() { EnableDepthTest = false };
                }
                else
                {
                    state = new() { EnableDepthTest = true, DepthFunc = ParserUtility.Keywords<ComparisonKind>(ref t) };
                }
                return true;

            case "ZWrite":
                t.Next();
                state = new() { DepthWriteMask = ParserUtility.Keywords(ref t, OnStateMap) };
                return true;

            case "ColorMask":
                t.Next();
                Token<ShaderToken> mask = ParserUtility.Expect(ref t, ShaderToken.Identifier);
                state = new() { WriteMask = ParseMask(ref t, mask) };
                return true;

            case "Offset":
                t.Next();
                float factor = ParserUtility.Float(ref t);
                float units = ParserUtility.Float(ref t);
                state = new()
                {
                    EnablePolygonOffsetFill = true,
                    PolygonOffsetFactor = factor,
                    PolygonOffsetUnits = units
                };
                return true;

            case "Blend":
                t.Next();
                BlendFactor src = ParserUtility.Keywords<BlendFactor>(ref t);
                BlendFactor dst = ParserUtility.Keywords<BlendFactor>(ref t);
                state = new()
                {
                    EnableBlend = true,
                    BlendSrcRgb = src,
                    BlendSrcAlpha = src,
                    BlendDstRgb = dst,
                    BlendDstAlpha = dst
                };
                return true;

            case "BlendRGB":
                t.Next();
                BlendFactor srcRgb = ParserUtility.Keywords<BlendFactor>(ref t);
                BlendFactor dstRgb = ParserUtility.Keywords<BlendFactor>(ref t);
                state = new() { EnableBlend = true, BlendSrcRgb = srcRgb, BlendDstRgb = dstRgb };
                return true;

            case "BlendAlpha":
                t.Next();
                BlendFactor srcA = ParserUtility.Keywords<BlendFactor>(ref t);
                BlendFactor dstA = ParserUtility.Keywords<BlendFactor>(ref t);
                state = new() { EnableBlend = true, BlendSrcAlpha = srcA, BlendDstAlpha = dstA };
                return true;

            case "Stencil":
                t.Next();
                state = ParseStencil(ref t);
                return true;

            default:
                state = default!;
                return false;
        }
    }


    static PassState FromSeveral(List<PassState> others)
    {
        if (others.Count == 0)
            return new();

        PassState value = others[0];

        for (int i = 1; i < others.Count; i++)
            value = value.Apply(others[i]);

        return value;
    }


    /// <summary>
    /// Parses a pass state as an unordered list of blend, depth, stencil, and raster commands.
    /// </summary>
    public static PassState ParsePassState(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return ParsePassState(ref t);
    }


    internal static PassState ParsePassState(ref Tokenizer<ShaderToken> t)
    {
        List<PassState> states = [];

        while (t.Peek().Kind == ShaderToken.Identifier)
        {
            string name = t.Slice(t.Peek()).ToString();
            if (!TryParseRenderCommand(ref t, name, out PassState state))
                break;
            states.Add(state);
        }

        return FromSeveral(states);
    }


    // ==================== ShaderProperty ====================

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
    public static ShaderProperty ParseProperty(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return ParseProperty(ref t);
    }


    internal static ShaderProperty ParseProperty(ref Tokenizer<ShaderToken> t)
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
