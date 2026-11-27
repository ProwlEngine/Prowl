using Prowl.Prism.Glsl;
using Prowl.Prism.SpirV;

namespace Prowl.Prism.Compiler;

// The sampling built-ins (texture/textureLod/textureGrad/textureProj* and their Offset / ARB Clamp forms),
// lowered the way glslang's TGlslangToSpvTraverser::createImageTextureFunctionCall and
// Builder::createTextureCall do: the call name is cracked into lod/grad/proj/offset/lodClamp, the trailing
// argument is a bias when there is one more argument than the form needs, a shadow sampler's depth
// reference is read from the coordinate (its third component for the projective forms), a projective
// divisor is moved next to the spatial coordinates, and the image operands are emitted in mask-bit order.
public sealed partial class Lowerer
{
    bool _computeDerivatives;   // a compute shader with a derivative_group_* layout keeps implicit LODs

    // A sparse* call lowers through its plain sibling: pending until the sibling's arguments are lowered, then
    // armed for the one sampling instruction it emits.
    (Expr Arg, int Slot)? _sparsePending;
    bool _sparseArmed, _sparseEmitted;
    Access _sparseTexelAccess;

    // The opcode and result type a sampling instruction is emitted with: in a sparse call, its OpImageSparse*
    // form returning the {residency code, texel} struct.
    (Op Op, uint Type) SampleForm(Op op, uint typeId, GlslType result)
    {
        if (!_sparseArmed) return (op, typeId);
        _sparseArmed = false;
        _sparseEmitted = true;
        Op sparse = op switch
        {
            Op.ImageSampleImplicitLod => Op.ImageSparseSampleImplicitLod,
            Op.ImageSampleExplicitLod => Op.ImageSparseSampleExplicitLod,
            Op.ImageSampleDrefImplicitLod => Op.ImageSparseSampleDrefImplicitLod,
            Op.ImageSampleDrefExplicitLod => Op.ImageSparseSampleDrefExplicitLod,
            Op.ImageSampleProjImplicitLod => Op.ImageSparseSampleProjImplicitLod,
            Op.ImageSampleProjExplicitLod => Op.ImageSparseSampleProjExplicitLod,
            Op.ImageSampleProjDrefImplicitLod => Op.ImageSparseSampleProjDrefImplicitLod,
            Op.ImageSampleProjDrefExplicitLod => Op.ImageSparseSampleProjDrefExplicitLod,
            Op.ImageFetch => Op.ImageSparseFetch,
            Op.ImageGather => Op.ImageSparseGather,
            Op.ImageDrefGather => Op.ImageSparseDrefGather,
            Op.ImageRead => Op.ImageSparseRead,
            _ => throw new GlslParseException($"'{op}' has no sparse form"),
        };
        return (sparse, ResTypeStruct2(Types.Int, result));
    }

    // GL_ARB_sparse_texture2 and _clamp: the plain built-in a sparse* name stands for, or null.
    static string? SparseSibling(string name)
    {
        if (!name.StartsWith("sparse", StringComparison.Ordinal) || !name.EndsWith("ARB", StringComparison.Ordinal)) return null;
        string core = name["sparse".Length..^"ARB".Length];
        string plain = char.ToLowerInvariant(core[0]) + core[1..];
        if (plain.EndsWith("Clamp", StringComparison.Ordinal)) plain += "ARB";
        return IsGenericSample(plain) || plain is "texelFetch" or "texelFetchOffset" or "textureGather" or "textureGatherOffset"
            or "textureGatherOffsets" or "imageLoad" ? plain : null;
    }

    // Where the out texel sits in a sparse call: after the arguments its plain sibling requires, before any optional trailing one.
    static int SparseTexelIndex(string plain, GlslType target)
    {
        if (target is ImageType image) return image.Multisampled ? 3 : 2;
        if (target is not SamplerType s) return 2;
        return plain switch
        {
            "texelFetch" => s.Dim is Dim.Buffer or Dim.Rect ? 2 : 3,
            "texelFetchOffset" => s.Dim is Dim.Rect ? 3 : 4,
            "textureGather" => s.Shadow ? 3 : 2,
            "textureGatherOffset" or "textureGatherOffsets" => s.Shadow ? 4 : 3,
            _ => 2 + (s.Dim == Dim.Cube && s.Arrayed && s.Shadow ? 1 : 0) + (plain.Contains("Lod", StringComparison.Ordinal) ? 1 : 0)
                + (plain.Contains("Grad", StringComparison.Ordinal) ? 2 : 0) + (plain.Contains("Offset", StringComparison.Ordinal) ? 1 : 0)
                + (plain.EndsWith("ClampARB", StringComparison.Ordinal) ? 1 : 0),
        };
    }

    static bool IsGenericSample(string name) => name is "texture" or "textureLod" or "textureGrad" or "textureOffset"
        or "textureLodOffset" or "textureGradOffset" or "textureProj" or "textureProjLod" or "textureProjOffset"
        or "textureProjGrad" or "textureProjLodOffset" or "textureProjGradOffset"
        or "textureClampARB" or "textureOffsetClampARB" or "textureGradClampARB" or "textureGradOffsetClampARB";

