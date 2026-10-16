namespace Prowl.Prism.SpirV;

/// <summary>
/// Builds a SPIR-V module: allocates result ids, de-duplicates types and constants, and
/// serializes the sections in the order the spec requires (capabilities, memory model,
/// entry points, execution modes, debug, decorations, types/constants/globals, then
/// function bodies). Mirrors the role of glslang's SpvBuilder.
/// </summary>
public sealed class Module
{
    uint _bound = 1;   // result ids start at 1
    readonly List<Instruction> _extImports = [];
    Instruction? _memoryModel;
    readonly List<Instruction> _entryPoints = [];
    readonly List<Instruction> _executionModes = [];
    readonly List<Instruction> _strings = [];            // OpString, referenced by debug/non-semantic instructions
    readonly List<Instruction> _source = [];             // OpSource (+ OpSourceContinued)
    readonly List<Instruction> _sourceExtensions = [];   // OpSourceExtension
    readonly List<Instruction> _names = [];              // OpName / OpMemberName
    readonly List<Instruction> _decorations = [];
    readonly List<Instruction> _globals = [];      // types, constants, global variables
    readonly List<Instruction> _functions = [];    // function bodies, appended last
    readonly Dictionary<string, uint> _cache = [];  // structural dedup of variable-arity types/constants
    // Fixed-arity type/constant dedup keyed by a value struct: no per-lookup string key or closure to
    // allocate (the hot path, called for nearly every instruction's operand types).
    readonly Dictionary<NKey, uint> _cacheN = [];
    readonly record struct NKey(byte Kind, uint A, uint B, uint C, uint D = 0, uint E = 0);

    public uint Version { get; private set; } = Spv.Version_1_0;
    // The SPIR-V generator magic number: Khronos Glslang Reference Front End (8), the generator the current
    // standalone glslang stamps, so the header word is identical to the reference output.
    public uint Generator { get; init; } = 0x0008000B;

    public uint AllocId() => _bound++;

    // Rewinds this module for reuse across compiles on the thread (with InstrPool, this is what makes the
    // whole front-end allocation-free after warm-up): every section list and dedup cache is cleared -- which
    // keeps its capacity, so no backing array regrows -- and the id counter/memory model reset.
    public Module Reset(uint version)
    {
        Version = version;
        _bound = 1;
        _memoryModel = null;
        _extImports.Clear(); _entryPoints.Clear(); _executionModes.Clear(); _strings.Clear();
        _source.Clear(); _sourceExtensions.Clear(); _names.Clear(); _decorations.Clear();
        _globals.Clear(); _functions.Clear();
        _cache.Clear(); _cacheN.Clear(); _compositeCache.Clear(); _extInstSets.Clear();
        _capabilitySet.Clear(); _extensionSet.Clear(); _globalVariables.Clear();
        return this;
    }

    // ---- module-level declarations ----

    readonly HashSet<Capability> _capabilitySet = [];
    public void AddCapability(Capability capability) => _capabilitySet.Add(capability);

    readonly HashSet<string> _extensionSet = [];
    public void AddExtension(string name) => _extensionSet.Add(name);

    // Keyed directly by the set name (a constant such as "GLSL.std.450") so the hot path -- every OpExtInst
    // imports the set -- neither builds a string key nor allocates a closure.
    readonly Dictionary<string, uint> _extInstSets = [];
    public uint ImportExtInstSet(string name) =>
        _extInstSets.TryGetValue(name, out uint id) ? id
            : _extInstSets[name] = Emit(_extImports, InstrPool.Rent(Op.ExtInstImport, resultId: AllocId()).AddString(name));

    public void SetMemoryModel(AddressingModel addressing, MemoryModel memory) =>
        _memoryModel = InstrPool.Rent(Op.MemoryModel).AddEnum(addressing).AddEnum(memory);

    public void AddEntryPoint(ExecutionModel model, uint entryPoint, string name, params uint[] interfaceIds) =>
        _entryPoints.Add(InstrPool.Rent(Op.EntryPoint).AddEnum(model).Add(entryPoint).AddString(name).Add(interfaceIds));

