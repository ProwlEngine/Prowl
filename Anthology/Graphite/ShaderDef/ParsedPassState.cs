using System.Collections.Generic;

using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


public class ParsedPassState
{
    public bool? EnableCulling;
    public FaceCullMode? CullMode;
    public FrontFace? FrontFace;

    public bool? EnablePolygonOffsetFill;
    public float? PolygonOffsetFactor;
    public float? PolygonOffsetUnits;

    // -------------------- Depth --------------------

    public ComparisonKind? DepthFunc;
    public bool? DepthWriteMask;
    public bool? EnableDepthClamp;

    // -------------------- Stencil --------------------

    public bool? EnableStencilTest;
    public ComparisonKind? StencilFrontFunc;
    public int? StencilFrontRef;
    public uint? StencilFrontReadMask;
    public StencilOperation? StencilFrontFailOp;
    public StencilOperation? StencilFrontDepthFailOp;
    public StencilOperation? StencilFrontPassOp;
    public uint? StencilFrontWriteMask;

    public ComparisonKind? StencilBackFunc;
    public int? StencilBackRef;
    public uint? StencilBackReadMask;
    public StencilOperation? StencilBackFailOp;
    public StencilOperation? StencilBackDepthFailOp;
    public StencilOperation? StencilBackPassOp;
    public uint? StencilBackWriteMask;

    // -------------------- Blending (equation / factors) --------------------
    public bool? EnableBlend;
    public BlendFunction? BlendFunctionRgb;
    public BlendFunction? BlendFunctionAlpha;
    public BlendFactor? BlendSrcRgb;
    public BlendFactor? BlendDstRgb;
    public BlendFactor? BlendSrcAlpha;
    public BlendFactor? BlendDstAlpha;

    // -------------------- Multisampling --------------------

    public bool? AlphaToMask;

    // -------------------- Color Write Mask --------------------
    public ColorWriteMask? WriteMask;


    public ParsedPassState Apply(ParsedPassState other)
    {
        return new()
        {
            EnableCulling = EnableCulling ?? other.EnableCulling,
            CullMode = CullMode ?? other.CullMode,
            FrontFace = FrontFace ?? other.FrontFace,
            EnablePolygonOffsetFill = EnablePolygonOffsetFill ?? other.EnablePolygonOffsetFill,
            PolygonOffsetFactor = PolygonOffsetFactor ?? other.PolygonOffsetFactor,
            PolygonOffsetUnits = PolygonOffsetUnits ?? other.PolygonOffsetUnits,
            DepthFunc = DepthFunc ?? other.DepthFunc,
            DepthWriteMask = DepthWriteMask ?? other.DepthWriteMask,
            EnableDepthClamp = EnableDepthClamp ?? other.EnableDepthClamp,
            EnableStencilTest = EnableStencilTest ?? other.EnableStencilTest,
            StencilFrontFunc = StencilFrontFunc ?? other.StencilFrontFunc,
            StencilFrontRef = StencilFrontRef ?? other.StencilFrontRef,
            StencilFrontReadMask = StencilFrontReadMask ?? other.StencilFrontReadMask,
            StencilFrontFailOp = StencilFrontFailOp ?? other.StencilFrontFailOp,
            StencilFrontDepthFailOp = StencilFrontDepthFailOp ?? other.StencilFrontDepthFailOp,
            StencilFrontPassOp = StencilFrontPassOp ?? other.StencilFrontPassOp,
            StencilFrontWriteMask = StencilFrontWriteMask ?? other.StencilFrontWriteMask,
            StencilBackFunc = StencilBackFunc ?? other.StencilBackFunc,
            StencilBackRef = StencilBackRef ?? other.StencilBackRef,
            StencilBackReadMask = StencilBackReadMask ?? other.StencilBackReadMask,
            StencilBackFailOp = StencilBackFailOp ?? other.StencilBackFailOp,
            StencilBackDepthFailOp = StencilBackDepthFailOp ?? other.StencilBackDepthFailOp,
            StencilBackPassOp = StencilBackPassOp ?? other.StencilBackPassOp,
            StencilBackWriteMask = StencilBackWriteMask ?? other.StencilBackWriteMask,
            EnableBlend = EnableBlend ?? other.EnableBlend,
            BlendFunctionRgb = BlendFunctionRgb ?? other.BlendFunctionRgb,
            BlendFunctionAlpha = BlendFunctionAlpha ?? other.BlendFunctionAlpha,
            BlendSrcRgb = BlendSrcRgb ?? other.BlendSrcRgb,
            BlendDstRgb = BlendDstRgb ?? other.BlendDstRgb,
            BlendSrcAlpha = BlendSrcAlpha ?? other.BlendSrcAlpha,
            BlendDstAlpha = BlendDstAlpha ?? other.BlendDstAlpha,
            AlphaToMask = AlphaToMask ?? other.AlphaToMask,
            WriteMask = WriteMask ?? other.WriteMask,
        };
    }


    static ParsedPassState FromSeveral(List<ParsedPassState> others)
    {
        if (others.Count == 0)
            return new();

        ParsedPassState value = others[0];

        for (int i = 1; i < others.Count; i++)
            value = value.Apply(others[i]);

        return value;
    }


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
    static bool TryParseStencilCommand(ref Tokenizer<ShaderToken> t, string name, out ParsedPassState state)
    {
        switch (name)
        {
            case "Ref":
                t.Next();
                int stencilref = ParserUtility.Integer(ref t);
                state = new() { StencilBackRef = stencilref, StencilFrontRef = stencilref };
                return true;

            case "ReadMask":
                t.Next();
                int readmask = ParserUtility.Integer(ref t);
                state = new() { StencilBackReadMask = (uint)readmask, StencilFrontReadMask = (uint)readmask };
                return true;

            case "WriteMask":
                t.Next();
                int writemask = ParserUtility.Integer(ref t);
                state = new() { StencilBackWriteMask = (uint)writemask, StencilFrontWriteMask = (uint)writemask };
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


    static ParsedPassState ParseStencil(ref Tokenizer<ShaderToken> t)
    {
        ParserUtility.Expect(ref t, ShaderToken.OpenBrace);

        List<ParsedPassState> states = new();

        while (t.Peek().Kind == ShaderToken.Identifier)
        {
            string name = t.Slice(t.Peek()).ToString();
            if (!TryParseStencilCommand(ref t, name, out ParsedPassState state))
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
    static bool TryParseRenderCommand(ref Tokenizer<ShaderToken> t, string name, out ParsedPassState state)
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
                state = new() { DepthFunc = ParserUtility.Keywords<ComparisonKind>(ref t) };
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
                state = new() { BlendSrcRgb = srcRgb, BlendDstRgb = dstRgb };
                return true;

            case "BlendAlpha":
                t.Next();
                BlendFactor srcA = ParserUtility.Keywords<BlendFactor>(ref t);
                BlendFactor dstA = ParserUtility.Keywords<BlendFactor>(ref t);
                state = new() { BlendSrcAlpha = srcA, BlendDstAlpha = dstA };
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


    public static ParsedPassState Parse(ref Tokenizer<ShaderToken> t)
    {
        List<ParsedPassState> states = new();

        while (t.Peek().Kind == ShaderToken.Identifier)
        {
            string name = t.Slice(t.Peek()).ToString();
            if (!TryParseRenderCommand(ref t, name, out ParsedPassState state))
                break;
            states.Add(state);
        }

        return FromSeveral(states);
    }
}
