using Prowl.Prism.Glsl;

namespace Prowl.Prism.Compiler;

// Function-call resolution, mirroring glslang's front end: the implicit-conversion rules
// (TIntermediate::canImplicitlyPromote), overload selection (TParseContextBase::selectFunction with the
// findFunction400 / findFunctionExplicitTypes rankings), and the built-in prototypes those rules run against.
// glslang inserts a conversion node around every argument whose type differs from the selected
// prototype's parameter, folding it into the constant when the argument is one; Prism mirrors that by
// lowering each such argument straight to the parameter type.
public sealed partial class Lowerer
{
    int _glslVersion = 450;
    bool _explicitArithmetic;       // any GL_EXT_shader_explicit_arithmetic_types* extension
    bool _gpuShaderInt16;           // GL_AMD_gpu_shader_int16
    bool _gpuShaderHalfFloat;       // GL_AMD_gpu_shader_half_float
    bool _nvGpuShader5;             // GL_NV_gpu_shader5
    bool _gpuShader5;               // GL_ARB_gpu_shader5
    bool _gpuShaderFp64;            // GL_ARB_gpu_shader_fp64
    bool _implicitConversionsExt;   // GL_EXT_shader_implicit_conversions

    void InitConversionRules(TranslationUnit unit)
    {
        _glslVersion = unit.Version;
        var exts = unit.Extensions ?? [];
        _explicitArithmetic = exts.Any(e => e.StartsWith("GL_EXT_shader_explicit_arithmetic_types", StringComparison.Ordinal));
        _gpuShaderInt16 = exts.Contains("GL_AMD_gpu_shader_int16");
        _gpuShaderHalfFloat = exts.Contains("GL_AMD_gpu_shader_half_float");
        _nvGpuShader5 = exts.Contains("GL_NV_gpu_shader5");
        _gpuShader5 = exts.Contains("GL_ARB_gpu_shader5");
        _gpuShaderFp64 = exts.Contains("GL_ARB_gpu_shader_fp64");
        _implicitConversionsExt = exts.Contains("GL_EXT_shader_implicit_conversions");
    }

    static bool IsIntegralPromotion(Scalar from, Scalar to) =>
        to == Scalar.Int && from is Scalar.Int8 or Scalar.Int16 or Scalar.UInt8 or Scalar.UInt16;

    static bool IsFPPromotion(Scalar from, Scalar to) => to == Scalar.Double && from is Scalar.Half or Scalar.Float;

    bool IsIntegralConversion(Scalar from, Scalar to) => from switch
    {
        Scalar.Int => to == Scalar.UInt ? _glslVersion >= 400 : to is Scalar.Int64 or Scalar.UInt64,
        Scalar.UInt => to is Scalar.Int64 or Scalar.UInt64,
        Scalar.Int8 => to is Scalar.UInt8 or Scalar.Int16 or Scalar.UInt16 or Scalar.UInt or Scalar.Int64 or Scalar.UInt64,
        Scalar.UInt8 => to is Scalar.Int16 or Scalar.UInt16 or Scalar.UInt or Scalar.Int64 or Scalar.UInt64,
        Scalar.Int16 => to is Scalar.UInt16 or Scalar.UInt or Scalar.Int64 or Scalar.UInt64,
        Scalar.UInt16 => to is Scalar.UInt or Scalar.Int64 or Scalar.UInt64,
        Scalar.Int64 => to == Scalar.UInt64,
        _ => false,
    };

    static bool IsFPConversion(Scalar from, Scalar to) => to == Scalar.Float && from == Scalar.Half;

    static bool IsFPIntegralConversion(Scalar from, Scalar to) => from switch
    {
        Scalar.Int or Scalar.UInt => to is Scalar.Float or Scalar.Double,
        Scalar.Int8 or Scalar.UInt8 or Scalar.Int16 or Scalar.UInt16 => to is Scalar.Half or Scalar.Float or Scalar.Double,
        Scalar.Int64 or Scalar.UInt64 => to == Scalar.Double,
        _ => false,
    };