    public void AddExecutionMode(uint entryPoint, ExecutionMode mode, params uint[] operands) =>
        _executionModes.Add(InstrPool.Rent(Op.ExecutionMode).Add(entryPoint).AddEnum(mode).Add(operands));

    public uint StringConstant(string value) =>
        Cache($"str:{value}", () => Emit(_strings, InstrPool.Rent(Op.String, resultId: AllocId()).AddString(value)));

    public void AddSource(SourceLanguage language, uint version) =>
        _source.Add(InstrPool.Rent(Op.Source).AddEnum(language).Add(version));

    public void AddSourceExtension(string extension) =>
        _sourceExtensions.Add(InstrPool.Rent(Op.SourceExtension).AddString(extension));

    public void AddName(uint target, string name) =>
        _names.Add(InstrPool.Rent(Op.Name).Add(target).AddString(name));

    public void AddMemberName(uint structType, int member, string name) =>
        _names.Add(InstrPool.Rent(Op.MemberName).Add(structType, (uint)member).AddString(name));

    public void Decorate(uint target, Decoration decoration, params uint[] operands) =>
        _decorations.Add(InstrPool.Rent(Op.Decorate).Add(target).AddEnum(decoration).Add(operands));

    // ---- types ----

    // A fixed-arity type instruction whose operands are exactly the key's A/B fields (in order). The hot
    // path (a cache hit) is a single struct-keyed dictionary lookup with no allocation.
    uint CacheType(NKey k, Op op, int arity)
    {
        if (_cacheN.TryGetValue(k, out uint id)) return id;
        var inst = InstrPool.Rent(op, resultId: AllocId());
        if (arity >= 1) inst.Add(k.A);
        if (arity >= 2) inst.Add(k.B);
        return _cacheN[k] = Emit(_globals, inst);
    }

    public uint TypeVoid() => CacheType(new NKey(1, 0, 0, 0), Op.TypeVoid, 0);

    public uint TypeBool() => CacheType(new NKey(2, 0, 0, 0), Op.TypeBool, 0);

    public uint TypeInt(int width, bool signed) => CacheType(new NKey(3, (uint)width, signed ? 1u : 0u, 0), Op.TypeInt, 2);

    public uint TypeFloat(int width) => CacheType(new NKey(4, (uint)width, 0, 0), Op.TypeFloat, 1);

    public uint TypeVector(uint component, int count) => CacheType(new NKey(5, component, (uint)count, 0), Op.TypeVector, 2);

    public uint TypeMatrix(uint column, int count) => CacheType(new NKey(6, column, (uint)count, 0), Op.TypeMatrix, 2);

    public uint TypePointer(StorageClass storage, uint pointee) => CacheType(new NKey(7, (uint)storage, pointee, 0), Op.TypePointer, 2);

    // A forward-declared pointer (OpTypeForwardPointer) for a recursive/buffer-reference type: the id is
    // reserved now, its pointee defined later with TypePointerForward using the same id.
    public uint TypeForwardPointer(StorageClass storage)
    {
        uint id = AllocId();
        Emit(_globals, InstrPool.Rent(Op.TypeForwardPointer).Add(id).AddEnum(storage));
        return id;
    }

    public void TypePointerForward(uint id, StorageClass storage, uint pointee) =>
        Emit(_globals, InstrPool.Rent(Op.TypePointer, resultId: id).AddEnum(storage).Add(pointee));

    public uint TypeFunction(uint returnType, params uint[] paramTypes) =>
        Cache($"fn{returnType}:{string.Join(',', paramTypes)}", () => Emit(_globals, InstrPool.Rent(Op.TypeFunction, resultId: AllocId()).Add(returnType).Add(paramTypes)));

    public uint TypeArray(uint element, uint lengthConstId) => CacheType(new NKey(8, element, lengthConstId, 0), Op.TypeArray, 2);

    public uint TypeRuntimeArray(uint element) => CacheType(new NKey(9, element, 0, 0), Op.TypeRuntimeArray, 1);

