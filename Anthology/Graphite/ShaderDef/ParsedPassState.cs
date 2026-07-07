using System.Collections.Generic;

using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// A defined set of all possible render state options, encapsulating rasterizer settings, blend, depth, stencil, multisampling, and write masks.
/// </summary>
public class ParsedPassState
{
#pragma warning disable CS1591
    public bool? EnableCulling;
    public FaceCullMode? CullMode;
    public FrontFace? FrontFace;

    public bool? EnablePolygonOffsetFill;
    public float? PolygonOffsetFactor;
    public float? PolygonOffsetUnits;

    // -------------------- Depth --------------------

    public bool? EnableDepthTest;
    public ComparisonKind? DepthFunc;
    public bool? DepthWriteMask;
    public bool? EnableDepthClamp;

    // -------------------- Stencil --------------------

    public bool? EnableStencilTest;
    public int? StencilRef;
    public uint? StencilReadMask;
    public uint? StencilWriteMask;

    public ComparisonKind? StencilFrontFunc;
    public StencilOperation? StencilFrontFailOp;
    public StencilOperation? StencilFrontDepthFailOp;
    public StencilOperation? StencilFrontPassOp;

    public ComparisonKind? StencilBackFunc;
    public StencilOperation? StencilBackFailOp;
    public StencilOperation? StencilBackDepthFailOp;
    public StencilOperation? StencilBackPassOp;

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
#pragma warning restore CS1591


    /// <summary>
    /// Collapses the blend-related fields into a single-attachment <see cref="BlendStateDescription"/>, overlaying any
    /// explicitly parsed values onto the given base description.
    /// </summary>
    public BlendStateDescription ToBlendState(BlendStateDescription baseState)
    {
        BlendAttachmentDescription attachment = baseState.AttachmentStates.Length > 0
            ? baseState.AttachmentStates[0]
            : BlendAttachmentDescription.Disabled;

        attachment.BlendEnabled = EnableBlend ?? attachment.BlendEnabled;
        attachment.ColorWriteMask = WriteMask ?? attachment.ColorWriteMask;
        attachment.SourceColorFactor = BlendSrcRgb ?? attachment.SourceColorFactor;
        attachment.DestinationColorFactor = BlendDstRgb ?? attachment.DestinationColorFactor;
        attachment.ColorFunction = BlendFunctionRgb ?? attachment.ColorFunction;
        attachment.SourceAlphaFactor = BlendSrcAlpha ?? attachment.SourceAlphaFactor;
        attachment.DestinationAlphaFactor = BlendDstAlpha ?? attachment.DestinationAlphaFactor;
        attachment.AlphaFunction = BlendFunctionAlpha ?? attachment.AlphaFunction;

        baseState.AlphaToCoverageEnabled = AlphaToMask ?? baseState.AlphaToCoverageEnabled;
        baseState.AttachmentStates = [attachment];
        return baseState;
    }


    /// <summary>
    /// Collapses the depth and stencil fields into a <see cref="DepthStencilStateDescription"/>, overlaying any explicitly
    /// parsed values onto the given base description.
    /// </summary>
    public DepthStencilStateDescription ToDepthStencilState(DepthStencilStateDescription baseState)
    {
        baseState.DepthTestEnabled = EnableDepthTest ?? baseState.DepthTestEnabled;
        baseState.DepthWriteEnabled = DepthWriteMask ?? baseState.DepthWriteEnabled;
        baseState.DepthComparison = DepthFunc ?? baseState.DepthComparison;
        baseState.StencilTestEnabled = EnableStencilTest ?? baseState.StencilTestEnabled;

        baseState.StencilFront = new StencilBehaviorDescription
        {
            Fail = StencilFrontFailOp ?? baseState.StencilFront.Fail,
            Pass = StencilFrontPassOp ?? baseState.StencilFront.Pass,
            DepthFail = StencilFrontDepthFailOp ?? baseState.StencilFront.DepthFail,
            Comparison = StencilFrontFunc ?? baseState.StencilFront.Comparison,
        };
        baseState.StencilBack = new StencilBehaviorDescription
        {
            Fail = StencilBackFailOp ?? baseState.StencilBack.Fail,
            Pass = StencilBackPassOp ?? baseState.StencilBack.Pass,
            DepthFail = StencilBackDepthFailOp ?? baseState.StencilBack.DepthFail,
            Comparison = StencilBackFunc ?? baseState.StencilBack.Comparison,
        };

        baseState.StencilReadMask = (byte)(StencilReadMask ?? baseState.StencilReadMask);
        baseState.StencilWriteMask = (byte)(StencilWriteMask ?? baseState.StencilWriteMask);
        baseState.StencilReference = (uint)(StencilRef ?? (int)baseState.StencilReference);
        return baseState;
    }


    /// <summary>
    /// Collapses the rasterizer fields into a <see cref="RasterizerStateDescription"/>, overlaying any explicitly parsed
    /// values onto the given base description.
    /// </summary>
    public RasterizerStateDescription ToRasterizerState(RasterizerStateDescription baseState)
    {
        baseState.CullMode = CullMode ?? baseState.CullMode;
        baseState.FrontFace = FrontFace ?? baseState.FrontFace;

        if (EnableDepthClamp.HasValue)
            baseState.DepthClipEnabled = !EnableDepthClamp.Value;

        return baseState;
    }


    /// <summary>
    /// Merges two <see cref="ParsedPassState"/> objects into a single merged <see cref="ParsedPassState"/> with values being overwritten on <paramref name="other"/>
    /// </summary>
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
            EnableDepthTest = EnableDepthTest ?? other.EnableDepthTest,
            DepthFunc = DepthFunc ?? other.DepthFunc,
            DepthWriteMask = DepthWriteMask ?? other.DepthWriteMask,
            EnableDepthClamp = EnableDepthClamp ?? other.EnableDepthClamp,
            EnableStencilTest = EnableStencilTest ?? other.EnableStencilTest,
            StencilRef = StencilRef ?? other.StencilRef,
            StencilReadMask = StencilReadMask ?? other.StencilReadMask,
            StencilWriteMask = StencilWriteMask ?? other.StencilWriteMask,
            StencilFrontFunc = StencilFrontFunc ?? other.StencilFrontFunc,
            StencilFrontFailOp = StencilFrontFailOp ?? other.StencilFrontFailOp,
            StencilFrontDepthFailOp = StencilFrontDepthFailOp ?? other.StencilFrontDepthFailOp,
            StencilFrontPassOp = StencilFrontPassOp ?? other.StencilFrontPassOp,
            StencilBackFunc = StencilBackFunc ?? other.StencilBackFunc,
            StencilBackFailOp = StencilBackFailOp ?? other.StencilBackFailOp,
            StencilBackDepthFailOp = StencilBackDepthFailOp ?? other.StencilBackDepthFailOp,
            StencilBackPassOp = StencilBackPassOp ?? other.StencilBackPassOp,
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


    /// <summary>
    /// Parses a pass state as an unordered list of blend, depth, stencil, and raster commands.
    /// </summary>
    public static ParsedPassState Parse(string source)
    {
        Tokenizer<ShaderToken> t = ShaderTokenizer.Create(source);
        return Parse(ref t);
    }


    internal static ParsedPassState Parse(ref Tokenizer<ShaderToken> t)
    {
        List<ParsedPassState> states = [];

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