    // glslang TIntermediate::canImplicitlyPromote for GLSL source.
    bool CanPromote(Scalar from, Scalar to)
    {
        if ((_isEs && _glslVersion < 310) || _glslVersion == 110) return false;
        if (from == to) return true;
        if (_explicitArithmetic && (IsIntegralPromotion(from, to) || IsFPPromotion(from, to) || IsIntegralConversion(from, to)
                || IsFPConversion(from, to) || IsFPIntegralConversion(from, to)))
            return true;
        if (_isEs)
            return to switch
            {
                Scalar.Float => from is Scalar.Int or Scalar.UInt && _implicitConversionsExt,
                Scalar.UInt => from == Scalar.Int && _implicitConversionsExt,
                _ => false,
            };
        bool fp64 = _glslVersion >= 400 || _gpuShaderFp64;
        return to switch
        {
            Scalar.Double => from switch
            {
                Scalar.Int or Scalar.UInt or Scalar.Int64 or Scalar.UInt64 or Scalar.Float => fp64,
                Scalar.Int16 or Scalar.UInt16 => fp64 && (_nvGpuShader5 || _gpuShaderInt16),
                Scalar.Half => fp64 && (_nvGpuShader5 || _gpuShaderHalfFloat),
                Scalar.Int8 or Scalar.UInt8 => _nvGpuShader5,
                _ => false,
            },
            Scalar.Float => from switch
            {
                Scalar.Int or Scalar.UInt => true,
                Scalar.Int16 or Scalar.UInt16 => _gpuShaderInt16 || _nvGpuShader5,
                Scalar.Half => _gpuShaderHalfFloat || _nvGpuShader5,
                Scalar.Int8 or Scalar.UInt8 => _nvGpuShader5,
                _ => false,
            },
            Scalar.UInt => from switch
            {
                Scalar.Int => _glslVersion >= 400 || _gpuShader5 || _nvGpuShader5,
                Scalar.Int16 or Scalar.UInt16 => _gpuShaderInt16 || _nvGpuShader5,
                Scalar.Int8 or Scalar.UInt8 => _nvGpuShader5,
                _ => false,
            },
            Scalar.Int => from switch
            {
                Scalar.Int16 => _gpuShaderInt16 || _nvGpuShader5,
                Scalar.Int8 => _nvGpuShader5,
                _ => false,
            },
            Scalar.UInt64 => from switch
            {
                Scalar.Int or Scalar.UInt or Scalar.Int64 => true,
                Scalar.Int16 or Scalar.UInt16 => _gpuShaderInt16 || _nvGpuShader5,
                Scalar.Int8 or Scalar.UInt8 => _nvGpuShader5,
                _ => false,
            },
            Scalar.Int64 => from switch
            {
                Scalar.Int => true,
                Scalar.Int8 => _nvGpuShader5,
                Scalar.Int16 => _gpuShaderInt16 || _nvGpuShader5,
                _ => false,
            },
            Scalar.Half => from is Scalar.Int16 or Scalar.UInt16 && _gpuShaderInt16,
            Scalar.UInt16 => from == Scalar.Int16 && _gpuShaderInt16,
            _ => false,
        };
    }

    // glslang TIntermediate::getConversionDestinationType: the base type both operands of a binary operator
    // convert to, or null when neither can be implicitly converted to the other.
    Scalar? ConversionDestination(Scalar a, Scalar b)
    {
        if (a == b) return a;
        if ((_isEs && (_glslVersion < 310 || !_implicitConversionsExt)) || _glslVersion == 110) return null;
        foreach (var f in new[] { Scalar.Double, Scalar.Float, Scalar.Half })
            if ((a == f && CanPromote(b, f)) || (b == f && CanPromote(a, f))) return f;
        if (!IsIntegral(a) || !IsIntegral(b) || !(CanPromote(a, b) || CanPromote(b, a))) return null;
        int ra = IntWidth(a), rb = IntWidth(b);
        if (IsSigned(a) == IsSigned(b)) return ra < rb ? b : a;
        if (IsUnsigned(a) && ra > rb) return a;
        if (IsUnsigned(b) && rb > ra) return b;
        // The signed one is at least as wide: it keeps its type if it can hold every value of the
        // unsigned one, else both become its unsigned counterpart.
        var (signed, unsigned) = IsSigned(a) ? (a, b) : (b, a);
        return IntWidth(signed) > IntWidth(unsigned) ? signed : UnsignedOfWidth(IntWidth(signed));
    }