    /// <summary>A runtime array that is never de-duplicated; an interface block's array member carries
    /// block-specific ArrayStride decoration, so each block needs its own array type identity.</summary>
    public uint TypeRuntimeArrayUnique(uint element) =>
        Emit(_globals, InstrPool.Rent(Op.TypeRuntimeArray, resultId: AllocId()).Add(element));

    /// <summary>A sized array that is never de-duplicated, for the same reason as TypeRuntimeArrayUnique.</summary>
    public uint TypeArrayUnique(uint element, uint lengthConstId) =>
        Emit(_globals, InstrPool.Rent(Op.TypeArray, resultId: AllocId()).Add(element, lengthConstId));

    public uint TypeStruct(params uint[] members) =>
        Cache($"struct:{string.Join(',', members)}", () => Emit(_globals, InstrPool.Rent(Op.TypeStruct, resultId: AllocId()).Add(members)));

    /// <summary>A distinct struct type that is never de-duplicated; interface blocks need their own
    /// identity because each carries block-specific decorations (Block/BufferBlock, offsets).</summary>
    public uint TypeStructUnique(params uint[] members) =>
        Emit(_globals, InstrPool.Rent(Op.TypeStruct, resultId: AllocId()).Add(members));

    public uint TypeImage(uint sampledType, Dim dim, int depth, int arrayed, int ms, int sampled, ImageFormat format) =>
        Cache($"img{sampledType}:{dim}:{depth}:{arrayed}:{ms}:{sampled}:{format}", () => Emit(_globals,
            InstrPool.Rent(Op.TypeImage, resultId: AllocId()).Add(sampledType).AddEnum(dim).Add((uint)depth, (uint)arrayed, (uint)ms, (uint)sampled).AddEnum(format)));

    public uint TypeSampledImage(uint imageType) => CacheType(new NKey(10, imageType, 0, 0), Op.TypeSampledImage, 1);

    public uint TypeCooperativeMatrixKHR(uint component, uint scope, uint rows, uint cols, uint use) =>
        Cache($"coopmat{component}:{scope}:{rows}:{cols}:{use}", () => Emit(_globals,
            InstrPool.Rent(Op.TypeCooperativeMatrixKHR, resultId: AllocId()).Add(component, scope, rows, cols, use)));

    public uint TypeSampler() =>
        Cache("sampler", () => Emit(_globals, InstrPool.Rent(Op.TypeSampler, resultId: AllocId())));

    public uint TypeAccelerationStructure() =>
        Cache("accel", () => Emit(_globals, InstrPool.Rent(Op.TypeAccelerationStructureKHR, resultId: AllocId())));

    public uint TypeRayQuery() =>
        Cache("rayquery", () => Emit(_globals, InstrPool.Rent(Op.TypeRayQueryKHR, resultId: AllocId())));

    public void MemberDecorate(uint structType, int member, Decoration decoration, params uint[] operands) =>
        _decorations.Add(InstrPool.Rent(Op.MemberDecorate).Add(structType, (uint)member).AddEnum(decoration).Add(operands));

    // ---- constants ----

    public uint ConstantBool(uint type, bool value)
    {
        var k = new NKey(20, type, value ? 1u : 0u, 0);
        return _cacheN.TryGetValue(k, out uint id) ? id
            : _cacheN[k] = Emit(_globals, InstrPool.Rent(value ? Op.ConstantTrue : Op.ConstantFalse, type, resultId: AllocId()));
    }

    public uint ConstantFloat(uint type, float value) =>
        ConstWord(21, type, BitConverter.SingleToUInt32Bits(value));

    // A 16-bit float constant: the half value occupies the low bits of a single operand word.
    public uint ConstantHalf(uint type, float value) =>
        ConstWord(22, type, BitConverter.HalfToUInt16Bits((Half)value));

    // A 64-bit float constant: the value occupies two operand words, low half first.
    public uint ConstantDouble(uint type, double value) => ConstDoubleWord(23, type, BitConverter.DoubleToUInt64Bits(value));

