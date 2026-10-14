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
        int allowed = required + (!lod && !grad && !cubeCompare ? 1 : 0);
        if (args.Length < required || args.Length > allowed)
            throw new GlslParseException($"'{name}' : no matching overloaded function found");
        bool bias = false;
        if (!lod && !grad && !cubeCompare)
        {
            int nonBias = 2 + (offset ? 1 : 0) + (lodClamp ? 1 : 0);
            bias = args.Length > nonBias;
        }

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
        var inst = InstrPool.Rent(op, resultTypeId, _m.AllocId()).Add(args[0].Id, coordId);
        if (dref != 0) inst.Add(dref);
        if (mask != 0) inst.Add(mask);
        foreach (uint id in operands) inst.Add(id);
        _code.Add(inst);
        return new Value(inst.ResultId, resultType, false);
    }
}