    // A scalar/vector/matrix type with its component replaced.
    static GlslType WithBase(GlslType t, Scalar s) => t switch
    {
        ScalarType => new ScalarType(s),
        VectorType v => new VectorType(s, v.Size),
        MatrixType m => new MatrixType(m.Columns, m.Rows, s),
        _ => t,
    };

    // glslang's `convertible` lambda: identical types, or the same element shape with a promotable base.
    bool ParamConvertible(GlslType from, GlslType to)
    {
        if (from.Equals(to)) return true;
        bool sameShape = (from, to) switch
        {
            (ScalarType, ScalarType) => true,
            (VectorType a, VectorType b) => a.Size == b.Size,
            (MatrixType a, MatrixType b) => a.Columns == b.Columns && a.Rows == b.Rows,
            _ => false,
        };
        return sameShape && CanPromote(BaseScalar(from), BaseScalar(to));
    }

    // Is `to2` a better conversion target for `from` than `to1`? (findFunction400 / findFunctionExplicitTypes)
    bool BetterConversion(GlslType from, GlslType to1, GlslType to2)
    {
        if (from.Equals(to2)) return !from.Equals(to1);
        if (from.Equals(to1)) return false;
        Scalar f = BaseScalar(from), t1 = BaseScalar(to1), t2 = BaseScalar(to2);
        if (!_explicitArithmetic)
        {
            if (f == Scalar.Float && t2 == Scalar.Double && t1 != Scalar.Double) return true;
            return t2 == Scalar.Float && t1 == Scalar.Double;
        }
        bool promo1 = IsIntegralPromotion(f, t1) || IsFPPromotion(f, t1);
        bool promo2 = IsIntegralPromotion(f, t2) || IsFPPromotion(f, t2);
        if (promo2) return !promo1;
        if (promo1) return false;
        bool conv1 = IsIntegralConversion(f, t1) || IsFPConversion(f, t1) || IsFPIntegralConversion(f, t1);
        bool conv2 = IsIntegralConversion(f, t2) || IsFPConversion(f, t2) || IsFPIntegralConversion(f, t2);
        return conv2 && !conv1;
    }

    // glslang TParseContextBase::selectFunction over candidates with matching arity: the index of the best
    // viable candidate (or -1 when none is viable) and whether another candidate ties with it.
    int SelectCandidate(IReadOnlyList<GlslType> args, IReadOnlyList<(GlslType Type, bool In, bool Out)[]> candidates, out bool tie)
    {
        tie = false;
        var viable = new List<int>();
        for (int c = 0; c < candidates.Count; c++)
        {
            var ps = candidates[c];
            if (ps.Length != args.Count) continue;
            bool ok = true;
            for (int i = 0; i < ps.Length && ok; i++)
            {
                if (ps[i].In && !ParamConvertible(args[i], ps[i].Type)) ok = false;
                if (ps[i].Out && !ParamConvertible(ps[i].Type, args[i])) ok = false;
            }
            if (ok) viable.Add(c);
        }
        if (viable.Count == 0) return -1;
        if (viable.Count == 1) return viable[0];

        bool BetterParam(int c1, int c2)
        {
            for (int i = 0; i < args.Count; i++)
                if (BetterConversion(args[i], candidates[c1][i].Type, candidates[c2][i].Type)) return true;
            return false;
        }
        bool Equivalent(int c1, int c2)
        {
            for (int i = 0; i < args.Count; i++)
                if (BetterConversion(args[i], candidates[c1][i].Type, candidates[c2][i].Type)
                    || BetterConversion(args[i], candidates[c2][i].Type, candidates[c1][i].Type))
                    return false;
            return true;
        }
        int incumbent = viable[0];
        foreach (int c in viable.Skip(1))
            if (BetterParam(incumbent, c) && !BetterParam(c, incumbent)) incumbent = c;
        foreach (int c in viable)
            if (c != incumbent && (BetterParam(incumbent, c) || Equivalent(incumbent, c))) tie = true;
        return incumbent;
    }

    // ---- built-in prototypes ----

    // One family of built-in prototypes: per parameter a pattern letter, instantiated for each base scalar
    // and each genType size in [MinSize, MaxSize]. G = the genType, S = its scalar, B = bool genType of the
    // same size, I = int genType of the same size, J = int16 genType of the same size. Ret uses the same letters.
    sealed record BuiltinProto(string Params, string Ret, Scalar[] Bases, int MinSize = 1, int MaxSize = 4);