    public uint ConstantInt(uint type, uint bits) => ConstWord(24, type, bits);

    // A 64-bit integer constant: the value occupies two operand words, low half first.
    public uint ConstantInt64(uint type, ulong bits) => ConstDoubleWord(25, type, bits);

    // A single-word scalar constant (int / float / half): keyed by (kind, type, bits), no string allocation.
    uint ConstWord(byte kind, uint type, uint bits)
    {
        var k = new NKey(kind, type, bits, 0);
        return _cacheN.TryGetValue(k, out uint id) ? id
            : _cacheN[k] = Emit(_globals, InstrPool.Rent(Op.Constant, type, AllocId()).Add(bits));
    }

    // A two-word scalar constant (int64 / double): keyed by (kind, type-and-low, high).
    uint ConstDoubleWord(byte kind, uint type, ulong bits)
    {
        var k = new NKey(kind, type, (uint)bits, (uint)(bits >> 32));
        return _cacheN.TryGetValue(k, out uint id) ? id
            : _cacheN[k] = Emit(_globals, InstrPool.Rent(Op.Constant, type, AllocId()).Add((uint)bits, (uint)(bits >> 32)));
    }

    // Large composites (array/struct constants) dedup by a hash of (type + components) with an exact
    // array compare on collision -- no string key. A big const array indexed repeatedly (glslang
    // re-materializes it per access) would otherwise rebuild a long joined-string key every time.
    readonly Dictionary<int, List<(uint Type, uint[] Comps, uint Id)>> _compositeCache = [];
    public uint ConstantComposite(uint type, params uint[] components)
    {
        if (components.Length is >= 2 and <= 4)
        {
            var k = new NKey(26, type, components[0], components[1], components.Length > 2 ? components[2] : 0, components.Length > 3 ? components[3] : 0);
            return _cacheN.TryGetValue(k, out uint hit) ? hit : _cacheN[k] = Emit(_globals, InstrPool.Rent(Op.ConstantComposite, type, AllocId()).Add(components));
        }
        var hash = new HashCode();
        hash.Add(type);
        foreach (uint c in components) hash.Add(c);
        int h = hash.ToHashCode();
        if (_compositeCache.TryGetValue(h, out var bucket))
            foreach (var e in bucket)
                if (e.Type == type && e.Comps.AsSpan().SequenceEqual(components)) return e.Id;
        uint id = Emit(_globals, InstrPool.Rent(Op.ConstantComposite, type, AllocId()).Add(components));
        (bucket ??= _compositeCache[h] = []).Add((type, components, id));
        return id;
    }

    // Specialization constants are never de-duplicated: each is a distinct specializable value.
    public uint SpecConstantBool(uint type, bool value) =>
        Emit(_globals, InstrPool.Rent(value ? Op.SpecConstantTrue : Op.SpecConstantFalse, type, resultId: AllocId()));

    public uint SpecConstantInt(uint type, uint bits) =>
        Emit(_globals, InstrPool.Rent(Op.SpecConstant, type, AllocId()).Add(bits));

    public uint SpecConstantFloat(uint type, float value) =>
        Emit(_globals, InstrPool.Rent(Op.SpecConstant, type, AllocId()).Add(BitConverter.SingleToUInt32Bits(value)));

    public uint SpecConstantHalf(uint type, float value) =>
        Emit(_globals, InstrPool.Rent(Op.SpecConstant, type, AllocId()).Add(BitConverter.HalfToUInt16Bits((Half)value)));

    // 64-bit specialization constants occupy two operand words, low half first (as ordinary 64-bit constants do).
    public uint SpecConstantDouble(uint type, double value)
    {
        ulong bits = BitConverter.DoubleToUInt64Bits(value);
        return Emit(_globals, InstrPool.Rent(Op.SpecConstant, type, AllocId()).Add((uint)bits, (uint)(bits >> 32)));
    }

    public uint SpecConstantInt64(uint type, ulong bits) =>
        Emit(_globals, InstrPool.Rent(Op.SpecConstant, type, AllocId()).Add((uint)bits, (uint)(bits >> 32)));