    Value SampleGeneric(string name, Value[] args)
    {
        var sampler = (SamplerType)args[0].Type;
        bool lod = name.Contains("Lod", StringComparison.Ordinal);
        bool grad = name.Contains("Grad", StringComparison.Ordinal);
        bool proj = name.Contains("Proj", StringComparison.Ordinal);
        bool offset = name.Contains("Offset", StringComparison.Ordinal);
        bool lodClamp = name.EndsWith("ClampARB", StringComparison.Ordinal);
        bool cubeCompare = sampler.Dim == Dim.Cube && sampler.Arrayed && sampler.Shadow;

        int required = 2 + (cubeCompare ? 1 : 0) + (lod ? 1 : 0) + (grad ? 2 : 0) + (offset ? 1 : 0) + (lodClamp ? 1 : 0);
        // GL_EXT_texture_shadow_lod adds the biased cube array shadow form.
        bool biasAllowed = !lod && !grad && (!cubeCompare || _requestedExts.Contains("GL_EXT_texture_shadow_lod"));
        int allowed = required + (biasAllowed ? 1 : 0);
        if (args.Length < required || args.Length > allowed)
            throw new GlslParseException($"'{name}' : no matching overloaded function found");
        // glslang accepts that bias but leaves it off the instruction, so it is read and not emitted.
        bool bias = biasAllowed && !cubeCompare && args.Length > required;

        Value coords = args[1];
        int extra = 0;
        uint dref = 0;
        if (cubeCompare) { dref = args[2].Id; extra++; }
        else if (sampler.Shadow)
        {
            int comp = proj ? 2 : ((VectorType)coords.Type).Size - 1;
            dref = Emit(Op.CompositeExtract, SpvType(new ScalarType(BaseScalar(coords.Type))), coords.Id, (uint)comp);
        }

        Value? lodArg = null;
        bool noImplicitLod = false;
        if (lod) lodArg = args[2 + extra++];
        else if (stage != ShaderStage.Fragment && !(stage == ShaderStage.Compute && _computeDerivatives)) noImplicitLod = true;
        Value? gradX = null, gradY = null;
        if (grad) { gradX = args[2 + extra]; gradY = args[3 + extra]; extra += 2; }
        Value? offsetArg = offset ? args[2 + extra++] : null;
        Value? clampArg = lodClamp ? args[2 + extra++] : null;
        Value? biasArg = bias ? args[2 + extra++] : null;

        uint coordId = coords.Id;
        if (proj && coords.Type is VectorType pv)
        {
            int source = pv.Size - 1;
            int target = sampler.Dim switch { Dim.D1 => 1, Dim.D2 or Dim.Rect => 2, _ => source };
            if (target != source)
            {
                uint comp = Emit(Op.CompositeExtract, SpvType(new ScalarType(pv.Component)), coordId, (uint)source);
                coordId = Emit(Op.CompositeInsert, SpvType(pv), comp, coordId, (uint)target);
            }
        }

        GlslType resultType = sampler.Shadow ? Types.Float : SampleResult(sampler);
        uint resultTypeId = SpvType(resultType);
        var operands = new List<uint>();
        uint mask = 0;
        bool explicitLod = false;
        if (biasArg is { } b) { mask |= (uint)ImageOperands.Bias; operands.Add(b.Id); }
        if (lodArg is { } l) { mask |= (uint)ImageOperands.Lod; operands.Add(l.Id); explicitLod = true; }
        else if (gradX is { } gx) { mask |= (uint)ImageOperands.Grad; operands.Add(gx.Id); operands.Add(gradY!.Value.Id); explicitLod = true; }
        else if (noImplicitLod)
        {
            mask |= (uint)ImageOperands.Lod;
            operands.Add(_m.ConstantFloat(SpvType(Types.Float), 0f));
            explicitLod = true;
        }
        if (offsetArg is { } o)
        {
            if (o.IsConstant) mask |= (uint)ImageOperands.ConstOffset;
            else { _m.AddCapability(Capability.ImageGatherExtended); mask |= (uint)ImageOperands.Offset; }
            operands.Add(o.Id);
        }
        if (clampArg is { } c) { _m.AddCapability(Capability.MinLod); mask |= (uint)ImageOperands.MinLod; operands.Add(c.Id); }

        Op op = (explicitLod, dref != 0, proj) switch
        {
            (true, true, true) => Op.ImageSampleProjDrefExplicitLod,
            (true, true, false) => Op.ImageSampleDrefExplicitLod,
            (true, false, true) => Op.ImageSampleProjExplicitLod,
            (true, false, false) => Op.ImageSampleExplicitLod,
            (false, true, true) => Op.ImageSampleProjDrefImplicitLod,
            (false, true, false) => Op.ImageSampleDrefImplicitLod,
            (false, false, true) => Op.ImageSampleProjImplicitLod,
            _ => Op.ImageSampleImplicitLod,
        };
        (op, resultTypeId) = SampleForm(op, resultTypeId, resultType);
        var inst = InstrPool.Rent(op, resultTypeId, _m.AllocId()).Add(args[0].Id, coordId);
        if (dref != 0) inst.Add(dref);
        if (mask != 0) inst.Add(mask);
        foreach (uint id in operands) inst.Add(id);
        _code.Add(inst);
        return new Value(inst.ResultId, resultType, false);
    }
}