    static readonly Scalar[] BasesFH = [Scalar.Float, Scalar.Half];
    static readonly Scalar[] BasesFDH = [Scalar.Float, Scalar.Double, Scalar.Half];
    static readonly Scalar[] BasesFD = [Scalar.Float, Scalar.Double];
    static readonly Scalar[] BasesInt = [Scalar.Int, Scalar.UInt, Scalar.Int64, Scalar.UInt64, Scalar.Int8, Scalar.UInt8, Scalar.Int16, Scalar.UInt16];
    static readonly Scalar[] BasesSignedInt = [Scalar.Int, Scalar.Int64, Scalar.Int8, Scalar.Int16];
    static readonly Scalar[] BasesNumeric = [.. BasesFDH, .. BasesInt];
    static readonly Scalar[] BasesMixSelect = [.. BasesFDH, .. BasesInt, Scalar.Bool];

    static readonly Dictionary<string, BuiltinProto[]> BuiltinProtos = BuildBuiltinProtos();

    static Dictionary<string, BuiltinProto[]> BuildBuiltinProtos()
    {
        var d = new Dictionary<string, BuiltinProto[]>();
        foreach (var n in new[] { "radians", "degrees", "sin", "cos", "tan", "asin", "acos", "exp", "log", "exp2", "log2",
                     "sinh", "cosh", "tanh", "asinh", "acosh", "atanh", "dFdx", "dFdy", "fwidth", "dFdxFine", "dFdyFine",
                     "dFdxCoarse", "dFdyCoarse", "fwidthFine", "fwidthCoarse" })
            d[n] = [new("G", "G", BasesFH)];
        d["atan"] = [new("G", "G", BasesFH), new("GG", "G", BasesFH)];
        d["pow"] = [new("GG", "G", BasesFH)];
        foreach (var n in new[] { "sqrt", "inversesqrt", "floor", "ceil", "fract", "trunc", "round", "roundEven", "normalize" })
            d[n] = [new("G", "G", BasesFDH)];
        d["abs"] = d["sign"] = [new("G", "G", [.. BasesFDH, .. BasesSignedInt])];
        d["length"] = [new("G", "S", BasesFDH)];
        d["isnan"] = d["isinf"] = [new("G", "B", BasesFDH)];
        d["mod"] = [new("GG", "G", BasesFDH), new("GS", "G", BasesFDH, 2)];
        d["min"] = d["max"] = [new("GG", "G", BasesNumeric), new("GS", "G", BasesNumeric, 2)];
        d["clamp"] = [new("GGG", "G", BasesNumeric), new("GSS", "G", BasesNumeric, 2)];
        d["mix"] = [new("GGG", "G", BasesFDH), new("GGS", "G", BasesFDH, 2), new("GGB", "G", BasesMixSelect)];
        d["step"] = [new("GG", "G", BasesFDH), new("SG", "G", BasesFDH, 2)];
        d["smoothstep"] = [new("GGG", "G", BasesFDH), new("SSG", "G", BasesFDH, 2)];
        d["dot"] = d["distance"] = [new("GG", "S", BasesFDH)];
        d["reflect"] = [new("GG", "G", BasesFDH)];
        d["faceforward"] = [new("GGG", "G", BasesFDH)];
        d["refract"] = [new("GGS", "G", BasesFDH)];
        d["cross"] = [new("GG", "G", BasesFDH, 3, 3)];
        d["fma"] = [new("GGG", "G", BasesFDH)];
        d["ldexp"] = [new("GI", "G", BasesFDH), new("GJ", "G", [Scalar.Half])];
        return d;
    }

    static GlslType ProtoType(char letter, Scalar b, int n) => letter switch
    {
        'G' => n == 1 ? new ScalarType(b) : new VectorType(b, n),
        'S' => new ScalarType(b),
        'B' => n == 1 ? Types.Bool : new VectorType(Scalar.Bool, n),
        'I' => n == 1 ? Types.Int : new VectorType(Scalar.Int, n),
        'J' => n == 1 ? Types.Int16 : new VectorType(Scalar.Int16, n),
        _ => throw new InvalidOperationException($"unknown prototype letter '{letter}'"),
    };