    /// <summary>An operation on specialization constants: it stays specializable rather than folding,
    /// so the opcode it performs is its first operand.</summary>
    public uint SpecConstantOp(uint type, Op opcode, params uint[] operands) =>
        Emit(_globals, InstrPool.Rent(Op.SpecConstantOp, type, AllocId()).Add((uint)opcode).Add(operands));

    public uint SpecConstantComposite(uint type, params uint[] components) =>
        Emit(_globals, InstrPool.Rent(Op.SpecConstantComposite, type, AllocId()).Add(components));

    // ---- globals & functions ----

    // Every module-scope OpVariable in creation order. SPIR-V 1.4 lists them all in OpEntryPoint's
    // interface (not just Input/Output), and creation order is the order glslang emits them in.
    readonly List<uint> _globalVariables = [];
    public IReadOnlyList<uint> GlobalVariables => _globalVariables;

    public uint GlobalVariable(uint pointerType, StorageClass storage)
    {
        uint id = AllocId();
        _globals.Add(InstrPool.Rent(Op.Variable, pointerType, id).AddEnum(storage));
        _globalVariables.Add(id);
        return id;
    }

    public uint GlobalVariable(uint pointerType, StorageClass storage, uint initializer)
    {
        uint id = AllocId();
        _globals.Add(InstrPool.Rent(Op.Variable, pointerType, id).AddEnum(storage).Add(initializer));
        _globalVariables.Add(id);
        return id;
    }

    /// <summary>The storage class a global-variable id was created with (the entry-point interface
    /// filter needs it: SPIR-V &lt;=1.3 may list only Input/Output variables). Null when the id is
    /// not a global variable emitted so far.</summary>
    public StorageClass? GlobalStorage(uint id)
    {
        foreach (var g in _globals)
            if (g.ResultId == id && g.Op == Op.Variable) return (StorageClass)g.Operands[0];
        return null;
    }

    /// <summary>Appends a function's instruction stream (OpFunction ... OpFunctionEnd).</summary>
    public void AddFunction(IEnumerable<Instruction> body) => _functions.AddRange(body);

    // ---- glslang's feature post-process ----

    /// <summary>
    /// glslang's Builder::postProcessFeatures for the narrow (8/16-bit) arithmetic capabilities. glslang does
    /// not add Int8/Int16/Float16 when it declares an 8/16-bit type: a module that only moves narrow values in
    /// and out of storage needs just the storage capabilities (and only the device features they imply). The
    /// arithmetic capability is added when a constant of the type exists or an instruction in a function body
    /// uses it beyond loading, storing, copying or converting it (postProcessType). Pointer types into
    /// PhysicalStorageBuffer get their storage capability here too, since such memory has no variable.
    /// </summary>
    public void PostProcessNarrowTypes(uint version)
    {
        var defs = new Dictionary<uint, Instruction>();
        foreach (var g in _globals) if (g.ResultId != 0) defs[g.ResultId] = g;
        var typeOf = new Dictionary<uint, uint>();
        foreach (var g in _globals) if (g.ResultId != 0 && g.TypeId != 0) typeOf[g.ResultId] = g.TypeId;
        foreach (var f in _functions) if (f.ResultId != 0 && f.TypeId != 0) typeOf[f.ResultId] = f.TypeId;

        // Follows vectors, matrices, arrays and pointers down to the scalar/struct at the bottom.
        Instruction? Basic(uint type)
        {
            while (defs.TryGetValue(type, out var t))
            {
                switch (t.Op)
                {
                    case Op.TypeVector: case Op.TypeMatrix: case Op.TypeArray: case Op.TypeRuntimeArray: type = t.Operands[0]; break;
                    case Op.TypePointer: type = t.Operands[1]; break;
                    default: return t;
                }
            }
            return null;
        }
        bool Contains(uint type, Op op, uint width)
        {
            if (!defs.TryGetValue(type, out var t)) return false;
            return t.Op switch
            {
                Op.TypeInt or Op.TypeFloat => t.Op == op && t.Operands[0] == width,
                Op.TypeStruct => t.Operands.Any(m => Contains(m, op, width)),
                Op.TypeVector or Op.TypeMatrix or Op.TypeArray or Op.TypeRuntimeArray => Contains(t.Operands[0], op, width),
                _ => false,
            };
        }
        void ArithmeticCap(Op op, uint width)
        {
            if (op == Op.TypeInt && width == 8) AddCapability(Capability.Int8);
            else if (op == Op.TypeInt && width == 16) AddCapability(Capability.Int16);
            else if (op == Op.TypeFloat && width == 16) AddCapability(Capability.Float16);
        }
        void StructCaps(uint type)
        {
            if (Contains(type, Op.TypeInt, 8)) AddCapability(Capability.Int8);
            if (Contains(type, Op.TypeInt, 16)) AddCapability(Capability.Int16);
            if (Contains(type, Op.TypeFloat, 16)) AddCapability(Capability.Float16);
        }
        StorageClass? PointerStorage(uint pointer) =>
            typeOf.TryGetValue(pointer, out uint pt) && defs.TryGetValue(pt, out var p) && p.Op == Op.TypePointer
                ? (StorageClass)p.Operands[0] : null;

        foreach (var g in _globals)
        {
            switch (g.Op)
            {
                // GlslangToSpv's createSpvConstant adds the arithmetic capability for every narrow constant.
                case Op.Constant or Op.ConstantComposite or Op.SpecConstant or Op.SpecConstantComposite or Op.SpecConstantOp:
                    if (Basic(g.TypeId) is { Op: Op.TypeInt or Op.TypeFloat } b) ArithmeticCap(b.Op, b.Operands[0]);
                    break;
                case Op.TypePointer when (StorageClass)g.Operands[0] == StorageClass.PhysicalStorageBuffer:
                    if (Contains(g.Operands[1], Op.TypeInt, 8))
                    {
                        if (version < (uint)SpirVVersion.V1_5) AddExtension("SPV_KHR_8bit_storage");
                        AddCapability(Capability.StorageBuffer8BitAccess);
                    }
                    if (Contains(g.Operands[1], Op.TypeInt, 16) || Contains(g.Operands[1], Op.TypeFloat, 16))
                    {
                        if (version < (uint)SpirVVersion.V1_3) AddExtension("SPV_KHR_16bit_storage");
                        AddCapability(Capability.StorageBuffer16BitAccess);
                    }
                    break;
            }
        }

        bool hasStorage16 = _capabilitySet.Contains(Capability.StorageInputOutput16) || _capabilitySet.Contains(Capability.StoragePushConstant16)
            || _capabilitySet.Contains(Capability.StorageBuffer16BitAccess) || _capabilitySet.Contains(Capability.UniformAndStorageBuffer16BitAccess);
        bool hasStorage8 = _capabilitySet.Contains(Capability.StoragePushConstant8) || _capabilitySet.Contains(Capability.UniformAndStorageBuffer8BitAccess)
            || _capabilitySet.Contains(Capability.StorageBuffer8BitAccess);

        void Check(Instruction inst, uint type)
        {
            if (!defs.ContainsKey(type)) return;
            var basic = Basic(type);
            Op basicOp = basic?.Op ?? Op.Nop;
            uint width = basicOp is Op.TypeInt or Op.TypeFloat ? basic!.Operands[0] : 0;
            switch (inst.Op)
            {
                case Op.Load or Op.Store:
                    if (basicOp == Op.TypeStruct) { StructCaps(type); break; }
                    var storage = PointerStorage(inst.Operands[0]);
                    bool storageClassExempt = storage is StorageClass.PhysicalStorageBuffer or StorageClass.Uniform
                        or StorageClass.StorageBuffer or StorageClass.PushConstant;
                    if (width == 8 && !storageClassExempt) AddCapability(Capability.Int8);
                    else if (width == 16 && !storageClassExempt && storage is not (StorageClass.Input or StorageClass.Output))
                        ArithmeticCap(basicOp, 16);
                    break;
                case Op.CopyObject:
                    break;
                case Op.FConvert or Op.SConvert or Op.UConvert:
                    if (!hasStorage16)
                    {
                        if (Contains(type, Op.TypeFloat, 16)) AddCapability(Capability.Float16);
                        if (Contains(type, Op.TypeInt, 16)) AddCapability(Capability.Int16);
                    }
                    if (!hasStorage8 && Contains(type, Op.TypeInt, 8)) AddCapability(Capability.Int8);
                    break;
                case Op.ExtInst:
                    break;
                case Op.AccessChain or Op.PtrAccessChain or Op.InBoundsAccessChain:
                    if (defs[type].Op == Op.TypePointer) break;
                    if (basicOp == Op.TypeInt && width is 8 or 16) ArithmeticCap(Op.TypeInt, width);
                    break;
                default:
                    if (basicOp is Op.TypeInt or Op.TypeFloat) ArithmeticCap(basicOp, width);
                    break;
            }
        }

        foreach (var inst in _functions)
        {
            // OpFunction/OpFunctionParameter sit outside any block; glslang only visits block instructions.
            if (inst.Op is Op.Function or Op.FunctionParameter or Op.FunctionEnd) continue;
            if (inst.TypeId != 0) Check(inst, inst.TypeId);
            int lead = (inst.TypeId != 0 ? 1 : 0) + (inst.ResultId != 0 ? 1 : 0);
            var words = new uint[lead + inst.Operands.Count];
            int w = 0;
            if (inst.TypeId != 0) words[w++] = inst.TypeId;
            if (inst.ResultId != 0) words[w++] = inst.ResultId;
            inst.Operands.CopyTo(words, w);
            Optimizer.SpvGrammar.Classify((int)inst.Op, words, out _, out bool[] isId);
            for (int i = 0; i < inst.Operands.Count; i++)
                if (isId[lead + i] && typeOf.TryGetValue(inst.Operands[i], out uint operandType))
                    Check(inst, operandType);
        }
    }