    // A mangled-name sort key, so candidates are walked in glslang's symbol-table (std::map) order.
    static string ProtoMangle(GlslType[] ps)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in ps)
        {
            Scalar s = BaseScalar(p);
            string m = s switch
            {
                Scalar.Int8 => "i8", Scalar.UInt8 => "u8", Scalar.Int16 => "i16", Scalar.UInt16 => "u16",
                _ => ScalarMangle(s),
            };
            if (p is VectorType v) sb.Append('v').Append(m).Append(v.Size);
            else sb.Append(m).Append('1');
            sb.Append(';');
        }
        return sb.ToString();
    }

    // The concrete prototypes glslang declares for a tabled built-in, with their return types, in symbol
    // table order; null for a built-in this table does not model.
    List<(GlslType[] Params, GlslType Ret)>? BuiltinPrototypes(string name, int argCount)
    {
        if (!BuiltinProtos.TryGetValue(name, out var protos)) return null;
        var list = new List<(GlslType[] Params, GlslType Ret)>();
        foreach (var p in protos)
        {
            if (p.Params.Length != argCount) continue;
            foreach (var b in p.Bases)
            {
                // Double prototypes exist on desktop only; the 64-bit and narrow integer and half ones from 450 (ES 310).
                if (b == Scalar.Double && (_isEs || _glslVersion < 150)) continue;
                if (b is not (Scalar.Float or Scalar.Double or Scalar.Int or Scalar.UInt or Scalar.Bool)
                    && !(_isEs ? _glslVersion >= 310 : _glslVersion >= 450)) continue;
                for (int n = p.MinSize; n <= p.MaxSize; n++)
                    list.Add(([.. p.Params.Select(c => ProtoType(c, b, n))], ProtoType(p.Ret[0], b, n)));
            }
        }
        list.Sort((x, y) => string.CompareOrdinal(ProtoMangle(x.Params), ProtoMangle(y.Params)));
        return list;
    }

    // The prototype a call to a tabled built-in resolves to, or null when the table does not model the
    // function or nothing matches (the call then lowers its arguments as written).
    (GlslType[] Params, GlslType Ret)? ResolveBuiltinPrototype(Call call)
    {
        if (!BuiltinProtos.ContainsKey(call.Name)) return null;
        var protos = BuiltinPrototypes(call.Name, call.Args.Count);
        if (protos is null || protos.Count == 0) return null;
        List<GlslType> argTypes;
        try { argTypes = [.. call.Args.Select(InferType)]; }
        catch (GlslParseException) { return null; }
        foreach (var p in protos)
            if (p.Params.Zip(argTypes).All(t => t.First.Equals(t.Second))) return p;
        int best = SelectCandidate(argTypes, [.. protos.Select(p => p.Params.Select(t => (t, true, false)).ToArray())], out _);
        return best < 0 ? null : protos[best];
    }

    // The parameter type each argument of a built-in call converts to (null entries keep the argument as
    // written), or null when no argument needs converting.
    GlslType?[]? BuiltinArgTargets(Call call)
    {
        if (ResolveBuiltinPrototype(call) is { } proto) return proto.Params;
        return TextureArgTargets(call);
    }

    // The sampling built-ins take float coordinates, LOD, bias, gradients and depth references (every
    // non-offset, non-component argument after the sampler); an integer one converts to float.
    GlslType?[]? TextureArgTargets(Call call)
    {
        if (!call.Name.StartsWith("texture", StringComparison.Ordinal)
            || call.Name is "textureSize" or "textureQueryLevels" or "textureSamples" || call.Args.Count < 2)
            return null;
        GlslType?[]? targets = null;
        bool shadow;
        try { shadow = InferType(call.Args[0]) is SamplerType { Shadow: true }; }
        catch (GlslParseException) { return null; }
        bool gather = call.Name.StartsWith("textureGather", StringComparison.Ordinal);
        int offsetIdx = call.Name switch
        {
            "textureOffset" or "textureProjOffset" or "textureOffsetClampARB" => 2,
            "textureLodOffset" or "textureProjLodOffset" => 3,
            "textureGradOffset" or "textureProjGradOffset" or "textureGradOffsetClampARB" => 4,
            "textureGatherOffset" or "textureGatherOffsets" => shadow ? 3 : 2,
            _ => -1,
        };
        for (int i = 1; i < call.Args.Count; i++)
        {
            if (i == offsetIdx) continue;
            // A non-shadow gather's trailing argument is the (int) component select.
            if (gather && !shadow && i == (offsetIdx >= 0 ? offsetIdx + 1 : 2)) continue;
            GlslType t;
            try { t = InferType(call.Args[i]); }
            catch (GlslParseException) { continue; }
            if (t is ScalarType or VectorType && IsIntegral(BaseScalar(t)) && CanPromote(BaseScalar(t), Scalar.Float))
                (targets ??= new GlslType?[call.Args.Count])[i] = ReplaceComponent(t, Scalar.Float);
        }
        return targets;
    }

    // Lowers a built-in call's arguments, each converted to its resolved parameter type.
    // A sparse call's out texel is resolved where it stands in the source, before the argument it preceded.
    Value[] LowerBuiltinArgs(Call call, (Expr Arg, int Slot)? sparseTexel = null)
    {
        var targets = BuiltinArgTargets(call);
        bool imageOp = IsImageOrTextureBuiltin(call.Name);
        var args = new Value[call.Args.Count];
        for (int i = 0; i < args.Length; i++)
        {
            bool afterTexel = sparseTexel is { Slot: var slot } && slot == i;
            if (afterTexel) _sparseTexelAccess = ResolveAccess(sparseTexel.Value.Arg);
            if (imageOp && !afterTexel && RepeatsPreviousSymbol(call, i, j => targets?[j])) args[i] = args[i - 1];
            else args[i] = targets?[i] is { } t ? LowerArgAs(call.Args[i], t, $"argument {i + 1} of '{call.Name}'") : LowerExpr(call.Args[i]);
        }
        if (sparseTexel is { Slot: var last } && last == args.Length) _sparseTexelAccess = ResolveAccess(sparseTexel.Value.Arg);
        return args;
    }

    // The built-ins glslang lowers as image or texture operations (its isImage and isTexture opcode ranges).
    static bool IsImageOrTextureBuiltin(string name) =>
        name.StartsWith("texture", StringComparison.Ordinal) || name.StartsWith("texel", StringComparison.Ordinal)
        || name.StartsWith("image", StringComparison.Ordinal) || name.StartsWith("sparse", StringComparison.Ordinal)
        || name is "subpassLoad" or "fragmentFetchAMD" or "fragmentMaskFetchAMD" or "colorAttachmentReadEXT";

    // glslang's translateArguments passes the previous argument's id again when this argument and the one
    // before it are the same variable symbol, so neither may be wrapped in a conversion to its parameter type.
    bool RepeatsPreviousSymbol(Call call, int i, Func<int, GlslType?> target)
    {
        if (i == 0 || call.Args[i] is not Ident cur || call.Args[i - 1] is not Ident prev || cur.Name != prev.Name) return false;
        if (ResolveLocalVar(cur.Name) is null && ResolveGlobal(cur.Name) is null && !Builtins.ContainsKey(cur.Name)) return false;
        var type = InferType(cur);
        return (target(i) is not { } t || t.Equals(type)) && (target(i - 1) is not { } p || p.Equals(type));
    }

    // An argument converted to a parameter type: a constant folds into a constant of that type, anything
    // else converts right after it is evaluated (glslang's conversion node wraps the argument).
    Value LowerArgAs(Expr e, GlslType target, string context)
    {
        GlslType t;
        try { t = InferType(e); }
        catch (GlslParseException) { return LowerExpr(e); }
        if (t.Equals(target)) return LowerExpr(e);
        if (!UsesSpecConstant(e) && EvalConst(e) is { } cv && FoldConversion(cv, target) is { } folded)
            return Materialize(folded);
        return CoerceOrThrow(LowerExpr(e), target, context);
    }

    // Folded argument values converted to the resolved prototype's parameter types (null if one cannot be).
    List<CVal>? ConvertConstArgs(Call call, List<CVal> args)
    {
        var targets = BuiltinArgTargets(call);
        if (targets is null) return args;
        var result = new List<CVal>(args.Count);
        for (int i = 0; i < args.Count; i++)
        {
            if (targets[i] is not { } t || CValType(args[i]).Equals(t)) { result.Add(args[i]); continue; }
            if (FoldConversion(args[i], t) is not { } c) return null;
            result.Add(c);
        }
        return result;
    }
}