    /// <summary>
    /// SPIR-V 1.4's SignExtend/ZeroExtend image operands: from 1.4 on glslang marks every texel-producing or
    /// texel-consuming image instruction on a 32-bit integer image with the image's signedness
    /// (TGlslangToSpvTraverser's signExtensionMask). Depth-compare forms only ever return float and are left alone.
    /// </summary>
    public void PostProcessImageSignExtension()
    {
        if (Version < (uint)SpirVVersion.V1_4) return;
        var defs = new Dictionary<uint, Instruction>();
        foreach (var g in _globals) if (g.ResultId != 0) defs[g.ResultId] = g;
        var typeOf = new Dictionary<uint, uint>();
        foreach (var g in _globals) if (g.ResultId != 0 && g.TypeId != 0) typeOf[g.ResultId] = g.TypeId;
        foreach (var f in _functions) if (f.ResultId != 0 && f.TypeId != 0) typeOf[f.ResultId] = f.TypeId;

        foreach (var inst in _functions)
        {
            int maskIndex = inst.Op switch
            {
                Op.ImageSampleImplicitLod or Op.ImageSampleExplicitLod or Op.ImageSampleProjImplicitLod or Op.ImageSampleProjExplicitLod
                    or Op.ImageFetch or Op.ImageRead or Op.ImageSparseSampleImplicitLod or Op.ImageSparseSampleExplicitLod
                    or Op.ImageSparseSampleProjImplicitLod or Op.ImageSparseSampleProjExplicitLod or Op.ImageSparseFetch
                    or Op.ImageSparseRead => 2,
                Op.ImageGather or Op.ImageSparseGather or Op.ImageWrite => 3,
                _ => -1,
            };
            if (maskIndex < 0 || !typeOf.TryGetValue(inst.Operands[0], out uint imageType) || !defs.TryGetValue(imageType, out var it)) continue;
            if (it.Op == Op.TypeSampledImage && !defs.TryGetValue(it.Operands[0], out it)) continue;
            if (it.Op != Op.TypeImage || !defs.TryGetValue(it.Operands[0], out var sampled)) continue;
            if (sampled.Op != Op.TypeInt || sampled.Operands[0] != 32) continue;
            uint bit = (uint)(sampled.Operands[1] != 0 ? ImageOperands.SignExtend : ImageOperands.ZeroExtend);
            // Neither bit takes an operand, so ORing it into an existing mask keeps the operand order intact.
            if (inst.Operands.Count > maskIndex) inst.Operands[maskIndex] |= bit;
            else inst.Operands.Add(bit);
        }
    }

    // ---- serialization ----

    [ThreadStatic] static List<uint>? _serializeBuf;

    public uint[] Serialize()
    {
        // Build into a pooled per-thread word buffer so the (large) growing list is reused across compiles;
        // only the final result array is a genuine allocation (it escapes to the caller).
        var words = _serializeBuf ??= [];
        words.Clear();
        words.Add(Spv.MagicNumber); words.Add(Version); words.Add(Generator); words.Add(_bound); words.Add(0 /* schema */);
        void Emit(IEnumerable<Instruction> section) { foreach (var i in section) i.WriteTo(words); }

        // Section order mirrors glslang's Builder::dump exactly (references/.../SpvBuilder.cpp).
        // glslang keeps capabilities in a std::set, so they are emitted in ascending value order.
        Emit(_capabilitySet.OrderBy(c => (uint)c).Select(c => InstrPool.Rent(Op.Capability).AddEnum(c)));
        // glslang keeps extensions in a std::set<std::string>, so they are emitted in ordinal order.
        Emit(_extensionSet.OrderBy(e => e, StringComparer.Ordinal).Select(e => InstrPool.Rent(Op.Extension).AddString(e)));
        Emit(_extImports);
        _memoryModel?.WriteTo(words);
        Emit(_entryPoints);
        Emit(_executionModes);
        Emit(_strings);
        Emit(_source);
        Emit(_sourceExtensions);
        Emit(_names);
        Emit(SortedDecorations());
        Emit(_globals);
        Emit(_functions);
        return words.ToArray();
    }

    // ---- helpers ----

    // glslang keeps decorations in a std::set ordered by DecorationInstructionLessThan (SpvBuilder.cpp): by the
    // target id (operand 0), then opcode (OpDecorate 71 before OpMemberDecorate 72, so a struct's Block sorts
    // before its members), then operand-by-operand (so Binding 33 sorts before DescriptorSet 34). Identical
    // decorations are deduplicated. Current glslang emits in this sorted order, not insertion order.
    List<Instruction> SortedDecorations()
    {
        // Sort and dedup in place (the list is used once, at serialization). The comparer is a total order,
        // so unstable List.Sort is fine; identical decorations compare equal and are compacted out.
        _decorations.Sort(DecorationOrder);
        int w = 0;
        for (int r = 0; r < _decorations.Count; r++)
            if (w == 0 || DecorationOrder.Compare(_decorations[w - 1], _decorations[r]) != 0)
                _decorations[w++] = _decorations[r];
        _decorations.RemoveRange(w, _decorations.Count - w);
        return _decorations;
    }

    static readonly IComparer<Instruction> DecorationOrder = Comparer<Instruction>.Create((a, b) =>
    {
        if (a.Operands[0] != b.Operands[0]) return a.Operands[0].CompareTo(b.Operands[0]);
        if (a.Op != b.Op) return ((uint)a.Op).CompareTo((uint)b.Op);
        int min = Math.Min(a.Operands.Count, b.Operands.Count);
        for (int i = 1; i < min; i++)
            if (a.Operands[i] != b.Operands[i]) return a.Operands[i].CompareTo(b.Operands[i]);
        return a.Operands.Count.CompareTo(b.Operands.Count);
    });

    uint Cache(string key, Func<uint> create) => _cache.TryGetValue(key, out uint id) ? id : _cache[key] = create();
    static uint Emit(List<Instruction> section, Instruction instruction) { section.Add(instruction); return instruction.ResultId; }
}
