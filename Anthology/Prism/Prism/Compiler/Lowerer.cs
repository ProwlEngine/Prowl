using System.Globalization;
using Prowl.Prism.Glsl;
using Prowl.Prism.SpirV;
using Index = Prowl.Prism.Glsl.Index;

namespace Prowl.Prism.Compiler;

/// <summary>
/// Lowers a parsed GLSL translation unit into a SPIR-V <see cref="Module"/>. The growing
/// heart of the backend; it covers the constructs implemented so far and throws a clear
/// "not lowered yet" on anything new, so the unimplemented corpus is self-documenting.
/// </summary>
public sealed class Lowerer
{
    // stage/version are mutable so the whole lowerer can be reused across compiles on a thread (see Reset),
    // the same TPoolAllocator-style trick the Module and instruction/token pools use to reach ~zero garbage.
    ShaderStage stage = ShaderStage.Fragment;
    SpirVVersion version = SpirVVersion.V1_0;

    // glslang's Builder::addIncorporatedExtension: an extension that later SPIR-V versions folded into core
    // is declared only below the version that incorporated it. From that version on the capability alone
    // is enough, and re-declaring the extension is what the reference compiler never does.
    void AddIncorporatedExtension(string name, SpirVVersion coreFrom)
    {
        if (version < coreFrom) _m.AddExtension(name);
    }

    // Relaxed marks a mediump/lowp value: its id carries a RelaxedPrecision decoration, and the
    // precision propagates to results computed from it (GLSL takes the highest precision of the operands).
    // Layout records the block ruleset an aggregate value was loaded under, so that storing it into a
    // plain variable of the same GLSL type knows the two SPIR-V types differ. Null outside any block.
    readonly record struct Value(uint Id, GlslType Type, bool IsConstant, bool Relaxed = false, BlockLayout? Layout = null);
    // A buffer's coherent/volatile qualifier. Under the Vulkan memory model these are not member
    // decorations but per-access MakePointerAvailable/Visible memory operands on each load/store.
    enum MemQual { None, Coherent, Volatile, Shared }
    sealed record Global(GlslType Type, StorageClass Storage, uint VarId, MemQual Mem = MemQual.None);
    // A member of an anonymous block (`uniform M { mat4 m; };`), accessed by bare name via the block variable.
    // Var resolves the owning block's variable, materializing the block on the first member reference.
    // TrivialSymbol mirrors glslang isTrivialLeaf on the BLOCK: only a uniform/push_constant block is a
    // symbol it counts as trivial to read; a buffer block and an in/out block are not.
    readonly record struct BlockMember(Func<uint> Var, int Index, GlslType Type, StorageClass Storage, BlockLayout? Layout = null, bool TrivialSymbol = false, MemQual Mem = MemQual.None);
    // Id is assigned when the function is emitted (glslang allocates the function id after its
    // parameter type/ids), so it is mutable rather than a positional record field.
    sealed record UserFunction(GlslType ReturnType, IReadOnlyList<FnParam> Params) { public uint Id; }
    // ByValue marks a `const in` parameter: nothing can write it, so glslang passes the r-value straight
    // through rather than a Function pointer to a copy.
    sealed record FnParam(string Name, GlslType Type, bool IsIn, bool IsOut, bool ByValue = false);

    // A compile-time-folded constant value: a scalar (F holds a float, I holds an int/uint/bool) or
    // an aggregate (vector components, matrix columns, array elements, or struct fields).
    abstract record CVal;
    sealed record CScalar(Scalar Kind, double F, long I) : CVal;
    sealed record CAgg(GlslType Type, IReadOnlyList<CVal> Parts) : CVal { public uint Materialized; }   // cached SPIR-V id (a const array is re-materialized on each index access)

    static readonly VectorType Vec4 = new(Scalar.Float, 4);
    static readonly VectorType Vec3 = new(Scalar.Float, 3);
    static readonly VectorType UVec3 = new(Scalar.UInt, 3);

    static readonly VectorType Vec2 = new(Scalar.Float, 2);
    static readonly ArrayType ClipCullType = new(Types.Float, 1);   // gl_ClipDistance/gl_CullDistance (single plane)
    // The gl_PerVertex block shape, used only for type inference of gl_in[i]/gl_out[i] and gl_in.length().
    static readonly StructType PerVertexStruct = new("gl_PerVertex",
        [("gl_Position", Vec4), ("gl_PointSize", Types.Float), ("gl_ClipDistance", ClipCullType), ("gl_CullDistance", ClipCullType)]);

    // The built-ins that live in gl_PerVertex (or the tessellation level arrays) rather than in a
    // variable of their own: they are reached through an access chain, not a plain global.
    static GlslType? PerVertexType(string name) => name switch
    {
        "gl_Position" => Vec4,
        "gl_PointSize" => Types.Float,
        "gl_ClipDistance" or "gl_CullDistance" => ClipCullType,
        "gl_TessLevelOuter" => new ArrayType(Types.Float, 4),
        "gl_TessLevelInner" => new ArrayType(Types.Float, 2),
        _ => null,
    };

    // gl_* builtin variables: which BuiltIn, its type, whether it is an output, and any capability and
    // SPIR-V extension it requires.
    static readonly Dictionary<string, (BuiltIn Kind, GlslType Type, bool Output, Capability? Cap, string? Ext)> Builtins = new()
    {
        ["gl_VertexIndex"] = (BuiltIn.VertexIndex, Types.Int, false, null, null),
        ["gl_InstanceIndex"] = (BuiltIn.InstanceIndex, Types.Int, false, null, null),
        ["gl_FragCoord"] = (BuiltIn.FragCoord, Vec4, false, null, null),
        ["gl_FrontFacing"] = (BuiltIn.FrontFacing, Types.Bool, false, null, null),
        ["gl_PointCoord"] = (BuiltIn.PointCoord, Vec2, false, null, null),
        ["gl_HelperInvocation"] = (BuiltIn.HelperInvocation, Types.Bool, false, null, null),
        ["gl_GlobalInvocationID"] = (BuiltIn.GlobalInvocationId, UVec3, false, null, null),
        ["gl_LocalInvocationID"] = (BuiltIn.LocalInvocationId, UVec3, false, null, null),
        ["gl_WorkGroupID"] = (BuiltIn.WorkgroupId, UVec3, false, null, null),
        ["gl_NumWorkGroups"] = (BuiltIn.NumWorkgroups, UVec3, false, null, null),
        ["gl_LocalInvocationIndex"] = (BuiltIn.LocalInvocationIndex, Types.UInt, false, null, null),
        ["gl_InvocationID"] = (BuiltIn.InvocationId, Types.Int, false, null, null),
        ["gl_TessCoord"] = (BuiltIn.TessCoord, Vec3, false, null, null),
        ["gl_PatchVerticesIn"] = (BuiltIn.PatchVertices, Types.Int, false, null, null),
        ["gl_PrimitiveID"] = (BuiltIn.PrimitiveId, Types.Int, false, Capability.Geometry, null),
        ["gl_PrimitiveIDIn"] = (BuiltIn.PrimitiveId, Types.Int, false, Capability.Geometry, null),
        ["gl_Layer"] = (BuiltIn.Layer, Types.Int, false, Capability.Geometry, null),
        ["gl_ViewportIndex"] = (BuiltIn.ViewportIndex, Types.Int, false, Capability.MultiViewport, null),
        ["gl_SampleID"] = (BuiltIn.SampleId, Types.Int, false, Capability.SampleRateShading, null),
        ["gl_SamplePosition"] = (BuiltIn.SamplePosition, Vec2, false, Capability.SampleRateShading, null),
        ["gl_FragDepth"] = (BuiltIn.FragDepth, Types.Float, true, null, null),
        ["gl_FragStencilRefARB"] = (BuiltIn.FragStencilRefEXT, Types.Int, true, Capability.StencilExportEXT, "SPV_EXT_shader_stencil_export"),
        ["gl_SampleMaskIn"] = (BuiltIn.SampleMask, new ArrayType(Types.Int, 1), false, null, null),
        ["gl_SampleMask"] = (BuiltIn.SampleMask, new ArrayType(Types.Int, 1), true, null, null),
        ["gl_ViewIndex"] = (BuiltIn.ViewIndex, Types.Int, false, Capability.MultiView, "SPV_KHR_multiview"),
        ["gl_DeviceIndex"] = (BuiltIn.DeviceIndex, Types.Int, false, Capability.DeviceGroup, "SPV_KHR_device_group"),
        // Draw parameters (core in 460, or GL_ARB_shader_draw_parameters with the ARB-suffixed spellings).
        ["gl_BaseVertex"] = (BuiltIn.BaseVertex, Types.Int, false, Capability.DrawParameters, "SPV_KHR_shader_draw_parameters"),
        ["gl_BaseInstance"] = (BuiltIn.BaseInstance, Types.Int, false, Capability.DrawParameters, "SPV_KHR_shader_draw_parameters"),
        ["gl_DrawID"] = (BuiltIn.DrawIndex, Types.Int, false, Capability.DrawParameters, "SPV_KHR_shader_draw_parameters"),
        ["gl_BaseVertexARB"] = (BuiltIn.BaseVertex, Types.Int, false, Capability.DrawParameters, "SPV_KHR_shader_draw_parameters"),
        ["gl_BaseInstanceARB"] = (BuiltIn.BaseInstance, Types.Int, false, Capability.DrawParameters, "SPV_KHR_shader_draw_parameters"),
        ["gl_DrawIDARB"] = (BuiltIn.DrawIndex, Types.Int, false, Capability.DrawParameters, "SPV_KHR_shader_draw_parameters"),
        ["gl_ShadingRateEXT"] = (BuiltIn.ShadingRateKHR, Types.Int, false, Capability.FragmentShadingRateKHR, "SPV_KHR_fragment_shading_rate"),
        ["gl_BaryCoordEXT"] = (BuiltIn.BaryCoordKHR, Vec3, false, Capability.FragmentBarycentricKHR, "SPV_KHR_fragment_shader_barycentric"),
        ["gl_PrimitiveShadingRateEXT"] = (BuiltIn.PrimitiveShadingRateKHR, Types.Int, true, Capability.FragmentShadingRateKHR, "SPV_KHR_fragment_shading_rate"),
        ["gl_SubgroupSize"] = (BuiltIn.SubgroupSize, Types.UInt, false, Capability.GroupNonUniform, null),
        ["gl_SubgroupInvocationID"] = (BuiltIn.SubgroupLocalInvocationId, Types.UInt, false, Capability.GroupNonUniform, null),
        ["gl_NumSubgroups"] = (BuiltIn.NumSubgroups, Types.UInt, false, Capability.GroupNonUniform, null),
        ["gl_SubgroupID"] = (BuiltIn.SubgroupId, Types.UInt, false, Capability.GroupNonUniform, null),
        ["gl_SubgroupEqMask"] = (BuiltIn.SubgroupEqMask, new VectorType(Scalar.UInt, 4), false, Capability.GroupNonUniformBallot, null),
        ["gl_SubgroupGeMask"] = (BuiltIn.SubgroupGeMask, new VectorType(Scalar.UInt, 4), false, Capability.GroupNonUniformBallot, null),
        ["gl_SubgroupGtMask"] = (BuiltIn.SubgroupGtMask, new VectorType(Scalar.UInt, 4), false, Capability.GroupNonUniformBallot, null),
        ["gl_SubgroupLeMask"] = (BuiltIn.SubgroupLeMask, new VectorType(Scalar.UInt, 4), false, Capability.GroupNonUniformBallot, null),
        ["gl_SubgroupLtMask"] = (BuiltIn.SubgroupLtMask, new VectorType(Scalar.UInt, 4), false, Capability.GroupNonUniformBallot, null),
        // GL_EXT_ray_tracing builtins (all Input; the RayTracingKHR capability from the stage covers them).
        ["gl_LaunchIDEXT"] = (BuiltIn.LaunchIdKHR, UVec3, false, null, null),
        ["gl_LaunchSizeEXT"] = (BuiltIn.LaunchSizeKHR, UVec3, false, null, null),
        ["gl_WorldRayOriginEXT"] = (BuiltIn.WorldRayOriginKHR, Vec3, false, null, null),
        ["gl_WorldRayDirectionEXT"] = (BuiltIn.WorldRayDirectionKHR, Vec3, false, null, null),
        ["gl_ObjectRayOriginEXT"] = (BuiltIn.ObjectRayOriginKHR, Vec3, false, null, null),
        ["gl_ObjectRayDirectionEXT"] = (BuiltIn.ObjectRayDirectionKHR, Vec3, false, null, null),
        ["gl_RayTminEXT"] = (BuiltIn.RayTminKHR, Types.Float, false, null, null),
        ["gl_RayTmaxEXT"] = (BuiltIn.RayTmaxKHR, Types.Float, false, null, null),
        ["gl_HitTEXT"] = (BuiltIn.RayTmaxKHR, Types.Float, false, null, null),
        ["gl_HitKindEXT"] = (BuiltIn.HitKindKHR, Types.UInt, false, null, null),
        ["gl_InstanceCustomIndexEXT"] = (BuiltIn.InstanceCustomIndexKHR, Types.Int, false, null, null),
        ["gl_GeometryIndexEXT"] = (BuiltIn.RayGeometryIndexKHR, Types.Int, false, null, null),
        ["gl_IncomingRayFlagsEXT"] = (BuiltIn.IncomingRayFlagsKHR, Types.UInt, false, null, null),
        ["gl_ObjectToWorldEXT"] = (BuiltIn.ObjectToWorldKHR, new MatrixType(4, 3), false, null, null),
        ["gl_WorldToObjectEXT"] = (BuiltIn.WorldToObjectKHR, new MatrixType(4, 3), false, null, null),
        // GL_NV_ray_tracing builtins map to the same SPIR-V BuiltIn decorations as the EXT ones.
        ["gl_LaunchIDNV"] = (BuiltIn.LaunchIdKHR, UVec3, false, null, null),
        ["gl_LaunchSizeNV"] = (BuiltIn.LaunchSizeKHR, UVec3, false, null, null),
        ["gl_WorldRayOriginNV"] = (BuiltIn.WorldRayOriginKHR, Vec3, false, null, null),
        ["gl_WorldRayDirectionNV"] = (BuiltIn.WorldRayDirectionKHR, Vec3, false, null, null),
        ["gl_ObjectRayOriginNV"] = (BuiltIn.ObjectRayOriginKHR, Vec3, false, null, null),
        ["gl_ObjectRayDirectionNV"] = (BuiltIn.ObjectRayDirectionKHR, Vec3, false, null, null),
        ["gl_RayTminNV"] = (BuiltIn.RayTminKHR, Types.Float, false, null, null),
        ["gl_RayTmaxNV"] = (BuiltIn.RayTmaxKHR, Types.Float, false, null, null),
        ["gl_HitTNV"] = (BuiltIn.RayTmaxKHR, Types.Float, false, null, null),
        ["gl_HitKindNV"] = (BuiltIn.HitKindKHR, Types.UInt, false, null, null),
        ["gl_InstanceCustomIndexNV"] = (BuiltIn.InstanceCustomIndexKHR, Types.Int, false, null, null),
        ["gl_IncomingRayFlagsNV"] = (BuiltIn.IncomingRayFlagsKHR, Types.UInt, false, null, null),
        ["gl_ObjectToWorldNV"] = (BuiltIn.ObjectToWorldKHR, new MatrixType(4, 3), false, null, null),
        ["gl_WorldToObjectNV"] = (BuiltIn.WorldToObjectKHR, new MatrixType(4, 3), false, null, null),
    };

    // One Module is pooled per thread and rewound by Reset between compiles. It is CLAIMED by the first
    // Lowerer built on the thread: any further Lowerer alive at the same time gets its own Module, because
    // two live Lowerers sharing one Module would silently overwrite each other's output.
    [ThreadStatic] static Module? _moduleTls;
    [ThreadStatic] static bool _moduleClaimed;
    readonly Module _m;
    readonly bool _ownsPool;   // this instance claimed the thread's pooled Module and instruction pool
    bool _lowering;            // re-entrancy guard: Lower on an instance already lowering would corrupt it

    public Lowerer()
    {
        if (_moduleClaimed) _m = new Module();
        else { _m = _moduleTls ??= new Module(); _moduleClaimed = true; _ownsPool = true; }
    }
    readonly Dictionary<string, Global> _globals = [];
    readonly HashSet<string> _globalDeclNames = [];   // names of declared global variables, for redefinition checks
    // glslang emits each global variable lazily, at its first reference during body traversal (so its
    // result id is interleaved with local temporaries/constants). We mirror that: a declaration records
    // a builder here and the SPIR-V variable is materialized on first use, in the same order.
    readonly Dictionary<string, (GlslType Type, Func<Global> Build)> _pendingGlobals = [];
    readonly Dictionary<string, BlockMember> _blockMembers = [];   // members of anonymous blocks, addressed by bare name
    readonly HashSet<StructType> _blockStructs = [];   // interface-block structs: their bool members are stored as uint
    readonly HashSet<StructType> _bufferBlockStructs = [];   // the `buffer` ones (Uniform-class BufferBlocks below 1.3)
    readonly Dictionary<StructType, BlockLayout> _blockLayout = [];   // the ruleset a block struct is laid out under
    // The same GLSL struct nested in a block is a DIFFERENT SPIR-V type from the one used outside it,
    // because it carries the block's Offsets. glslang keys its struct cache by layout; so do we, and a
    // copy between the two universes goes member by member (see MultiTypeStore).
    readonly Dictionary<(StructType Struct, BlockLayout Layout, bool RowMajor), uint> _layoutStructIds = [];
    // A block's array/runtime-array member carries an ArrayStride, so reading it as a whole value needs
    // the same distinct type. Keyed by the stored element type, length and layout.
    readonly Dictionary<(GlslType Element, int Length, BlockLayout Layout), uint> _layoutArrayIds = [];
    // Anonymous blocks, forced in declaration order if nothing referenced them (glslang keeps every
    // declared block as a linker object, in source order).
    readonly Dictionary<InterfaceBlock, Func<uint>> _pendingBlockBuilds = [];
    readonly HashSet<uint> _relaxedIds = [];   // ids decorated RelaxedPrecision (declared mediump/lowp, or derived from them)
    bool _defaultFloatMediump;   // `precision mediump/lowp float`: user float symbols and their values are relaxed
    bool _defaultIntMediump;   // int/uint default to mediump (ES fragment, or a `precision mediump/lowp int`)
    bool _isEs;   // the OpenGL ES (ESSL) profile: samplers and integers carry ES default precisions

    // Under a mediump/lowp default float precision, a user float symbol (and the values read from it) is
    // relaxed. Builtins keep their own precision, so this is only for declared user variables.
    bool RelaxByDefault(GlslType type) => BaseScalar(type) switch
    {
        Scalar.Float => _defaultFloatMediump,
        Scalar.Int or Scalar.UInt => _defaultIntMediump,
        _ => false,
    };

    // Whether a declared variable is relaxed: explicit mediump/lowp, or the type's mediump default unless
    // the declaration explicitly overrides with highp.
    bool DeclRelaxed(GlslType type, Qualifiers q) => IsRelaxed(q) || (!q.Has("highp") && RelaxByDefault(type));
    readonly Dictionary<string, Value> _valueParams = [];   // `const in` params: the name IS the value, not a pointer
    readonly HashSet<string> _outParams = [];   // write-only params: reading one is not a trivial leaf
    readonly HashSet<string> _varyingNames = [];   // in/out globals: not trivial to read (checked without materializing)
    // Operations glslang's PropagateNoContraction marks (value: skip a 32-bit signed int result).
    Dictionary<object, bool> _preciseOps = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<string> _preciseVars = [];   // interface variables declared `precise`
    readonly Dictionary<string, (GlslType Type, uint VarId)> _builtinVars = [];
    readonly Dictionary<string, List<UserFunction>> _userFunctions = [];   // by name, allowing overloads
    readonly Dictionary<string, StructType> _structs = [];
    AddressingModel _addressingModel = AddressingModel.Logical;
    bool _vmmEnabled;   // the module emits the Vulkan memory model (cap + extension + MemoryModel operand)
    bool _vmmPragma;    // #pragma use_vulkan_memory_model: default atomic scope becomes QueueFamily
    readonly Dictionary<string, StructType> _bufferRefs = [];   // GL_EXT_buffer_reference types: name -> referenced struct
    readonly Dictionary<string, uint> _bufferRefPtrIds = [];    // the materialized PhysicalStorageBuffer pointer type id
    readonly HashSet<uint> _namedStructs = [];   // user-struct type ids already given OpName/OpMemberName
    readonly Dictionary<string, uint> _indexableTemps = [];   // Function-local copies of const arrays for dynamic indexing
    readonly Dictionary<StructType, uint> _blockStructIds = [];   // decorated block struct ids, so an array-of-block reuses the same (Block-decorated) struct type
    readonly Dictionary<StructType, uint> _plainStructIds = [];   // plain (undecorated) struct ids, so a struct accessed repeatedly is built once, not re-keyed each time
    readonly Dictionary<string, Value> _constants = [];   // const scalars/vectors
    readonly Dictionary<string, CVal> _constValues = [];   // folded value of const globals, for further folding
    readonly Dictionary<string, (GlslType Type, Expr Init, int SpecId)> _pendingSpecConstants = [];   // constant_id specs, materialized on first use
    // A const whose initializer reads a specialization constant cannot fold: it stays specializable as a
    // chain of OpSpecConstantOp. Like any const it materializes lazily, at its first reference.
    readonly Dictionary<string, Expr> _pendingSpecExprs = [];
    readonly HashSet<string> _specConstNames = [];   // consts that are (or derive from) a specialization constant
    readonly HashSet<uint> _specConstIds = [];       // their ids, so an operand can be recognised as specializable
    readonly HashSet<uint> _nonUniformIds = [];      // ids tainted by nonuniformEXT; propagate NonUniform through access chains/loads
    readonly Dictionary<string, (GlslType Type, uint VarId)> _locals = [];
    readonly Stack<HashSet<string>> _scopeNames = new();   // names declared directly in each open block, for same-scope redeclaration checks
    readonly Dictionary<(StorageClass Storage, int Index), List<(int Lo, int Hi, int CompLo, int CompHi)>> _locationUse = [];   // occupied interface location/component ranges, for overlap checks
    // Opt-in: auto-assign a location to any in/out that omits layout(location=...), mirroring glslang's
    // --auto-map-locations. Off by default, matching glslang's default (which errors on a missing location).
    public bool AutoMapLocations;
    bool _pushConstantSeen;   // at most one push_constant block per shader
    // glslang creates an UNINITIALIZED local (`vec3 c;`) lazily at its first use, not at its declaration,
    // so its result id is interleaved with whatever the intervening code allocates. We mirror that: the
    // declared type is recorded here and DeclareLocal runs on first reference.
    readonly Dictionary<string, (GlslType Type, bool Relaxed)> _pendingLocals = [];
    readonly Dictionary<string, GlslType> _pendingPrivates = [];   // rayQuery locals: Private module-scope
                                                                   // variables, materialized on first reference
                                                                   // like glslang (an unreferenced one emits nothing)
    readonly List<uint> _interface = [];
    readonly List<Instruction> _vars = [];
    readonly List<Instruction> _code = [];
    readonly Stack<(uint Merge, uint Continue)> _loops = new();
    uint _glPerVertex;
    // A `out/in gl_PerVertex { ... }` redeclaration overrides the default per-vertex block: members appear
    // in the declared order and gl_ClipDistance/gl_CullDistance take their declared array sizes. _perVertexOut
    // is the single output block (vertex/geometry/tess-eval); _perVertexIn is the gl_in[] input array element.
    List<(string Name, GlslType Type)>? _perVertexOut, _perVertexIn;
    bool _usesStreams;   // a geometry shader with layout(stream=...) outputs: gl_PerVertex gets Stream 0
    uint _glIn, _glOut, _glTessInner, _glTessOuter;   // geometry/tessellation builtin arrays
    bool _invariantPosition;   // `invariant gl_Position;` decorates the gl_PerVertex Position member
    readonly HashSet<string> _invariantVars = [];   // `invariant name;` redeclarations of user outputs
    (uint Value, int? SpecId)[] _localSize = [];   // compute local size per axis, for gl_WorkGroupSize
    uint _wgSizeConst;   // the gl_WorkGroupSize constant, built on first use (or at the end if unused)
    uint _glClipFrag, _glCullFrag;   // standalone gl_ClipDistance/gl_CullDistance input arrays in a fragment shader
    ExecutionMode? _depthCondition;   // conservative-depth mode from a `layout(depth_*) out float gl_FragDepth;` redeclaration
    uint _currentBlock;   // label of the basic block currently being emitted (for OpPhi predecessors)
    GlslType _currentReturnType = Types.Void;   // return type of the function being lowered
    bool _terminated;   // whether the current basic block already has a terminator

    // Rewinds a reused lowerer for the next compile: every collection is cleared (keeping capacity, so the
    // backing arrays are not reallocated) and every piece of scalar state reset to its default. The lazy
    // builtin-variable ids (_glPerVertex etc.) and the one-shot flags (_pushConstantSeen) MUST reset to 0/
    // false or a stale id / false rejection would leak into the next compile.
    public Lowerer Reset(ShaderStage stage, SpirVVersion version, bool autoMapLocations)
    {
        this.stage = stage;
        this.version = version;
        AutoMapLocations = autoMapLocations;
        _m.Reset((uint)version);

        _globals.Clear(); _globalDeclNames.Clear(); _pendingGlobals.Clear(); _blockMembers.Clear();
        _blockStructs.Clear(); _bufferBlockStructs.Clear(); _blockLayout.Clear(); _layoutStructIds.Clear(); _layoutArrayIds.Clear();
        _pendingBlockBuilds.Clear(); _relaxedIds.Clear(); _valueParams.Clear(); _outParams.Clear();
        _varyingNames.Clear(); _preciseVars.Clear(); _builtinVars.Clear(); _userFunctions.Clear();
        _structs.Clear(); _bufferRefs.Clear(); _bufferRefPtrIds.Clear(); _namedStructs.Clear();
        _indexableTemps.Clear(); _blockStructIds.Clear(); _plainStructIds.Clear(); _constants.Clear();
        _constValues.Clear(); _pendingSpecConstants.Clear(); _pendingSpecExprs.Clear(); _specConstNames.Clear();
        _specConstIds.Clear(); _nonUniformIds.Clear(); _locals.Clear(); _scopeNames.Clear();
        _locationUse.Clear(); _pendingLocals.Clear(); _pendingPrivates.Clear(); _interface.Clear(); _vars.Clear();
        _code.Clear(); _loops.Clear(); _invariantVars.Clear(); _xfbStrides.Clear();
        _rtByLoc.Clear(); _resTypeNamed.Clear(); _bufferRefQuals.Clear(); _bufferRefAlign.Clear();
        _definedBufferRefs.Clear(); _bufferRefStructIds.Clear(); _bufferRefMemberIds.Clear(); _decorated.Clear();

        _defaultFloatMediump = _defaultIntMediump = _isEs = _vmmEnabled = _vmmPragma = false;
        _usesStreams = _invariantPosition = _terminated = _nvMesh = _nvRayTracing = _xfbMode = _pushConstantSeen = false;
        _addressingModel = AddressingModel.Logical;
        _glPerVertex = _glIn = _glOut = _glTessInner = _glTessOuter = _glClipFrag = _glCullFrag = 0;
        _glMeshVertices = _glMeshIndices = _glMeshPrimitives = _glMeshVerticesNV = _glPrimIndicesNV = _glPrimCountNV = 0;
        _currentBlock = _wgSizeConst = 0;
        _depthCondition = null;
        _currentReturnType = Types.Void;
        _inVertexCount = 32; _outVertexCount = 0;
        _meshMaxVertices = _meshMaxPrimitives = 0;
        _meshPrimitive = "triangles";
        _taskPayloadName = null;
        _lowerLine = 0;
        _exprDepth = 0;
        _currentBody = null;
        _readOnlyVars.Clear();
        _readOnlyMembers.Clear();
        _writeOnlyVars.Clear();
        _writeOnlyMembers.Clear();
        _memberIndexOf.Clear();
        _perVertexOut = _perVertexIn = null;
        _localSize = [];
        return this;
    }

    /// <summary>Drop the references the last compile left behind (AST nodes, symbol tables, the lowered module's
    /// contents) once its words are serialized, so they are garbage during optimization instead of being
    /// carried (and promoted) through it. The next Reset re-arms the instance as usual.</summary>
    public void ReleaseCompileState()
    {
        Reset(stage, version, AutoMapLocations);
        _preciseOps = new(ReferenceEqualityComparer.Instance);
    }

    public Module Lower(TranslationUnit unit)
    {
        // Lowering mutates this instance and its pooled Module, so it cannot be re-entered (a nested compile
        // on the same thread would otherwise silently corrupt the outer one's output).
        if (_lowering) throw new InvalidOperationException("Lowerer.Lower is already running on this instance; use a separate Lowerer for a nested compile.");
        _lowering = true;
        // A Lowerer that does not own this thread's pooled resources must not rewind or draw from the
        // shared instruction pool either.
        if (!_ownsPool) InstrPool.PushBypass();
        try { return LowerCore(unit); }
        catch (GlslParseException e) when (!e.Located) { throw e.Resolve(unit.SourceNames); }
        finally { _lowering = false; if (!_ownsPool) InstrPool.PopBypass(); }
    }

    Module LowerCore(TranslationUnit unit)
    {
        // Rewind this thread's instruction pool so the whole module reuses the previous compile's objects.
        InstrPool.Reset();
        // glslang's PropagateNoContraction runs over the whole AST before any code is generated.
        _preciseOps = PreciseAnalysis.Run(unit).Marked;
        // The legacy GL_NV_mesh_shader path uses the NV capability/extension/execution model and a richer
        // per-vertex block (with the PerViewNV members) instead of the EXT ones.
        _nvMesh = stage == ShaderStage.Mesh && (unit.Extensions?.Contains("GL_NV_mesh_shader") ?? false);
        // The legacy GL_NV_ray_tracing path shares KHR execution models, storage classes and builtins, but uses
        // the NV capability/extension and the NV trace/callable ops (which take the payload location as a plain int).
        _nvRayTracing = IsRayTracing(stage) && (unit.Extensions?.Contains("GL_NV_ray_tracing") ?? false);
        // Geometry, Tessellation and mesh/task each imply Shader, so glslang emits only the stage
        // capability. Mesh/task additionally need the SPV_EXT_mesh_shader extension.
        _m.AddCapability(stage switch
        {
            ShaderStage.Geometry => Capability.Geometry,
            ShaderStage.TessControl or ShaderStage.TessEvaluation => Capability.Tessellation,
            ShaderStage.Mesh or ShaderStage.Task => _nvMesh ? Capability.MeshShadingNV : Capability.MeshShadingEXT,
            _ when IsRayTracing(stage) => _nvRayTracing ? Capability.RayTracingNV : Capability.RayTracingKHR,
            _ => Capability.Shader,
        });
        if (stage is ShaderStage.Mesh or ShaderStage.Task) _m.AddExtension(_nvMesh ? "SPV_NV_mesh_shader" : "SPV_EXT_mesh_shader");
        if (IsRayTracing(stage)) _m.AddExtension(_nvRayTracing ? "SPV_NV_ray_tracing" : "SPV_KHR_ray_tracing");

        // Match glslang's module setup and id-allocation order exactly (SpvBuilder::makeEntryPoint):
        // the GLSL.std.450 import is always id 1 (even when unused), then void(2), main's void() type(3),
        // the main function(4) and its entry label(5) are reserved before any globals are created.
        // GL_EXT_buffer_reference: a buffer_reference block declares a 64-bit pointer type, so the whole
        // module switches to the PhysicalStorageBuffer64 addressing model. Register the reference types up
        // front so member declarations can resolve them.
        RegisterBufferReferences(unit);
        bool physicalBuffers = _bufferRefs.Count > 0;
        if (physicalBuffers)
        {
            _m.AddCapability(Capability.PhysicalStorageBufferAddresses);
            AddIncorporatedExtension("SPV_KHR_physical_storage_buffer", SpirVVersion.V1_5);
        }

        _m.ImportExtInstSet("GLSL.std.450");
        _defaultFloatMediump = unit.DefaultFloatRelaxed;   // `precision mediump/lowp float` relaxes user float values
        _isEs = unit.IsEs;
        // ES fragment shaders default int/uint to mediump (glslang ParseHelper); a `precision <q> int` overrides.
        _defaultIntMediump = unit.IntPrecisionRelaxed ?? (_isEs && stage == ShaderStage.Fragment);
        _addressingModel = physicalBuffers ? AddressingModel.PhysicalStorageBuffer64 : AddressingModel.Logical;
        // #pragma use_vulkan_memory_model opts the whole module into the Vulkan memory model up front.
        _vmmPragma = unit.Pragmas?.Contains("use_vulkan_memory_model") ?? false;
        // A `stream = N` appears either on an output variable or on the bare `layout(..., stream=N) out;`.
        _usesStreams = unit.Declarations.Any(d =>
            d is GlobalVar g && g.Qualifiers.Has("out") && g.Qualifiers.Layout.Any(i => i.Name == "stream")
            || d is LayoutDecl l && l.Qualifiers.Has("out") && l.Qualifiers.Layout.Any(i => i.Name == "stream"));
        _m.SetMemoryModel(_addressingModel, MemoryModel.GLSL450);
        if (_vmmPragma) EnableVulkanMemoryModel();
        _m.AddSource(unit.IsEs ? SourceLanguage.ESSL : SourceLanguage.GLSL, (uint)unit.Version);

        uint voidType = _m.TypeVoid();
        _m.TypeFunction(voidType);
        var allDefs = unit.Declarations.OfType<FunctionDef>().ToList();
        var mainDef = allDefs.FirstOrDefault(f => f.Name == "main")
            ?? throw new GlslParseException("shader has no entry point: a 'void main()' function is required");
        if (allDefs.Count(f => f.Name == "main") > 1)
            throw new GlslParseException("'main' is already defined");
        if (mainDef.Parameters.Count > 0)
            throw new GlslParseException("'main' cannot take parameters");
        if (mainDef.ReturnType.Name != "void")
            throw new GlslParseException("'main' must return void");
        // glslang validates every function body up front, uncalled ones included: a non-void function
        // must return a value (a bare `return;` errors at its own line) and must return at all.
        // Prism lowers bodies lazily on call, so an uncalled broken function would otherwise compile.
        foreach (var f in allDefs)
            if (f.ReturnType.Name != "void")
                CheckFunctionReturns(f);
        uint mainId = _m.AllocId();
        uint mainLabel = _m.AllocId();
        _m.AddName(mainId, "main");

        // glslang keeps requested extensions in a std::set, so OpSourceExtension is emitted in sorted
        // (ordinal) order. The GL_KHR_shader_subgroup_{vote,ballot,arithmetic,shuffle,...} extensions imply
        // the base one, which glslang records too; the standalone GL_KHR_shader_subgroup_rotate does not.
        var exts = new HashSet<string>(unit.Extensions ?? []);
        // glslang gates some extensions on the target SPIR-V version, rejecting the #extension itself
        // ("'GL_EXT_ray_tracing' : not supported for current targeted SPIR-V version") before any
        // lowering: EXT ray tracing and mesh shading need SPIR-V 1.4's interface/storage-class rules
        // (oracle-verified at vulkan1.0). The NV paths predate them and are legal down to 1.0, so they
        // are not gated. Emitting the extension at a lower version would produce a module spirv-val
        // (and every driver) rejects.
        if (version < SpirVVersion.V1_4)
            foreach (var ext in exts)
                if (ext is "GL_EXT_ray_tracing" or "GL_EXT_mesh_shader")
                    throw new GlslParseException($"'{ext}' : not supported for current targeted SPIR-V version");
        if (exts.Any(e => e.StartsWith("GL_KHR_shader_subgroup", StringComparison.Ordinal) && e != "GL_KHR_shader_subgroup_rotate")
            || exts.Contains("GL_NV_shader_subgroup_partitioned"))
            exts.Add("GL_KHR_shader_subgroup_basic");
        // glslang's updateExtensionBehavior implications: the buffer reference add-ons enable the base
        // extension, as do the cooperative-matrix variants theirs.
        if (exts.Contains("GL_EXT_buffer_reference2") || exts.Contains("GL_EXT_buffer_reference_uvec2")) exts.Add("GL_EXT_buffer_reference");
        if (exts.Contains("GL_NV_integer_cooperative_matrix")) exts.Add("GL_NV_cooperative_matrix");
        if (exts.Contains("GL_NV_cooperative_matrix2")) exts.Add("GL_KHR_cooperative_matrix");
        // GL_EXT_shader_subgroup_extended_types_X implies the matching explicit-arithmetic-types_X.
        foreach (var w in new[] { "float16", "int8", "int16", "int64" })
            if (exts.Contains($"GL_EXT_shader_subgroup_extended_types_{w}")) exts.Add($"GL_EXT_shader_explicit_arithmetic_types_{w}");
        foreach (var ext in exts.OrderBy(e => e, StringComparer.Ordinal)) _m.AddSourceExtension(ext);

        ComputeVertexCounts(unit);
        ComputeXfb(unit);
        if (stage == ShaderStage.Mesh) ParseMeshOutput(unit);
        _localSize = LocalSizeDims(unit);   // gl_WorkGroupSize may be read from a body, before the mode is emitted
        // Unless a dimension is specializable, gl_WorkGroupSize is a compile-time constant, so reads of it
        // fold (glslang turns `gl_WorkGroupSize.x` into a literal rather than extracting from the composite).
        if (stage == ShaderStage.Compute && !_localSize.Any(d => d.SpecId is not null))
            _constValues["gl_WorkGroupSize"] =
                new CAgg(UVec3, [.. _localSize.Select(d => (CVal)new CScalar(Scalar.UInt, 0, d.Value))]);

        _structs["gl_TextureFootprint2DNV"] = FootprintStruct;
        foreach (var s in unit.Declarations.OfType<StructDef>())
            AtDecl(s, () =>
            {
                if (s.Members.Count == 0)
                    throw new GlslParseException($"struct '{s.Name}' must declare at least one member");
                if (s.Members.Any(f => f.Declarators.Any(d => d.Init is not null)))
                    throw new GlslParseException($"struct '{s.Name}' members cannot have initializers");
                if (_structs.ContainsKey(s.Name))
                    throw new GlslParseException($"'{s.Name}' : struct redefinition");
                var fields = s.Members.SelectMany(f => f.Declarators.Select(d => (d.Name, Type: ApplyArrayDims(ResolveTypeRef(f.Type), d.Array)))).ToList();
                // glslang reports a duplicate member at the DUPLICATE's line, not the struct's.
                var seen = new HashSet<string>();
                foreach (var f in s.Members)
                    foreach (var d in f.Declarators)
                        if (!seen.Add(d.Name))
                            throw new GlslParseException($"struct '{s.Name}' has duplicate member names", d.Line);
                _structs[s.Name] = new StructType(s.Name, fields);
            });

        var mainInfo = new UserFunction(Types.Void, []) { Id = mainId };
        _userFunctions["main"] = [mainInfo];

        foreach (var redecl in unit.Declarations.OfType<QualifierRedecl>())
            ApplyQualifierRedecl(redecl);
        foreach (var decl in unit.Declarations.OfType<GlobalVar>())
            AtDecl(decl, () => EmitGlobal(decl));
        foreach (var block in unit.Declarations.OfType<InterfaceBlock>())
            AtDecl(block, () => EmitBlock(block));

        // Pruning needs the structs, globals and blocks in scope so a call's arguments can be typed.
        var defs = ReachableFunctions(allDefs, mainDef, GlobalInitializers(unit));
        var helperDefs = defs.Where(f => f != mainDef).ToList();
        var infos = new Dictionary<FunctionDef, UserFunction> { [mainDef] = mainInfo };
        foreach (var f in helperDefs) infos[f] = RegisterFunction(f);   // signatures only; ids assigned when declared

        // glslang declares the helper functions first (makeFunctions; main's id and entry label are
        // already reserved), then translates every body in SOURCE order - so a helper defined before
        // main is traversed first. The functions section still lists main first regardless.
        var declared = new Dictionary<FunctionDef, DeclaredFn>();
        foreach (var f in helperDefs) declared[f] = DeclareFunction(f, infos[f]);
        declared[mainDef] = DeclareFunction(mainDef, mainInfo, mainLabel);
        EmitGlobalInitializers(unit);
        var bodies = defs.ToDictionary(f => f, f => EmitBody(declared[f]));
        _m.AddFunction(bodies[mainDef]);
        foreach (var f in helperDefs) _m.AddFunction(bodies[f]);
        foreach (var b in bodies.Values) ReturnBody(b);   // the module copied them; recycle the assembly lists

        // glslang keeps every declared global as a "linker object", even ones the body never references:
        // an unused in/out variable, an unreferenced interface block, a specialization constant nothing
        // reads, or a const that folded away entirely. They are all materialized after the bodies, in
        // declaration order, so their ids come last (a const array is the exception: it only ever exists
        // at its use sites).
        foreach (var decl in unit.Declarations)
            switch (decl)
            {
                case GlobalVar g:
                    foreach (var d in g.Declarators)
                    {
                        if (_pendingGlobals.ContainsKey(d.Name))
                            ResolveGlobal(d.Name);
                        else if (_pendingSpecConstants.TryGetValue(d.Name, out var sc))
                            MaterializeSpecConstant(d.Name, sc);
                        else if (_pendingSpecExprs.ContainsKey(d.Name))
                            SpecConstantExpr(new Ident(d.Name));
                        else if (_constValues.TryGetValue(d.Name, out var cv) && !_constants.ContainsKey(d.Name))
                            _constants[d.Name] = Materialize(cv);
                    }
                    break;
                case InterfaceBlock { InstanceName: { } instance } when _pendingGlobals.ContainsKey(instance):
                    ResolveGlobal(instance);
                    break;
                case InterfaceBlock ib when _pendingBlockBuilds.TryGetValue(ib, out var build):
                    _pendingBlockBuilds.Remove(ib);
                    build();
                    break;
                // Declaring local_size makes gl_WorkGroupSize an editable variable, which tracks it as a
                // linker object right there, so it materializes in source order with the other globals.
                case LayoutDecl l when stage is ShaderStage.Compute or ShaderStage.Mesh or ShaderStage.Task
                        && l.Qualifiers.Layout.Any(i => i.Name.StartsWith("local_size", StringComparison.Ordinal)):
                    WorkGroupSizeConstant();
                    break;
            }

        // SPIR-V 1.4 raised the entry point interface to list EVERY global variable, not just the
        // Input/Output ones collected in _interface. Below 1.4 the interface must contain ONLY
        // Input/Output variables (glslang matches: a used rayQueryEXT lives in Private and is NOT
        // listed) -- listing anything else makes the module invalid.
        uint[] iface = version >= SpirVVersion.V1_4
            ? [.. _m.GlobalVariables]
            : [.. _interface.Where(id => _m.GlobalStorage(id) is StorageClass.Input or StorageClass.Output)];
        _m.AddEntryPoint(ExecutionModelOf(stage), mainId, "main", iface);
        if (stage == ShaderStage.Fragment)
        {
            if (InLayout(unit, "pixel_center_integer")) _m.AddExecutionMode(mainId, ExecutionMode.PixelCenterInteger);
            _m.AddExecutionMode(mainId, ExecutionMode.OriginUpperLeft);
            // Emission order follows glslang (GlslangToSpv): early/post-depth modes, then the depth and
            // stencil replacement modes, then the depth condition, and the interlock modes last.
            // GL_ARB_post_depth_coverage additionally turns early_fragment_tests on (ParseHelper).
            bool postDepth = InLayout(unit, "post_depth_coverage");
            if (InLayout(unit, "early_fragment_tests")
                || (postDepth && unit.Extensions?.Contains("GL_ARB_post_depth_coverage") == true))
                _m.AddExecutionMode(mainId, ExecutionMode.EarlyFragmentTests);
            if (InLayout(unit, "early_and_late_fragment_tests_amd"))
            {
                _m.AddExtension("SPV_AMD_shader_early_and_late_fragment_tests");
                _m.AddExecutionMode(mainId, ExecutionMode.EarlyAndLateFragmentTestsAMD);
            }
            if (postDepth)
            {
                _m.AddCapability(Capability.SampleMaskPostDepthCoverage);
                _m.AddExtension("SPV_KHR_post_depth_coverage");
                _m.AddExecutionMode(mainId, ExecutionMode.PostDepthCoverage);
            }
            if (_builtinVars.ContainsKey("gl_FragDepth"))
                _m.AddExecutionMode(mainId, ExecutionMode.DepthReplacing);
            if (_builtinVars.ContainsKey("gl_FragStencilRefARB"))
                _m.AddExecutionMode(mainId, ExecutionMode.StencilRefReplacingEXT);
            if (_depthCondition is ExecutionMode dc)
                _m.AddExecutionMode(mainId, dc);
            if (InLayout(unit, "pixel_interlock_ordered"))
            {
                _m.AddCapability(Capability.FragmentShaderPixelInterlockEXT);
                _m.AddExtension("SPV_EXT_fragment_shader_interlock");
                _m.AddExecutionMode(mainId, ExecutionMode.PixelInterlockOrderedEXT);
            }
            if (InLayout(unit, "sample_interlock_ordered"))
            {
                _m.AddCapability(Capability.FragmentShaderSampleInterlockEXT);
                _m.AddExtension("SPV_EXT_fragment_shader_interlock");
                _m.AddExecutionMode(mainId, ExecutionMode.SampleInterlockOrderedEXT);
            }
        }
        // A work-group size only means something in a stage that has work groups.
        if (stage is not (ShaderStage.Compute or ShaderStage.Mesh or ShaderStage.Task))
            foreach (var ld in unit.Declarations.OfType<LayoutDecl>())
                foreach (var it in ld.Qualifiers.Layout)
                    if (it.Name.StartsWith("local_size_", StringComparison.Ordinal))
                        throw new GlslParseException($"'{it.Name}' is only valid in a compute, task, or mesh shader");
        if (stage is ShaderStage.Compute or ShaderStage.Mesh or ShaderStage.Task)
        {
            _m.AddExecutionMode(mainId, ExecutionMode.LocalSize, _localSize[0].Value, _localSize[1].Value, _localSize[2].Value);
        }
        if (stage == ShaderStage.Compute && InLayout(unit, "derivative_group_quadsNV"))
        {
            _m.AddCapability(Capability.ComputeDerivativeGroupQuadsNV);
            _m.AddExtension("SPV_NV_compute_shader_derivatives");
            _m.AddExecutionMode(mainId, ExecutionMode.DerivativeGroupQuadsNV);
        }
        if (stage == ShaderStage.Compute && InLayout(unit, "derivative_group_linearNV"))
        {
            _m.AddCapability(Capability.ComputeDerivativeGroupLinearNV);
            _m.AddExtension("SPV_NV_compute_shader_derivatives");
            _m.AddExecutionMode(mainId, ExecutionMode.DerivativeGroupLinearNV);
        }
        if (stage == ShaderStage.Mesh) EmitMeshModes(unit, mainId);
        EmitStageModes(unit, mainId);
        _m.PostProcessNarrowTypes((uint)version);
        _m.PostProcessImageSignExtension();

        return _m;
    }

    // glslang deletes every function body the entry point cannot reach before generating any SPIR-V, so
    // an included library's unused helpers contribute no functions, types or constants at all
    // (TIntermediate::checkCallGraphBodies). Its call graph is keyed by mangled name, so an unused
    // OVERLOAD is dropped while its siblings are kept.
    List<FunctionDef> ReachableFunctions(List<FunctionDef> defs, FunctionDef mainDef, IEnumerable<Expr> initializers)
    {
        // Every signature has to be in scope to resolve an overloaded call, so register them all, walk,
        // then put the table back for the real pass to rebuild over only what survived.
        var saved = new Dictionary<string, List<UserFunction>>(_userFunctions);
        var owner = new Dictionary<UserFunction, FunctionDef>();
        _userFunctions.Clear();
        foreach (var f in defs) owner[RegisterFunction(f)] = f;

        var reached = new HashSet<FunctionDef> { mainDef };
        var queue = new Queue<FunctionDef>([mainDef]);
        var callEdges = new Dictionary<FunctionDef, HashSet<FunctionDef>>();   // caller -> callees, for recursion detection
        // A global initializer's code goes into the entry point, so whatever it calls is reachable too.
        _pendingLocals.Clear(); _pendingPrivates.Clear();
        Reach(mainDef, initializers.SelectMany(Calls));
        while (queue.Count > 0)
        {
            var def = queue.Dequeue();
            ScopeOf(def);
            Reach(def, Calls(def.Body));
        }

        void Reach(FunctionDef caller, IEnumerable<Call> calls)
        {
            foreach (var call in calls)
                if (CallsUserFunction(call)
                    && owner.TryGetValue(ResolveOverload(_userFunctions[call.Name], call.Args), out var callee))
                {
                    (callEdges.TryGetValue(caller, out var s) ? s : callEdges[caller] = []).Add(callee);
                    if (reached.Add(callee)) queue.Enqueue(callee);
                }
        }

        // glslang forbids recursion: a cycle in the call graph (a function reachable from itself) is an error.
        var mark = new Dictionary<FunctionDef, int>();   // 0/absent = unvisited, 1 = on stack, 2 = done
        bool Cycle(FunctionDef n)
        {
            mark[n] = 1;
            if (callEdges.TryGetValue(n, out var callees))
                foreach (var c in callees)
                    if (mark.GetValueOrDefault(c) == 1 || (mark.GetValueOrDefault(c) == 0 && Cycle(c)))
                        return true;
            mark[n] = 2;
            return false;
        }
        foreach (var def in reached)
            if (mark.GetValueOrDefault(def) == 0 && Cycle(def))
                throw new GlslParseException("recursion is not allowed: a function calls itself directly or indirectly");

        _pendingLocals.Clear(); _pendingPrivates.Clear();
        _userFunctions.Clear();
        foreach (var (name, list) in saved) _userFunctions[name] = list;
        return [.. defs.Where(reached.Contains)];
    }

    // Types the names a body can see, so that resolving an overloaded call inside it can infer its
    // argument types. Scopes are flattened: an overload is only ever chosen by an argument's type.
    bool _speculativeFold;

    void ScopeOf(FunctionDef def)
    {
        _pendingLocals.Clear(); _pendingPrivates.Clear();
        _speculativeFold = true;
        try { ScopeOfCore(def); }
        finally { _speculativeFold = false; }
    }

    void ScopeOfCore(FunctionDef def)
    {
        // Only the element type matters for resolving an overloaded call, and spec-constant array sizes
        // are not registered yet during this scan, so an unresolvable length is fine here.
        GlslType Typed(TypeRef t, IReadOnlyList<Expr?> array)
        {
            try { return ApplyArrayDims(ResolveTypeRef(t), array); }
            catch (GlslParseException) { return ResolveTypeRef(t); }
        }
        foreach (var p in def.Parameters)
            if (p.Name is not null)
                _pendingLocals[p.Name] = (Typed(p.Type, p.Array), false);
        foreach (var decl in LocalDecls(def.Body))
            foreach (var d in decl.Declarators)
            {
                _pendingLocals[d.Name] = (Typed(decl.Type, d.Array), false);
            }
    }

    // The largest constant index applied to `name` anywhere in a statement tree, or null if it is never
    // indexed by a constant. Used for GLSL implicit array sizing.
    int? MaxConstIndexOf(Stmt s, string name)
    {
        int? best = null;
        void Merge(int? v) { if (v is int x && (best is null || x > best)) best = x; }
        void Walk(Expr? e)
        {
            switch (e)
            {
                case null: return;
                case Index ix:
                    if (ix.Target is Ident id && id.Name == name && EvalConst(ix.Idx) is CScalar c) Merge((int)c.I);
                    Walk(ix.Target); Walk(ix.Idx); return;
                case Binary bi: Walk(bi.Left); Walk(bi.Right); return;
                case Unary u: Walk(u.Operand); return;
                case Conditional t: Walk(t.Cond); Walk(t.Then); Walk(t.Else); return;
                case Call cl: foreach (var a in cl.Args) Walk(a); return;
                case Method me: Walk(me.Target); foreach (var a in me.Args) Walk(a); return;
                case Member m: Walk(m.Target); return;
                default: return;
            }
        }
        void WalkStmt(Stmt? st)
        {
            switch (st)
            {
                case null: return;
                case Block b2: foreach (var x in b2.Statements) WalkStmt(x); return;
                case VarDeclStmt v: foreach (var d in v.Declarators) Walk(d.Init); return;
                case ExprStmt ex: Walk(ex.Expression); return;
                case IfStmt i: Walk(i.Cond); WalkStmt(i.Then); WalkStmt(i.Else); return;
                case ForStmt f: WalkStmt(f.Init); Walk(f.Cond); Walk(f.Update); WalkStmt(f.Body); return;
                case WhileStmt w: Walk(w.Cond); WalkStmt(w.Body); return;
                case DoWhileStmt d2: WalkStmt(d2.Body); Walk(d2.Cond); return;
                case SwitchStmt sw: Walk(sw.Value); foreach (var cse in sw.Cases) { Walk(cse.Label); foreach (var x in cse.Body) WalkStmt(x); } return;
                case ReturnStmt r: Walk(r.Value); return;
                default: return;
            }
        }
        WalkStmt(s);
        return best;
    }

    static IEnumerable<VarDeclStmt> LocalDecls(Stmt s) => s switch
    {
        VarDeclStmt d => [d],
        Block b => b.Statements.SelectMany(LocalDecls),
        IfStmt i => LocalDecls(i.Then).Concat(i.Else is null ? [] : LocalDecls(i.Else)),
        ForStmt f => (f.Init is null ? [] : LocalDecls(f.Init)).Concat(LocalDecls(f.Body)),
        WhileStmt w => LocalDecls(w.Body),
        DoWhileStmt d => LocalDecls(d.Body),
        SwitchStmt sw => sw.Cases.SelectMany(c => c.Body.SelectMany(LocalDecls)),
        _ => [],
    };

    static IEnumerable<Call> Calls(Stmt s) => s switch
    {
        Block b => b.Statements.SelectMany(Calls),
        VarDeclStmt d => d.Declarators.Select(x => x.Init).OfType<Expr>().SelectMany(Calls),
        ExprStmt e => Calls(e.Expression),
        IfStmt i => Calls(i.Cond).Concat(Calls(i.Then)).Concat(i.Else is null ? [] : Calls(i.Else)),
        ForStmt f => (f.Init is null ? [] : Calls(f.Init)).Concat(f.Cond is null ? [] : Calls(f.Cond))
                     .Concat(f.Update is null ? [] : Calls(f.Update)).Concat(Calls(f.Body)),
        WhileStmt w => Calls(w.Cond).Concat(Calls(w.Body)),
        DoWhileStmt d => Calls(d.Body).Concat(Calls(d.Cond)),
        SwitchStmt sw => Calls(sw.Value).Concat(sw.Cases.SelectMany(c =>
            (c.Label is null ? [] : Calls(c.Label)).Concat(c.Body.SelectMany(Calls)))),
        ReturnStmt r => r.Value is null ? [] : Calls(r.Value),
        _ => [],
    };

    static IEnumerable<Call> Calls(Expr e) => e switch
    {
        Call c => c.Args.SelectMany(Calls).Append(c),
        Method m => Calls(m.Target).Concat(m.Args.SelectMany(Calls)),
        Unary u => Calls(u.Operand),
        Binary b => Calls(b.Left).Concat(Calls(b.Right)),
        Conditional c => Calls(c.Cond).Concat(Calls(c.Then)).Concat(Calls(c.Else)),
        Index ix => Calls(ix.Target).Concat(Calls(ix.Idx)),
        Member m => Calls(m.Target),
        _ => [],
    };

    int _inVertexCount = 32;   // gl_MaxPatchVertices default (geometry overrides from its input primitive)
    int _outVertexCount;
    bool _nvMesh;                                   // legacy GL_NV_mesh_shader path (vs GL_EXT_mesh_shader)
    bool _nvRayTracing;                             // legacy GL_NV_ray_tracing path (vs GL_EXT_ray_tracing)
    bool _xfbMode;                                  // transform feedback: any output carries an xfb layout qualifier
    readonly Dictionary<int, int> _xfbStrides = []; // per xfb_buffer capture stride in bytes
    int _meshMaxVertices, _meshMaxPrimitives;   // mesh output caps, from layout(..., max_vertices=N, max_primitives=M) out
    string _meshPrimitive = "triangles";        // mesh output primitive: triangles / lines / points
    string? _taskPayloadName;                    // the taskPayloadSharedEXT global, passed to EmitMeshTasksEXT
    readonly Dictionary<(StorageClass, int), string> _rtByLoc = [];   // ray payload/callable-data globals by location

    void ComputeVertexCounts(TranslationUnit unit)
    {
        foreach (var l in unit.Declarations.OfType<LayoutDecl>())
            foreach (var item in l.Qualifiers.Layout)
            {
                if ((item.Name is "max_vertices" or "vertices") && item.Value is Literal n) _outVertexCount = (int)ParseIntLiteral(n.Text);
                if (stage == ShaderStage.Geometry && l.Qualifiers.Has("in") && InputPrimitiveVertices(item.Name) is int c) _inVertexCount = c;
            }
    }

    // Transform feedback: collect which outputs are captured and each xfb_buffer's stride. The stride is
    // an explicit xfb_stride when given, otherwise the tight extent of the buffer's captured outputs.
    void ComputeXfb(TranslationUnit unit)
    {
        // Filter to the outputs that carry an xfb qualifier BEFORE resolving their type: a non-xfb output
        // may reference a user struct that is not registered yet at this point.
        var xfbOuts = unit.Declarations.OfType<GlobalVar>()
            .Where(g => g.Qualifiers.Has("out")
                && (LayoutInt(g.Qualifiers, "xfb_offset") is not null || LayoutInt(g.Qualifiers, "xfb_buffer") is not null || LayoutInt(g.Qualifiers, "xfb_stride") is not null))
            .SelectMany(g => g.Declarators.Select(d => (g.Qualifiers, Type: ApplyArrayDims(ResolveTypeRef(g.Type), d.Array))))
            .ToList();
        if (xfbOuts.Count == 0) return;
        _xfbMode = true;
        foreach (var o in xfbOuts)
        {
            int buffer = LayoutInt(o.Qualifiers, "xfb_buffer") ?? 0;
            int stride = _xfbStrides.GetValueOrDefault(buffer);
            if (LayoutInt(o.Qualifiers, "xfb_stride") is int explicitStride) stride = Math.Max(stride, explicitStride);
            if (LayoutInt(o.Qualifiers, "xfb_offset") is int off) stride = Math.Max(stride, off + Size(o.Type, BlockLayout.Scalar));
            _xfbStrides[buffer] = stride;
        }
    }

    static int? InputPrimitiveVertices(string name) => name switch
    {
        "points" => 1, "lines" => 2, "lines_adjacency" => 4,
        "triangles" => 3, "triangles_adjacency" => 6, "quads" => 4, "isolines" => 2,
        _ => null,
    };

    // The mesh output layout `layout(triangles|lines|points, max_vertices=N, max_primitives=M) out;`.
    // The per-vertex output arrays (gl_MeshVerticesEXT and user out arrays) are sized to max_vertices.
    void ParseMeshOutput(TranslationUnit unit)
    {
        var outLayout = unit.Declarations.OfType<LayoutDecl>().FirstOrDefault(l => l.Qualifiers.Has("out"));
        var q = outLayout?.Qualifiers ?? Qualifiers.None;
        _meshMaxVertices = LayoutInt(q, "max_vertices") ?? 0;
        _meshMaxPrimitives = LayoutInt(q, "max_primitives") ?? 0;
        foreach (var item in q.Layout)
            if (item.Name is "triangles" or "lines" or "points") _meshPrimitive = item.Name;
        _outVertexCount = _meshMaxVertices;   // user `out T x[];` arrays size to max_vertices
    }

    void EmitMeshModes(TranslationUnit unit, uint mainId)
    {
        _m.AddExecutionMode(mainId, ExecutionMode.OutputVertices, (uint)_meshMaxVertices);
        _m.AddExecutionMode(mainId, ExecutionMode.OutputPrimitivesEXT, (uint)_meshMaxPrimitives);
        _m.AddExecutionMode(mainId, _meshPrimitive switch
        {
            "lines" => ExecutionMode.OutputLinesEXT,
            "points" => ExecutionMode.OutputPoints,
            _ => ExecutionMode.OutputTrianglesEXT,
        });
    }

    void EmitStageModes(TranslationUnit unit, uint mainId)
    {
        var inLayout = unit.Declarations.OfType<LayoutDecl>().FirstOrDefault(l => l.Qualifiers.Has("in"));
        var outLayout = unit.Declarations.OfType<LayoutDecl>().FirstOrDefault(l => l.Qualifiers.Has("out"));

        if (_xfbMode)
        {
            _m.AddCapability(Capability.TransformFeedback);
            _m.AddExecutionMode(mainId, ExecutionMode.Xfb);
        }

        if (stage == ShaderStage.Geometry)
        {
            var inItems = (inLayout?.Qualifiers.Layout ?? []).Select(i => i.Name).ToList();
            if (!inItems.Any(n => n is "points" or "lines" or "lines_adjacency" or "triangles" or "triangles_adjacency"))
                throw new GlslParseException("a geometry shader requires an input primitive layout (points, lines, lines_adjacency, triangles, or triangles_adjacency)");
            foreach (var item in inLayout!.Qualifiers.Layout)
                if (InputPrimitiveMode(item.Name) is ExecutionMode m) _m.AddExecutionMode(mainId, m);
            _m.AddExecutionMode(mainId, ExecutionMode.Invocations, (uint)(LayoutInt(inLayout.Qualifiers, "invocations") ?? 1));
            // NV geometry passthrough with no explicit output layout derives the output primitive and
            // vertex count from the input primitive (GL_NV_geometry_shader_passthrough).
            bool passthrough = unit.Declarations.OfType<InterfaceBlock>().Any(b => b.Qualifiers.Layout.Any(i => i.Name == "passthrough"));
            var outModes = (outLayout?.Qualifiers.Layout ?? []).Select(i => OutputPrimitiveMode(i.Name)).Where(m => m is not null).ToList();
            if (!passthrough && outModes.Count == 0)
                throw new GlslParseException("a geometry shader requires an output primitive layout (points, line_strip, or triangle_strip)");
            if (!passthrough && _outVertexCount <= 0)
                throw new GlslParseException("a geometry shader requires a layout(max_vertices = N) out; declaration");
            if (outModes.Count == 0 && passthrough && DerivedPassthroughOutput() is ExecutionMode derived)
                _m.AddExecutionMode(mainId, derived);
            else
                foreach (var m in outModes) _m.AddExecutionMode(mainId, m!.Value);
            _m.AddExecutionMode(mainId, ExecutionMode.OutputVertices, (uint)(passthrough && _outVertexCount == 0 ? _inVertexCount : _outVertexCount));
        }
        else if (stage == ShaderStage.TessControl)
        {
            _m.AddExecutionMode(mainId, ExecutionMode.OutputVertices, (uint)_outVertexCount);
        }
        else if (stage == ShaderStage.TessEvaluation)
        {
            // glslang emits tess-eval modes in a canonical order (primitive, spacing, winding, point
            // mode) and always emits a spacing and a winding: TIntermediate::finalCheck defaults them to
            // equal_spacing and counter-clockwise.
            var items = (inLayout?.Qualifiers.Layout ?? []).Select(i => i.Name).ToList();
            foreach (var name in items)
                if (name is "triangles" or "quads" or "isolines" && InputPrimitiveMode(name) is ExecutionMode m) _m.AddExecutionMode(mainId, m);
            string[] spacings = ["equal_spacing", "fractional_even_spacing", "fractional_odd_spacing"];
            foreach (var name in items)
                if (spacings.Contains(name)) _m.AddExecutionMode(mainId, InputPrimitiveMode(name)!.Value);
            if (!items.Any(spacings.Contains)) _m.AddExecutionMode(mainId, ExecutionMode.SpacingEqual);
            _m.AddExecutionMode(mainId, items.Contains("cw") ? ExecutionMode.VertexOrderCw : ExecutionMode.VertexOrderCcw);
            if (items.Contains("point_mode")) _m.AddExecutionMode(mainId, ExecutionMode.PointMode);
        }
    }

    static ExecutionMode? InputPrimitiveMode(string name) => name switch
    {
        "points" => ExecutionMode.InputPoints,
        "lines" => ExecutionMode.InputLines,
        "lines_adjacency" => ExecutionMode.InputLinesAdjacency,
        "triangles" => ExecutionMode.Triangles,
        "triangles_adjacency" => ExecutionMode.InputTrianglesAdjacency,
        "quads" => ExecutionMode.Quads,
        "isolines" => ExecutionMode.Isolines,
        "equal_spacing" => ExecutionMode.SpacingEqual,
        "fractional_even_spacing" => ExecutionMode.SpacingFractionalEven,
        "fractional_odd_spacing" => ExecutionMode.SpacingFractionalOdd,
        "cw" => ExecutionMode.VertexOrderCw,
        "ccw" => ExecutionMode.VertexOrderCcw,
        "point_mode" => ExecutionMode.PointMode,
        _ => null,
    };

    static ExecutionMode? OutputPrimitiveMode(string name) => name switch
    {
        "points" => ExecutionMode.OutputPoints,
        "line_strip" => ExecutionMode.OutputLineStrip,
        "triangle_strip" => ExecutionMode.OutputTriangleStrip,
        _ => null,
    };

    // Transform-feedback decorations on a captured output: XfbBuffer + the buffer's XfbStride, then the
    // capture Offset. glslang emits these right after Location.
    void DecorateXfb(uint var, Qualifiers quals)
    {
        if (!_xfbMode) return;
        if (LayoutInt(quals, "xfb_buffer") is int buffer)
        {
            _m.Decorate(var, Decoration.XfbBuffer, (uint)buffer);
            _m.Decorate(var, Decoration.XfbStride, (uint)_xfbStrides.GetValueOrDefault(buffer));
        }
        if (LayoutInt(quals, "xfb_offset") is int offset) _m.Decorate(var, Decoration.Offset, (uint)offset);
    }

    ExecutionMode? DerivedPassthroughOutput() => _inVertexCount switch
    {
        1 => ExecutionMode.OutputPoints,
        2 => ExecutionMode.OutputLineStrip,
        3 => ExecutionMode.OutputTriangleStrip,
        _ => null,
    };

    static BuiltIn PerVertexBuiltIn(string name) => name switch
    {
        "gl_Position" => BuiltIn.Position,
        "gl_PointSize" => BuiltIn.PointSize,
        "gl_ClipDistance" => BuiltIn.ClipDistance,
        "gl_CullDistance" => BuiltIn.CullDistance,
        _ => throw new GlslParseException($"'{name}' is not a gl_PerVertex member"),
    };

    UserFunction RegisterFunction(FunctionDef f)
    {
        RejectReservedName(f.Name);
        foreach (var p in f.Parameters) RejectReservedName(p.Name ?? "");
        var pars = f.Parameters.Select(p => new FnParam(
            p.Name ?? "",
            ApplyArrayDims(ResolveTypeRef(p.Type), p.Array),
            IsIn: !p.Qualifiers.Has("out") || p.Qualifiers.Has("inout"),
            IsOut: p.Qualifiers.Has("out") || p.Qualifiers.Has("inout"),
            ByValue: p.Qualifiers.Has("const"))).ToList();
        // Overloading a built-in name is legal GLSL, but a signature the built-in itself could have is a
        // redefinition. No built-in takes a user struct, so a struct parameter is what makes it an overload.
        if (BuiltinFunctionNames.Contains(f.Name) && !pars.Any(p => ContainsUserStruct(p.Type)))
            throw new GlslParseException($"'{f.Name}' : cannot redefine or overload a built-in function");
        var info = new UserFunction(ResolveTypeRef(f.ReturnType), pars);   // id assigned when emitted
        if (!_userFunctions.TryGetValue(f.Name, out var list)) _userFunctions[f.Name] = list = [];
        list.Add(info);
        return info;
    }

    // glslang's mangled function name: name '(' then, per parameter, its type mangle and ';'.
    static string MangleFunction(string name, IReadOnlyList<FnParam> pars)
    {
        var sb = new System.Text.StringBuilder(name).Append('(');
        foreach (var p in pars) { MangleType(sb, p.Type); sb.Append(';'); }
        return sb.ToString();
    }

    // Mirrors glslang's TType::buildMangledName: an optional 'm'/'v' prefix, the basic type, then the
    // vector size (or the matrix's columns and rows) - which a struct also carries, as a trailing '1' -
    // and finally any array dimensions as "[N]".
    static void MangleType(System.Text.StringBuilder sb, GlslType t)
    {
        switch (t)
        {
            case MatrixType m: sb.Append('m').Append(ScalarMangle(m.Component)).Append((char)('0' + m.Columns)).Append((char)('0' + m.Rows)); break;
            case VectorType v: sb.Append('v').Append(ScalarMangle(v.Component)).Append((char)('0' + v.Size)); break;
            case ScalarType s: sb.Append(ScalarMangle(s.Kind)).Append('1'); break;
            case ArrayType a: MangleType(sb, a.Element); sb.Append('[').Append(a.Length).Append(']'); break;
            case StructType st:
                sb.Append("struct-").Append(st.Name);
                foreach (var fld in st.Fields) { sb.Append('-'); MangleType(sb, fld.Type); }
                sb.Append('1');   // a struct's vector size
                break;
            // An opaque type mangles as its sampled type, then its class, its arrayed/shadow flags, its
            // dimension, and the multisample flag - e.g. sampler2D is "s21".
            case SamplerType s:
                sb.Append(SamplerScalarMangle(s.Sampled)).Append('s');
                if (s.Arrayed) sb.Append('A');
                if (s.Shadow) sb.Append('S');
                sb.Append(DimMangle(s.Dim));
                if (s.Multisampled) sb.Append('M');
                sb.Append('1');
                break;
            case TextureType tx:
                sb.Append('t');
                if (tx.Arrayed) sb.Append('A');
                if (tx.Shadow) sb.Append('S');
                sb.Append(DimMangle(tx.Dim)).Append('1');
                break;
            case ImageType img:
                sb.Append(SamplerScalarMangle(img.Sampled)).Append('I');
                if (img.Arrayed) sb.Append('A');
                sb.Append(DimMangle(img.Dim)).Append('1');
                break;
            case PureSamplerType ps: sb.Append(ps.Shadow ? "pS1" : "p1"); break;   // glslang: pure, shadow flag, no dim
            case BufferReferenceType: sb.Append('1'); break;   // glslang has no letter for a reference, only the size
            default: throw new GlslParseException($"cannot mangle parameter type '{t}'");
        }
    }

    static string ScalarMangle(Scalar s) => s switch
    {
        Scalar.Float => "f", Scalar.Int => "i", Scalar.UInt => "u", Scalar.Bool => "b", Scalar.Half => "f16", Scalar.Double => "d", Scalar.Int64 => "i64", Scalar.UInt64 => "u64", _ => "f",
    };

    // An opaque type's sampled type prefixes its mangle; float is the default and contributes nothing.
    static string SamplerScalarMangle(Scalar s) => s switch
    {
        Scalar.Int => "i", Scalar.UInt => "u", Scalar.Half => "f16", Scalar.Int64 => "i64", Scalar.UInt64 => "u64", _ => "",
    };

    static string DimMangle(Dim d) => d switch
    {
        Dim.D1 => "1", Dim.D2 => "2", Dim.D3 => "3", Dim.Cube => "C",
        Dim.Rect => "R2", Dim.Buffer => "B", Dim.SubpassData => "P", _ => "",
    };

    // A function whose header ids (types, params, id, entry label) are allocated but whose body has
    // not yet been translated - glslang declares all functions before translating any body.
    sealed record DeclaredFn(FunctionDef Def, UserFunction Info, List<Instruction> Header, uint[] ParamIds, uint EntryLabel);

    // <paramref name="preLabel"/> is the entry label reserved up front for main (glslang allocates
    // main's id and entry label together in makeEntryPoint). Allocation order mirrors makeFunctionEntry:
    // return/param types, function type, the parameter ids, then the function id, then the entry label.
    DeclaredFn DeclareFunction(FunctionDef f, UserFunction info, uint? preLabel = null)
    {
        // glslang's makeFunctions converts the PARAMETER types before the return type, so a type first
        // named by a parameter is created first. A `const in` parameter is the value itself, not a pointer.
        uint[] paramPtrTypes = [.. info.Params.Select(p =>
        {
            // A reference parameter is a full use of the reference type (see DeclareLocal).
            if (ReferenceIn(p.Type) is string reference) BufferRefDefine(reference);
            return p.ByValue ? SpvType(p.Type) : _m.TypePointer(ParamStorage(p.Type), SpvType(p.Type));
        })];
        uint retType = SpvType(info.ReturnType);
        uint fnType = _m.TypeFunction(retType, paramPtrTypes);
        uint[] paramIds = [.. info.Params.Select(_ => _m.AllocId())];
        if (info.Id == 0)
        {
            info.Id = _m.AllocId();
            _m.AddName(info.Id, MangleFunction(f.Name, info.Params));
        }
        uint entryLabel = preLabel ?? _m.AllocId();

        // glslang names the parameters in makeFunctions (after the function name), during declaration.
        for (int i = 0; i < info.Params.Count; i++) _m.AddName(paramIds[i], info.Params[i].Name);
        for (int i = 0; i < info.Params.Count; i++)
            if (!info.Params[i].ByValue && ReferenceIn(info.Params[i].Type) is not null) _m.Decorate(paramIds[i], Decoration.AliasedPointer);

        var header = new List<Instruction> { InstrPool.Rent(Op.Function, retType, info.Id).AddEnum(FunctionControl.None).Add(fnType) };
        for (int i = 0; i < info.Params.Count; i++)
            header.Add(InstrPool.Rent(Op.FunctionParameter, paramPtrTypes[i], paramIds[i]));
        header.Add(InstrPool.Rent(Op.Label, resultId: entryLabel));
        return new DeclaredFn(f, info, header, paramIds, entryLabel);
    }

    // A module-scope initializer is not an OpVariable initializer: glslang turns every one of them into
    // a store at the top of the entry point, in source order, and does so BEFORE translating any
    // function body (makeGlobalInitializers). Even an unused global's initializer runs.
    // The module-scope declarators whose initializer runs at the top of main, in source order.
    static IEnumerable<(string Name, Expr Init)> InitializedGlobals(TranslationUnit unit) =>
        unit.Declarations.OfType<GlobalVar>()
            .Where(g => !IsConstOrInterface(g.Qualifiers))
            .SelectMany(g => g.Declarators)
            .Where(d => d.Init is not null)
            .Select(d => (d.Name, d.Init!));

    static IEnumerable<Expr> GlobalInitializers(TranslationUnit unit) =>
        InitializedGlobals(unit).Select(d => d.Init);

    void EmitGlobalInitializers(TranslationUnit unit)
    {
        _code.Clear();
        _vars.Clear();
        foreach (var (name, init) in InitializedGlobals(unit))
            if (GlobalType(name) is { } type)
                MultiTypeStore(BuildAccess(new Ident(name)), LowerCoerced(init, type, $"initializing '{name}'"));
        // An initializer that calls a function leaves its copy temps behind, and they belong to main.
        _globalInitCode = [.. _code];
        _globalInitVars = [.. _vars];
        _code.Clear();
        _vars.Clear();
    }

    // A const folds, and in/out/uniform/buffer/shared declarations cannot carry an initializer, so only
    // a plain Private global reaches the initializer pass.
    static bool IsConstOrInterface(Qualifiers q) =>
        q.Has("const") || q.Has("in") || q.Has("out") || q.Has("uniform") || q.Has("buffer") || q.Has("shared");

    List<Instruction> _globalInitCode = [];
    List<Instruction> _globalInitVars = [];

    List<Instruction> EmitBody(DeclaredFn d)
    {
        _locals.Clear();
        _pendingLocals.Clear();
        _pendingPrivates.Clear();
        _scopeNames.Clear();
        _scopeNames.Push([]);
        _valueParams.Clear();
        _outParams.Clear();
        _vars.Clear();
        _code.Clear();
        // The globals initialize at the top of main, before its body.
        if (d.Def.Name == "main") { _vars.AddRange(_globalInitVars); _code.AddRange(_globalInitCode); }
        _loops.Clear();
        _terminated = false;
        _currentReturnType = d.Info.ReturnType;
        _currentBlock = d.EntryLabel;

        for (int i = 0; i < d.Info.Params.Count; i++)
        {
            var p = d.Info.Params[i];
            // A `const in` parameter IS its value, so it binds directly; the rest are pointers to load.
            if (p.ByValue) _valueParams[p.Name] = new Value(d.ParamIds[i], p.Type, false);
            else _locals[p.Name] = (p.Type, d.ParamIds[i]);
            if (p.IsOut && !p.IsIn) _outParams.Add(p.Name);
            if (!string.IsNullOrEmpty(p.Name)) _scopeNames.Peek().Add(p.Name);
        }

        bool isVoid = d.Info.ReturnType is ScalarType { Kind: Scalar.Void };
        var prevBody = _currentBody;
        _currentBody = d.Def.Body;
        LowerStatements(d.Def.Body.Statements);
        _currentBody = prevBody;
        if (!_terminated)
            _code.Add(InstrPool.Rent(isVoid ? Op.Return : Op.Unreachable));
        // glslang's leaveFunction terminates whatever trailing block a non-void function is left on with
        // OpReturnValue(OpUndef). Post-processing rewrites the dead block to OpUnreachable, but the
        // OpUndef consumed an id either way.
        if (!isVoid) _m.AllocId();

        RewriteDeadMergesAndContinues(_code, d.EntryLabel);

        var body = RentBody();   // pooled across functions/compiles (returned once appended to the module)
        body.AddRange(d.Header);
        body.AddRange(_vars);
        body.AddRange(_code);
        body.Add(InstrPool.Rent(Op.FunctionEnd));
        return body;
    }


    const int MaxCfgDepth = 2000;

    // glslang's post-process pass (SpvPostProcess.cpp): a merge block or continue target that no reachable
    // block branches to is reached only by being NAMED in a merge instruction. glslang rewrites those into
    // their canonical forms - a dead merge keeps only its label plus OpUnreachable, a dead continue target
    // only its label plus a branch back to its header - which is how an unconditionally returning loop body
    // loses its increment.
    static void RewriteDeadMergesAndContinues(List<Instruction> code, uint entryLabel)
    {
        // The entry block's label is emitted with the function header, so seed it at -1: its body is
        // whatever precedes the first label in the code list.
        var blockStart = new Dictionary<uint, int> { [entryLabel] = -1 };
        for (int i = 0; i < code.Count; i++)
            if (code[i].Op == Op.Label) blockStart[code[i].ResultId] = i;
        if (blockStart.Count < 2) return;

        int End(int start)
        {
            int i = start + 1;
            while (i < code.Count && code[i].Op != Op.Label) i++;
            return i;
        }

        static IEnumerable<uint> SuccessorsOf(Instruction term) => term.Op switch
        {
            Op.Branch => [term.Operands[0]],
            Op.BranchConditional => [term.Operands[1], term.Operands[2]],
            // OpSwitch is [selector, default, (literal, label)...]; a 32-bit GLSL selector gives one word
            // per literal, so the case labels sit at 3, 5, 7...
            Op.Switch => Enumerable.Range(0, (term.Operands.Count - 2) / 2).Select(k => term.Operands[3 + k * 2]).Prepend(term.Operands[1]),
            _ => [],
        };

        // The merge instruction of a block, if it has one, plus the block's own control-flow successors.
        (Instruction? Merge, List<uint> Succ) Shape(uint label)
        {
            int start = blockStart[label], end = End(start);
            Instruction? merge = null;
            var succ = new List<uint>();
            for (int i = start + 1; i < end; i++)
            {
                if (code[i].Op is Op.LoopMerge or Op.SelectionMerge) merge = code[i];
                else if (code[i].Op is Op.Branch or Op.BranchConditional or Op.Switch) succ.AddRange(SuccessorsOf(code[i]));
            }
            return (merge, succ);
        }

        var reachable = new HashSet<uint>();
        var visited = new HashSet<uint>();
        var delayed = new HashSet<uint>();
        var deadMerges = new HashSet<uint>();
        var deadContinues = new Dictionary<uint, uint>();   // continue target -> its header
        int depth = 0;
        bool overflow = false;

        // Reason a block is reached: 0 = via control flow, 1 = only as a continue target, 2 = only as a merge.
        void Visit(uint label, int why, uint header)
        {
            if (why == 0) reachable.Add(label);
            if (visited.Contains(label) || delayed.Contains(label)) return;
            if (++depth > MaxCfgDepth) { overflow = true; depth--; return; }
            if (why == 1) deadContinues[label] = header;
            else if (why == 2) deadMerges.Add(label);
            visited.Add(label);

            var (merge, succ) = Shape(label);
            uint mergeL = 0, contL = 0;
            if (merge is not null)
            {
                mergeL = merge.Operands[0];
                delayed.Add(mergeL);
                if (merge.Op == Op.LoopMerge) { contL = merge.Operands[1]; delayed.Add(contL); }
            }
            if (why == 0)
                foreach (uint sc in succ) if (blockStart.ContainsKey(sc)) Visit(sc, 0, 0);
            if (contL != 0 && blockStart.ContainsKey(contL))
            {
                bool live = reachable.Contains(contL);
                delayed.Remove(contL);
                Visit(contL, live ? 0 : 1, label);
            }
            if (mergeL != 0 && blockStart.ContainsKey(mergeL))
            {
                bool live = reachable.Contains(mergeL);
                delayed.Remove(mergeL);
                Visit(mergeL, live ? 0 : 2, label);
            }
            depth--;
        }

        Visit(entryLabel, 0, 0);
        // A control-flow graph deeper than the recursion budget is left as emitted: still valid SPIR-V,
        // just not rewritten to glslang's canonical dead-block form.
        if (overflow) return;
        if (deadMerges.Count == 0 && deadContinues.Count == 0) return;

        // Rewrite back-to-front so the recorded block starts stay valid.
        foreach (uint label in deadMerges.Concat(deadContinues.Keys).Where(l => blockStart[l] >= 0).OrderByDescending(l => blockStart[l]))
        {
            int start = blockStart[label], end = End(start);
            code.RemoveRange(start + 1, end - start - 1);
            code.Insert(start + 1, deadContinues.TryGetValue(label, out uint header)
                ? InstrPool.Rent(Op.Branch).Add(header)
                : InstrPool.Rent(Op.Unreachable));
        }
    }

    // A per-thread pool of function-body assembly lists, reused across functions and compiles.
    [ThreadStatic] static Stack<List<Instruction>>? _bodyPool;
    static List<Instruction> RentBody() { var p = _bodyPool ??= new(); return p.Count > 0 ? p.Pop() : []; }
    static void ReturnBody(List<Instruction> b) { b.Clear(); (_bodyPool ??= new()).Push(b); }

    // Records an interface variable's location/component footprint and rejects any overlap with one already
    // placed in the same direction and index (glslang's "overlapping use of location" error). Only 32-bit
    // scalars/vectors can be component-packed, so wider or aggregate types conservatively fill their rows.
    void ReserveLocations(StorageClass storage, Qualifiers quals, int location, GlslType type, string name)
    {
        int index = LayoutInt(quals, "index") ?? 0;
        int component = LayoutInt(quals, "component") ?? 0;
        int span = LocationSpan(type);
        int compLo = component, compHi = component + LocationComponents(type);
        var key = (storage, index);
        if (!_locationUse.TryGetValue(key, out var list)) _locationUse[key] = list = [];
        foreach (var u in list)
            if (location < u.Hi && u.Lo < location + span && compLo < u.CompHi && u.CompLo < compHi)
                throw new GlslParseException($"'{name}': layout location {location} overlaps another {(storage == StorageClass.Output ? "output" : "input")}");
        list.Add((location, location + span, compLo, compHi));
    }

    // The number of location rows a type occupies (glslang's getTypeLocationSize): a matrix is its columns,
    // an array multiplies by its length, a struct sums its members, and a 64-bit vec3/vec4 takes two rows.
    static int LocationSpan(GlslType t) => t switch
    {
        ArrayType a => System.Math.Max(1, a.Length) * LocationSpan(a.Element),
        MatrixType m => m.Columns * RowSpan(m.Rows, m.Component),
        StructType s => s.Fields.Sum(f => LocationSpan(f.Type)),
        VectorType v => RowSpan(v.Size, v.Component),
        _ => 1,
    };

    static int RowSpan(int components, Scalar c) => Wide64(c) && components > 2 ? 2 : 1;

    // glslang computeTypeLocationSize with the vertex-input rule: a vertex shader input scalar/vector always
    // consumes ONE location (dvec3/dvec4 do NOT get two there), unlike every other in/out.
    static int LocationSpanVI(GlslType t, bool vertexInput) => t switch
    {
        ArrayType a => System.Math.Max(1, a.Length) * LocationSpanVI(a.Element, vertexInput),
        MatrixType m => m.Columns * (vertexInput ? 1 : RowSpan(m.Rows, m.Component)),
        StructType s => s.Fields.Sum(f => LocationSpanVI(f.Type, vertexInput)),
        VectorType v => vertexInput ? 1 : RowSpan(v.Size, v.Component),
        _ => 1,
    };

    // Lowest free location slot for an auto-assigned in/out (mirrors glslang's getFreeSlot from 0): the first
    // location where the type's whole span fits without overlapping an already-reserved range.
    int FreeLocation(StorageClass storage, Qualifiers quals, GlslType type, bool vertexInput)
    {
        int index = LayoutInt(quals, "index") ?? 0;
        int component = LayoutInt(quals, "component") ?? 0;
        int span = LocationSpanVI(type, vertexInput);
        int compLo = component, compHi = component + LocationComponents(type);
        _locationUse.TryGetValue((storage, index), out var list);
        for (int loc = 0; ; loc++)
        {
            bool overlaps = false;
            if (list is not null)
                foreach (var u in list)
                    if (loc < u.Hi && u.Lo < loc + span && compLo < u.CompHi && u.CompLo < compHi) { overlaps = true; break; }
            if (!overlaps) return loc;
        }
    }

    // Components consumed within a single location row (each 64-bit component takes two of the four slots).
    static int LocationComponents(GlslType t) => t switch
    {
        ScalarType s when !Wide64(s.Kind) => 1,
        VectorType v when !Wide64(v.Component) => v.Size,
        ScalarType => 2,
        VectorType v => System.Math.Min(4, v.Size * 2),
        _ => 4,
    };

    static bool Wide64(Scalar s) => s is Scalar.Double or Scalar.Int64 or Scalar.UInt64;

    // Materializes a global's SPIR-V on first real use (emitting path); returns null if unknown.
    Global? ResolveGlobal(string name)
    {
        if (_globals.TryGetValue(name, out var g)) return g;
        if (_pendingGlobals.TryGetValue(name, out var pending))
        {
            _pendingGlobals.Remove(name);
            return _globals[name] = pending.Build();
        }
        return null;
    }

    bool GlobalExists(string name) => _globals.ContainsKey(name) || _pendingGlobals.ContainsKey(name);

    // The declared type of a global WITHOUT materializing it (used by non-emitting type inference).
    GlslType? GlobalType(string name) =>
        _globals.TryGetValue(name, out var g) ? g.Type :
        _pendingGlobals.TryGetValue(name, out var p) ? p.Type : null;

    // A qualifier applied to an already-declared name, e.g. `invariant gl_Position;`. Only the built-in
    // gl_PerVertex members can be re-qualified this way; a user variable must carry its qualifiers on
    // its own declaration, so anything else throws rather than being silently ignored.
    void ApplyQualifierRedecl(QualifierRedecl redecl)
    {
        // `layout(buffer_reference) buffer Node;` forward-declares a reference type so a block can point at
        // itself. The full block declaration registers the type (RegisterBufferReferences); nothing to emit.
        if (redecl.Qualifiers.Has("buffer") && redecl.Qualifiers.Layout.Any(i => i.Name == "buffer_reference")) return;
        if (!redecl.Qualifiers.Has("invariant"))
            throw new GlslParseException($"re-declaring '{redecl.Name}' with these qualifiers is not supported");
        if (redecl.Name == "gl_Position") _invariantPosition = true;
        else _invariantVars.Add(redecl.Name);   // `invariant <userOutput>;` decorates that output Invariant
    }

    void EmitGlobal(GlobalVar decl)
    {
        if (Types.Resolve(decl.Type.Name) is ScalarType { Kind: Scalar.Void })
            throw new GlslParseException("a variable cannot have type 'void'");
        foreach (var d in decl.Declarators)
        {
            RejectReservedName(d.Name);
            if (!_globalDeclNames.Add(d.Name))
                throw new GlslParseException($"'{d.Name}' : redefinition");
        }
        // in/out/uniform/buffer/shared variables are interface- or resource-bound and cannot be initialized.
        if ((decl.Qualifiers.Has("in") || decl.Qualifiers.Has("out") || decl.Qualifiers.Has("uniform")
                || decl.Qualifiers.Has("buffer") || decl.Qualifiers.Has("shared")) && decl.Declarators.Any(d => d.Init is not null))
            throw new GlslParseException("cannot initialize this type of qualifier");
        if (decl.Qualifiers.Has("const"))
        {
            var baseType = ResolveTypeRef(decl.Type);
            int? specId = LayoutInt(decl.Qualifiers, "constant_id");
            foreach (var d in decl.Declarators)
            {
                if (specId is int id)
                {
                    // glslang emits the OpSpecConstant lazily (at first reference), like an ordinary const.
                    _pendingSpecConstants[d.Name] = (baseType, d.Init!, id);
                    _specConstNames.Add(d.Name);
                    continue;
                }
                // A const built from a specialization constant keeps its specialization-ness instead of
                // folding, so record the expression and build its OpSpecConstantOp chain on first use.
                if (d.Init is not null && UsesSpecConstant(d.Init))
                {
                    _pendingSpecExprs[d.Name] = d.Init;
                    _specConstNames.Add(d.Name);
                    continue;
                }
                // A const initializer must be a compile-time constant expression: fold it fully to a
                // single OpConstant / OpConstantComposite (scalar arithmetic, constructors, swizzles,
                // indexing, and const-of-const references all included).
                var type = ApplyArrayDims(baseType, d.Array);
                var cval = EvalConst(ResolveInitializer(d.Init!, type))
                    ?? throw new GlslParseException($"const initializer for '{d.Name}' must be a constant expression");
            // GLSL implicit sizing for an unsized local with NO initializer (an initialized one is already
            // sized from it): the size is the largest constant index used on the variable, plus one. A
            // runtime array cannot be instantiated as an OpVariable, so leaving it unsized is invalid.
            if (type is RuntimeArrayType lra && d.Init is null && _currentBody is not null
                && MaxConstIndexOf(_currentBody, d.Name) is int mx)
                type = SizedArray(lra.Element, mx + 1);
                if (type is RuntimeArrayType && cval is CAgg { Type: ArrayType inferred })
                    type = inferred;   // `const T name[] = T[](...)`: size comes from the initializer
                _constValues[d.Name] = cval;
                // A const array stays a pure OpConstantComposite (no variable); a dynamic index into it
                // copies the constant into a Function-local "indexable" temp at the access site, exactly
                // like glslang. A constant index folds via _constValues. A scalar/vector/matrix becomes an
                // OpConstant, but glslang emits it only when first referenced, so it is materialized lazily.
            }
            return;
        }

        if (decl.Qualifiers.Has("taskPayloadSharedEXT"))
        {
            var baseType = ResolveTypeRef(decl.Type);
            foreach (var d in decl.Declarators)
            {
                var type = ApplyArrayDims(baseType, d.Array);
            // GLSL implicit sizing for an unsized local with NO initializer (an initialized one is already
            // sized from it): the size is the largest constant index used on the variable, plus one. A
            // runtime array cannot be instantiated as an OpVariable, so leaving it unsized is invalid.
            if (type is RuntimeArrayType lra && d.Init is null && _currentBody is not null
                && MaxConstIndexOf(_currentBody, d.Name) is int mx)
                type = SizedArray(lra.Element, mx + 1);
                string name = d.Name;
                _taskPayloadName = name;
                _pendingGlobals[name] = (type, () =>
                {
                    uint ptr = _m.TypePointer(StorageClass.TaskPayloadWorkgroupEXT, SpvType(type));
                    uint var = _m.GlobalVariable(ptr, StorageClass.TaskPayloadWorkgroupEXT);
                    _m.AddName(var, name);
                    return new Global(type, StorageClass.TaskPayloadWorkgroupEXT, var);
                });
            }
            return;
        }

        if (decl.Qualifiers.Has("shared"))
        {
            // Workgroup storage only exists in the stages that have work groups; elsewhere the module is
            // invalid ("Workgroup Storage Class is limited to MeshEXT, TaskEXT, MeshNV, TaskNV, GLCompute").
            if (stage is not (ShaderStage.Compute or ShaderStage.Mesh or ShaderStage.Task))
                throw new GlslParseException("'shared' is only available in a compute, task, or mesh shader");
            var baseType = ResolveTypeRef(decl.Type);
            foreach (var d in decl.Declarators)
            {
                var type = ApplyArrayDims(baseType, d.Array);
            // GLSL implicit sizing for an unsized local with NO initializer (an initialized one is already
            // sized from it): the size is the largest constant index used on the variable, plus one. A
            // runtime array cannot be instantiated as an OpVariable, so leaving it unsized is invalid.
            if (type is RuntimeArrayType lra && d.Init is null && _currentBody is not null
                && MaxConstIndexOf(_currentBody, d.Name) is int mx)
                type = SizedArray(lra.Element, mx + 1);
                string name = d.Name;
                _pendingGlobals[name] = (type, () =>
                {
                    uint ptr = _m.TypePointer(StorageClass.Workgroup, SpvType(type));
                    uint var = _m.GlobalVariable(ptr, StorageClass.Workgroup);
                    _m.AddName(var, name);
                    // Under the Vulkan memory model shared memory is implicitly workgroup-coherent.
                    return new Global(type, StorageClass.Workgroup, var, _vmmPragma ? MemQual.Shared : MemQual.None);
                });
            }
            return;
        }

        if (decl.Qualifiers.Has("uniform") && IsOpaque(ResolveTypeRef(decl.Type)))
        {
            var baseType = ResolveTypeRef(decl.Type) is ImageType img ? img with { Format = ResolveImageFormat(decl.Qualifiers) } : ResolveTypeRef(decl.Type);
            var quals = decl.Qualifiers;
            foreach (var d in decl.Declarators)
            {
                var type = ApplyArrayDims(baseType, d.Array);
            // GLSL implicit sizing for an unsized local with NO initializer (an initialized one is already
            // sized from it): the size is the largest constant index used on the variable, plus one. A
            // runtime array cannot be instantiated as an OpVariable, so leaving it unsized is invalid.
            if (type is RuntimeArrayType lra && d.Init is null && _currentBody is not null
                && MaxConstIndexOf(_currentBody, d.Name) is int mx)
                type = SizedArray(lra.Element, mx + 1);
                string name = d.Name;
                // A descriptor with no binding cannot be bound; glslang errors rather than defaulting it.
                if (LayoutInt(quals, "binding") is null)
                    throw new GlslParseException($"'binding' : sampler/texture/image requires layout(binding=X)");
                // A storage image must state its format unless it is writeonly.
                if (baseType is ImageType { Format: ImageFormat.Unknown, Dim: not Dim.SubpassData } && !quals.Has("writeonly"))
                    throw new GlslParseException("image variables not declared 'writeonly' and without a format layout qualifier");
                _pendingGlobals[name] = (type, () =>
                {
                    if (type is RuntimeArrayType) DeclareRuntimeDescriptorArray();
                    uint ptr = _m.TypePointer(StorageClass.UniformConstant, SpvType(type));
                    uint var = _m.GlobalVariable(ptr, StorageClass.UniformConstant);
                    _m.AddName(var, name);
                    _m.Decorate(var, Decoration.DescriptorSet, (uint)(LayoutInt(quals, "set") ?? 0));
                    _m.Decorate(var, Decoration.Binding, (uint)(LayoutInt(quals, "binding") ?? 0));
                    if (baseType is ImageType && quals.Has("writeonly")) { _m.Decorate(var, Decoration.NonReadable); _writeOnlyVars.Add(var); }
                    if (baseType is ImageType && quals.Has("readonly")) { _m.Decorate(var, Decoration.NonWritable); _readOnlyVars.Add(var); }
                    if (baseType is not ImageType { Dim: Dim.SubpassData } && LayoutInt(quals, "input_attachment_index") is not null)
                        throw new GlslParseException("'input_attachment_index' is only valid on a subpass input", decl.Line);
                    if (baseType is ImageType { Dim: Dim.SubpassData } && LayoutInt(quals, "input_attachment_index") is int ai)
                        _m.Decorate(var, Decoration.InputAttachmentIndex, (uint)ai);
                    // Under the Vulkan memory model coherent/volatile are not decorations (spirv-val bans them)
                    // but per-access texel operands; see ImageMemOperands.
                    foreach (var (word, decoration) in MemoryQualifiers)
                        if (quals.Has(word) && !(_vmmPragma && decoration is Decoration.Coherent or Decoration.Volatile))
                            _m.Decorate(var, decoration);
                    // ES samplers carry a precision: mediump/lowp declared, or the lowp default of
                    // sampler2D/samplerCube. A relaxed sampler relaxes its loads and its sampling results.
                    if (SamplerRelaxed(baseType, quals)) Relax(var);
                    return new Global(type, StorageClass.UniformConstant, var, _vmmPragma && baseType is ImageType ? BlockMemQual(quals) : MemQual.None);
                });
            }
            return;
        }

        // Ray-tracing payload/attribute/callable-data globals: a variable in a dedicated storage class with no
        // descriptor. Under GL_EXT_ray_tracing the layout(location=) is a purely logical index (traceRayEXT names
        // it) and carries no decoration; GL_NV_ray_tracing instead decorates the variable with that Location.
        if (RayTracingStorage(decl.Qualifiers) is StorageClass rtStorage)
        {
            var rtBase = ResolveTypeRef(decl.Type);
            int? locLayout = LayoutInt(decl.Qualifiers, "location");
            foreach (var d in decl.Declarators)
            {
                var type = ApplyArrayDims(rtBase, d.Array);
                string name = d.Name;
                if (locLayout is int loc) _rtByLoc[(rtStorage, loc)] = name;
                _pendingGlobals[name] = (type, () =>
                {
                    uint ptr = _m.TypePointer(rtStorage, SpvType(type));
                    uint var = _m.GlobalVariable(ptr, rtStorage);
                    _m.AddName(var, name);
                    if (_nvRayTracing && locLayout is int l) _m.Decorate(var, Decoration.Location, (uint)l);
                    _interface.Add(var);
                    return new Global(type, rtStorage, var);
                });
            }
            return;
        }

        if (decl.Qualifiers.Has("in") && decl.Qualifiers.Has("out"))
            throw new GlslParseException("a variable cannot be both 'in' and 'out'");
        StorageClass? storage =
            decl.Qualifiers.Has("out") ? StorageClass.Output :
            decl.Qualifiers.Has("in") ? StorageClass.Input :
            null;
        if (storage is null)
        {
            if (decl.Qualifiers.Has("uniform"))
                throw new GlslParseException($"a non-opaque uniform must be declared inside a uniform block in Vulkan GLSL");
            // A plain module-scope global (no in/out/uniform/const/...) is a Private-storage variable.
            var globalBase = ResolveTypeRef(decl.Type);
            if (IsOpaque(globalBase))
                throw new GlslParseException($"'{decl.Type.Name}' is opaque and must be declared 'uniform'");
            foreach (var d in decl.Declarators)
            {
                var t = ApplyArrayDims(globalBase, d.Array);
                string name = d.Name;
                _pendingGlobals[name] = (t, () =>
                {
                    uint pointer = _m.TypePointer(StorageClass.Private, SpvType(t));
                    // Any initializer is a store at the top of main, not an OpVariable initializer.
                    uint variable = _m.GlobalVariable(pointer, StorageClass.Private);
                    _m.AddName(variable, name);
                    return new Global(t, StorageClass.Private, variable);
                });
            }
            return;
        }

        foreach (var d in decl.Declarators)
        {
            // A gl_-prefixed name is a reserved built-in; `out float gl_FragDepth;` (optionally with a
            // conservative-depth layout) and similar redeclarations are handled by the built-in access
            // paths, so we only honor any execution-mode qualifier here rather than emit a user variable.
            if (d.Name.StartsWith("gl_", StringComparison.Ordinal))
            {
                _depthCondition = DepthConditionOf(decl.Qualifiers) ?? _depthCondition;
                continue;
            }
            _varyingNames.Add(d.Name);
            // Geometry/tessellation/mesh per-vertex I/O is declared unsized ("[]"); size it to the
            // stage's vertex count. A mesh perprimitiveEXT output is per primitive, so it sizes to
            // max_primitives instead.
            var type = ApplyArrayDims(ResolveTypeRef(decl.Type), d.Array);
            // A per-vertex/per-patch interface is declared unsized ("[]"); its outer dimension is the
            // vertex index, so it sizes to the stage's vertex count and does NOT consume extra locations.
            bool perVertexArray = type is RuntimeArrayType;
            // A per-vertex input in a stage that receives a primitive is indexed by vertex, so it must be
            // declared as an array; a scalar declaration is a source error, not something to auto-array.
            if (storage == StorageClass.Input && type is not (RuntimeArrayType or ArrayType)
                && !decl.Qualifiers.Has("patch")   // a patch input is per-patch, so it is not arrayed
                && stage is ShaderStage.Geometry or ShaderStage.TessControl or ShaderStage.TessEvaluation)
                throw new GlslParseException($"per-vertex input '{d.Name}' must be declared as an array in this stage");
            if (type is RuntimeArrayType rv)
            {
                int count = decl.Qualifiers.Has("perprimitiveEXT") ? _meshMaxPrimitives
                          : storage == StorageClass.Input ? _inVertexCount : _outVertexCount;
                type = new ArrayType(rv.Element, count);
            }
            // An integer fragment input cannot be interpolated and must be declared 'flat' (VUID-04744);
            // omitting it is invalid GLSL, so throw rather than emit an invalid module.
            if (stage == ShaderStage.Fragment && storage == StorageClass.Input && !decl.Qualifiers.Has("flat")
                && BaseScalar(type) is Scalar.Int or Scalar.UInt)
                throw new GlslParseException($"integer fragment input '{d.Name}' must be declared 'flat'");
            // Interpolation is a property of a fragment INPUT; the same qualifier on an output produces a
            // Centroid/Sample-decorated Output variable, which is invalid.
            if (stage == ShaderStage.Fragment && storage == StorageClass.Output)
                foreach (string w in new[] { "centroid", "sample", "flat", "noperspective", "smooth" })
                    if (decl.Qualifiers.Has(w))
                        throw new GlslParseException($"'{w}' cannot be used on a fragment shader output");
            GlslType locType = perVertexArray ? ((ArrayType)type).Element : type;
            if (LayoutInt(decl.Qualifiers, "location") is not int location)
            {
                if (!AutoMapLocations)
                    throw new GlslParseException($"'{d.Name}' : SPIR-V requires location for user input/output", decl.Line);
                location = FreeLocation(storage.Value, decl.Qualifiers, locType, storage == StorageClass.Input && stage == ShaderStage.Vertex);
            }
            ReserveLocations(storage.Value, decl.Qualifiers, location, locType, d.Name);
            var storageValue = storage.Value;
            var quals = decl.Qualifiers;
            string name = d.Name;
            if (quals.Has("precise")) _preciseVars.Add(name);
            _pendingGlobals[name] = (type, () =>
            {
                uint ptr = _m.TypePointer(storageValue, SpvType(type));
                uint var = _m.GlobalVariable(ptr, storageValue);
                _m.AddName(var, name);
                // glslang emits interpolation/qualifier decorations before Location, but Invariant after it.
                ApplyVariableQualifiers(var, quals);
                if (!IsRelaxed(quals) && !quals.Has("highp") && RelaxByDefault(type)) Relax(var);
                // glslang emits Component and Index before Location.
                if (LayoutInt(quals, "component") is int comp) _m.Decorate(var, Decoration.Component, (uint)comp);
                if (LayoutInt(quals, "index") is int idx) _m.Decorate(var, Decoration.Index, (uint)idx);
                _m.Decorate(var, Decoration.Location, (uint)location);
                DecorateXfb(var, quals);
                if (LayoutInt(quals, "stream") is int strm) { _m.AddCapability(Capability.GeometryStreams); _m.Decorate(var, Decoration.Stream, (uint)strm); }
                if (quals.Has("pervertexEXT"))
                {
                    _m.AddCapability(Capability.FragmentBarycentricKHR);
                    _m.AddExtension("SPV_KHR_fragment_shader_barycentric");
                    _m.Decorate(var, Decoration.PerVertexKHR);
                }
                if (quals.Has("invariant") || _invariantVars.Contains(name)) _m.Decorate(var, Decoration.Invariant);
                _interface.Add(var);
                return new Global(type, storageValue, var);
            });
        }
    }

    // Applies interpolation / per-patch / precision qualifiers to an interface variable. 'smooth' is the
    // default and carries no decoration; 'highp' is the default precision and likewise emits nothing,
    // while mediump/lowp become RelaxedPrecision. Anything we cannot represent correctly throws rather
    // than being silently dropped.
    void ApplyVariableQualifiers(uint var, Qualifiers q)
    {
        if (IsRelaxed(q)) Relax(var);
        if (q.Has("perprimitiveEXT")) _m.Decorate(var, Decoration.PerPrimitiveEXT);
        if (q.Has("flat")) _m.Decorate(var, Decoration.Flat);
        if (q.Has("noperspective")) _m.Decorate(var, Decoration.NoPerspective);
        if (q.Has("centroid")) _m.Decorate(var, Decoration.Centroid);
        if (q.Has("sample"))
        {
            // Per-sample interpolation requires the SampleRateShading capability (the Sample decoration
            // is only defined with it).
            _m.AddCapability(Capability.SampleRateShading);
            _m.Decorate(var, Decoration.Sample);
        }
        if (q.Has("patch")) _m.Decorate(var, Decoration.Patch);
        // `precise` on an interface variable does not decorate it; its writes get NoContraction instead.
        if (q.Has("coherent") || q.Has("volatile") || q.Has("restrict"))
            throw new GlslParseException("memory qualifiers (coherent/volatile/restrict) are only valid on buffers and images");
    }

    // GLSL memory qualifiers and their SPIR-V decorations (valid on buffer blocks and images).
    static readonly (string Word, Decoration Decoration)[] MemoryQualifiers =
    [
        ("coherent", Decoration.Coherent),
        ("volatile", Decoration.Volatile),
        ("volatile", Decoration.Coherent),   // glslang emits both Volatile and Coherent for `volatile`
        ("restrict", Decoration.Restrict),
    ];

    // A `layout(constant_id = N) const` becomes an OpSpecConstant decorated with SpecId N: a
    // specializable default. Treated as a runtime value (IsConstant=false) so it never folds into
    // an ordinary OpConstantComposite, which may not contain specialization constants.
    // True when every leaf is a constant or a specialization constant, so the whole operation can stay in
    // the constant world as OpSpecConstantOp instead of becoming runtime code.
    bool IsSpecConstExpr(Expr e) => e switch
    {
        Ident id => _specConstNames.Contains(id.Name) || EvalConst(e) is not null,
        Literal => true,
        Unary u => u.Op is "-" or "~" or "!" && IsSpecConstExpr(u.Operand) && !IsFloating(BaseScalar(InferType(u.Operand))),
        // A binary touching the floating domain is not expressible in OpSpecConstantOp (it would need
        // the Kernel capability), so glslang lowers it as ordinary runtime code.
        Binary b => b.Op is not ("=" or ",") && !IsCompoundAssign(b.Op)
            && !IsFloating(BaseScalar(InferType(b.Left))) && !IsFloating(BaseScalar(InferType(b.Right)))
            && IsSpecConstExpr(b.Left) && IsSpecConstExpr(b.Right),
        // A scalar conversion of a specialization constant stays specializable only when
        // OpSpecConstantOp can express it; crossing into the floating domain is ordinary runtime code.
        Call c => c.Args.Count == 1 && !c.IsArray && Types.Resolve(c.Name) is ScalarType t
            && IsSpecConstExpr(c.Args[0]) && IsSpecConversion(BaseScalar(InferType(c.Args[0])), t.Kind),
        // A ternary becomes OpSpecConstantOp OpSelect, which (unlike the arithmetic ops) is allowed on
        // floating-point operands too, so there is no floating-domain restriction here.
        Conditional c => IsSpecConstExpr(c.Cond) && IsSpecConstExpr(c.Then) && IsSpecConstExpr(c.Else),
        _ => false,
    };

    static bool IsSpecConversion(Scalar from, Scalar to) =>
        (IsFloating(from) && IsFloating(to)) || (!IsFloating(from) && !IsFloating(to));

    // True when an expression reads a specialization constant, so it cannot fold to a plain value.
    // A shader without any specialization constants can never use one, so the recursive scan (called at
    // every operator node during lowering, otherwise O(n^2)) short-circuits to false.
    bool UsesSpecConstant(Expr e) => _specConstNames.Count > 0 && UsesSpecConstantCore(e);
    bool UsesSpecConstantCore(Expr e) => e switch
    {
        Ident id => _specConstNames.Contains(id.Name),
        Unary u => UsesSpecConstantCore(u.Operand),
        Binary b => UsesSpecConstantCore(b.Left) || UsesSpecConstantCore(b.Right),
        Conditional t => UsesSpecConstantCore(t.Cond) || UsesSpecConstantCore(t.Then) || UsesSpecConstantCore(t.Else),
        Call c => c.Args.Any(UsesSpecConstantCore),
        Member m => UsesSpecConstantCore(m.Target),
        Index ix => UsesSpecConstantCore(ix.Target) || UsesSpecConstantCore(ix.Idx),
        _ => false,
    };

    // Builds a specialization-constant expression: the leaves are specialization constants or ordinary
    // constants, and each operation becomes an OpSpecConstantOp so the whole stays specializable.
    Value SpecConstantExpr(Expr e)
    {
        switch (e)
        {
            case Ident id when _pendingSpecConstants.TryGetValue(id.Name, out var sc):
                return MaterializeSpecConstant(id.Name, sc);
            case Ident id when _pendingSpecExprs.TryGetValue(id.Name, out var pending):
                _pendingSpecExprs.Remove(id.Name);
                var built = SpecConstantExpr(pending);
                _m.AddName(built.Id, id.Name);   // glslang names a derived specialization constant too
                return _constants[id.Name] = built;
            case Ident id when _constants.TryGetValue(id.Name, out var known):
                return known;
            case Unary u when u.Op is "-" or "~" or "!":
            {
                var operand = SpecConstantExpr(u.Operand);
                var op = u.Op switch
                {
                    "-" => IsFloating(BaseScalar(operand.Type)) ? Op.FNegate : Op.SNegate,
                    "~" => Op.Not,
                    _ => Op.LogicalNot,
                };
                return TrackSpec(_m.SpecConstantOp(SpvType(operand.Type), op, operand.Id), operand.Type);
            }
            case Binary b when b.Op is not ("=" or ","):
            {
                var l = SpecConstantExpr(b.Left);
                var r = SpecConstantExpr(b.Right);
                var common = CommonType(l.Type, r.Type);
                var (opcode, type) = SpecBinaryOp(b.Op, common);
                return TrackSpec(_m.SpecConstantOp(SpvType(type), opcode, l.Id, r.Id), type);
            }
            case Call c when Types.Resolve(c.Name) is ScalarType target:
                return SpecConstantConvert(SpecConstantExpr(c.Args[0]), target);
            case Conditional t:
            {
                var cond = SpecConstantExpr(t.Cond);
                var thenV = SpecConstantExpr(t.Then);
                var elseV = SpecConstantExpr(t.Else);
                var type = CommonType(thenV.Type, elseV.Type);
                return TrackSpec(_m.SpecConstantOp(SpvType(type), Op.Select, cond.Id, thenV.Id, elseV.Id), type);
            }
            default:
                // A leaf with no specialization constant in it is an ordinary folded constant.
                return EvalConst(e) is { } folded
                    ? Materialize(folded)
                    : throw new GlslParseException("a specialization-constant expression may only combine constants");
        }
    }

    Value TrackSpec(uint id, GlslType type) { _specConstIds.Add(id); return new Value(id, type, IsConstant: false); }

    // Converting a specialization constant has to stay specializable. A signedness-only change cannot
    // use OpBitcast inside OpSpecConstantOp, so glslang adds an unsigned zero instead; a real numeric
    // change is the ordinary conversion opcode.
    Value SpecConstantConvert(Value operand, ScalarType target)
    {
        var from = BaseScalar(operand.Type);
        if (from == target.Kind) return operand;
        // An integer to bool compares against an UNSIGNED zero whatever the source's signedness.
        if (target.Kind == Scalar.Bool && IsIntegral(from))
        {
            uint zero = Is64Bit(from) ? _m.ConstantInt64(SpvType(Types.UInt64), 0) : _m.ConstantInt(SpvType(Types.UInt), 0);
            return TrackSpec(_m.SpecConstantOp(SpvType(target), Op.INotEqual, operand.Id, zero), target);
        }
        // A bool to integer selects between one and zero of the target's own signedness.
        if (from == Scalar.Bool && IsIntegral(target.Kind))
        {
            uint zero = Materialize(new CScalar(target.Kind, 0, 0)).Id;
            uint one = Materialize(new CScalar(target.Kind, 1, 1)).Id;
            return TrackSpec(_m.SpecConstantOp(SpvType(target), Op.Select, operand.Id, one, zero), target);
        }
        if (IsIntegral(from) && IsIntegral(target.Kind) && Is64Bit(from) == Is64Bit(target.Kind))
        {
            uint zero = Is64Bit(target.Kind)
                ? _m.ConstantInt64(SpvType(Types.UInt64), 0)
                : _m.ConstantInt(SpvType(Types.UInt), 0);
            return TrackSpec(_m.SpecConstantOp(SpvType(target), Op.IAdd, operand.Id, zero), target);
        }
        if (ConvertOp(from, target.Kind) is not { } op)
            throw new GlslParseException($"cannot convert a specialization constant from '{Types.ScalarName(from)}' to '{Types.ScalarName(target.Kind)}'");
        return TrackSpec(_m.SpecConstantOp(SpvType(target), op, operand.Id), target);
    }

    static (Op Opcode, GlslType Type) SpecBinaryOp(string op, GlslType operandType)
    {
        if (IsComparison(op)) return (CompareOp(op, BaseScalar(operandType)), Types.Bool);
        if (op is "&&") return (Op.LogicalAnd, Types.Bool);
        if (op is "||") return (Op.LogicalOr, Types.Bool);
        if (op is "^^") return (Op.LogicalNotEqual, Types.Bool);
        if (op is "&" or "|" or "^")
            return (op switch { "&" => Op.BitwiseAnd, "|" => Op.BitwiseOr, _ => Op.BitwiseXor }, operandType);
        if (op is "<<") return (Op.ShiftLeftLogical, operandType);
        if (op is ">>") return (IsSigned(BaseScalar(operandType)) ? Op.ShiftRightArithmetic : Op.ShiftRightLogical, operandType);
        return (ArithOp(op, operandType), operandType);
    }

    // Materializes a deferred specialization constant, names it (glslang emits an OpName), and caches it.
    Value MaterializeSpecConstant(string name, (GlslType Type, Expr Init, int SpecId) sc)
    {
        _pendingSpecConstants.Remove(name);
        var value = SpecConstant(sc.Type, sc.Init, sc.SpecId);
        _m.AddName(value.Id, name);
        return _constants[name] = value;
    }

    Value SpecConstant(GlslType type, Expr init, int specId)
    {
        if (type is not ScalarType scalar)
            throw new GlslParseException("a specialization constant (constant_id) must be a scalar", init.Line);

        var (valueExpr, negate) = init is Unary { Op: "-", Operand: Literal } u ? (u.Operand, true) : (init, false);
        if (valueExpr is not Literal lit)
            throw new GlslParseException("a specialization constant needs a literal default value");

        uint typeId = SpvType(scalar);
        uint SignedBits() => negate ? unchecked((uint)-(int)ParseIntLiteral(lit.Text)) : ParseIntLiteral(lit.Text);
        ulong SignedBits64() => negate ? unchecked((ulong)-(long)ParseInt64Literal(lit.Text)) : ParseInt64Literal(lit.Text);
        uint id = scalar.Kind switch
        {
            Scalar.Bool => _m.SpecConstantBool(typeId, lit.Text == "true"),
            Scalar.Float => _m.SpecConstantFloat(typeId, (negate ? -1f : 1f) * ParseFloat(lit.Text)),
            Scalar.Double => _m.SpecConstantDouble(typeId, (negate ? -1.0 : 1.0) * ParseDouble(lit.Text)),
            Scalar.Half => _m.SpecConstantHalf(typeId, (negate ? -1f : 1f) * ParseFloat(lit.Text)),
            Scalar.Int or Scalar.UInt or Scalar.Int16 or Scalar.UInt16 or Scalar.Int8 or Scalar.UInt8 => _m.SpecConstantInt(typeId, SignedBits()),
            Scalar.Int64 or Scalar.UInt64 => _m.SpecConstantInt64(typeId, SignedBits64()),
            _ => throw new GlslParseException($"specialization constant of type {scalar.Kind} is not supported"),
        };
        _m.Decorate(id, Decoration.SpecId, (uint)specId);
        _specConstIds.Add(id);
        return new Value(id, scalar, IsConstant: false);
    }

    // ---- compile-time constant folding ----

    // Evaluates a GLSL constant expression to a folded value, or null if it is not a compile-time
    // constant. Covers scalars, vectors, matrices, arrays and structs, with operators, constructors,
    // conversions, swizzles, indexing and the ?: operator. Memoized per node (the dispatch speculatively
    // folds every operator, so an unmemoized recursion is O(n^2) and re-parses each literal).
    CVal? EvalConst(Expr e)
    {
        if (e is null) return null;   // a missing const initializer reaches here as null
        if (e.ConstEvaluated) return (CVal?)e.ConstResult;
        // The scoping pre-pass folds speculatively, before any const is registered, so its results (in
        // particular its nulls) must not be cached onto the AST or the real fold would inherit them.
        if (_speculativeFold) return EvalConstCore(e);
        if (++_exprDepth > Parser.MaxNestingDepth) { _exprDepth--; throw new GlslParseException($"expression nesting is too deep (limit {Parser.MaxNestingDepth})", _lowerLine); }
        CVal? result;
        try { result = EvalConstCore(e); }
        finally { _exprDepth--; }
        e.ConstResult = result;
        e.ConstEvaluated = true;
        return result;
    }

    CVal? EvalConstCore(Expr e) => e switch
    {
        Literal lit => FoldLiteral(lit),
        Ident id when ShadingRateFlags.TryGetValue(id.Name, out int flag) => new CScalar(Scalar.Int, 0, flag),
        Ident id when RayFlags.TryGetValue(id.Name, out int rf) => new CScalar(Scalar.UInt, 0, rf),
        Ident id => _constValues.TryGetValue(id.Name, out var v) ? v : null,
        Unary u => EvalConstUnary(u),
        Binary b => EvalConst(b.Left) is CVal l && EvalConst(b.Right) is CVal r ? ConstBinary(b.Op, l, r) : null,
        Conditional t => EvalConst(t.Cond) is CScalar { Kind: Scalar.Bool } c ? EvalConst(c.I != 0 ? t.Then : t.Else) : null,
        Call { IsArray: true } c when ArrayCtorElement(c) is GlslType elem => EvalConstAll(c.Args) is { } es
            ? new CAgg(new ArrayType(elem, es.Count), [.. es.Select(e => e is CScalar s && elem is ScalarType et && s.Kind != et.Kind ? ConvertConst(s, et.Kind) : e)]) : null,
        Call c when Types.IsTypeName(c.Name) => EvalConstConstructor(c),
        Call c when _structs.TryGetValue(c.Name, out var st) => EvalConstAll(c.Args) is { } fs ? new CAgg(st, fs) : null,
        Call c => EvalConstBuiltin(c),
        Member m => EvalConstMember(m),
        Index ix => EvalConstIndex(ix),
        Method { Name: "length", Args.Count: 0 } meth when InferType(meth.Target) is ArrayType arr => new CScalar(Scalar.Int, arr.Length, arr.Length),
        _ => null,
    };

    // glslang's constant folder evaluates the math built-ins at compile time (in double, converting to
    // the target width only when the constant is materialized), so a call on constants is not a runtime
    // op. Anything not folded here returns null and lowers normally.
    CVal? EvalConstBuiltin(Call c)
    {
        if (CallsUserFunction(c)) return null;   // a user function is never foldable
        if (EvalConstAll(c.Args) is not { } args) return null;
        // A call with the wrong argument count indexes past `args` in a folder; bail so it lowers at
        // runtime, where the builtin-call wrapper reports "wrong number of arguments" cleanly.
        try { return FoldConstBuiltin(c, args); }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException) { return null; }
    }

    CVal? FoldConstBuiltin(Call c, List<CVal> args)
    {
        // glslang folds integer abs/sign/min/max/clamp; the float folders below only handle the float
        // domain, so intercept the all-integer case here.
        if (c.Name is "abs" or "sign" or "min" or "max" or "clamp" && args.All(a => IsIntegral(BaseScalar(CValType(a)))))
            return IntFold(c.Name, args);

        if (args.Count == 1 && UnaryMathFold(c.Name) is { } f)
            return MapMath(args[0], f);
        if (c.Name == "outerProduct" && args is [CAgg { Type: VectorType } col, CAgg { Type: VectorType } row])
            return ConstOuterProduct(col, row);
        if (c.Name is "length" or "normalize" && args is [var v]) return ConstLength(c.Name == "normalize", v);
        if (c.Name is "dot" && args is [var da, var db]) return ConstDot(da, db);
        if (c.Name is "distance" && args is [var pa, var pb]) return ConstDistance(pa, pb);
        if (c.Name is "cross" && args is [CAgg { Type: VectorType { Size: 3 } } xa, CAgg { Type: VectorType { Size: 3 } } xb])
            return ConstCross(xa, xb);
        if (c.Name is "faceforward" && args.Count == 3) return ConstFaceForward(args[0], args[1], args[2]);
        if (c.Name is "reflect" && args.Count == 2) return ConstReflect(args[0], args[1]);
        if (c.Name is "refract" && args.Count == 3) return ConstRefract(args[0], args[1], args[2]);
        // glslang folds matrixCompMult (a plain component-wise product) but leaves determinant/transpose/
        // inverse as runtime ops (verified against the oracle), so only the former is folded here.
        if (c.Name is "matrixCompMult" && args is [CAgg { Type: MatrixType } ca, CAgg { Type: MatrixType } cb])
            return ConstMatrixCompMult(ca, cb);
        if (ComponentwiseFold(c.Name) is { } fold) return MapComponentwise(fold, args);
        return null;
    }

    // The multi-argument component-wise folds, in double. A scalar argument broadcasts to every lane.
    static Func<double[], double>? ComponentwiseFold(string name) => name switch
    {
        "pow" => a => Math.Pow(a[0], a[1]),
        "min" => a => Math.Min(a[0], a[1]),
        "max" => a => Math.Max(a[0], a[1]),
        "mod" => a => a[0] - a[1] * Math.Floor(a[0] / a[1]),
        "atan" => a => a.Length == 2 ? Math.Atan2(a[0], a[1]) : Math.Atan(a[0]),
        "clamp" => a => Math.Min(Math.Max(a[0], a[1]), a[2]),
        "mix" => a => a[0] * (1.0 - a[2]) + a[1] * a[2],
        "step" => a => a[1] < a[0] ? 0.0 : 1.0,
        "smoothstep" => a => SmoothStep(a[0], a[1], a[2]),
        _ => null,
    };

    static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    // Integer-domain folds for abs/sign/min/max/clamp, honouring signedness for the comparisons.
    CVal? IntFold(string name, List<CVal> args)
    {
        bool uns = IsUnsigned(BaseScalar(CValType(args[0])));
        long Min(long a, long b) => uns ? unchecked((long)Math.Min((ulong)a, (ulong)b)) : Math.Min(a, b);
        long Max(long a, long b) => uns ? unchecked((long)Math.Max((ulong)a, (ulong)b)) : Math.Max(a, b);
        Func<long[], long>? f = name switch
        {
            "abs" => a => Math.Abs(a[0]),
            "sign" => a => Math.Sign(a[0]),
            "min" => a => Min(a[0], a[1]),
            "max" => a => Max(a[0], a[1]),
            "clamp" => a => Min(Max(a[0], a[1]), a[2]),
            _ => null,
        };
        if (f is null) return null;
        long Lane(CVal a, int i) => a is CAgg { Parts: var p } ? ((CScalar)p[Math.Min(i, p.Count - 1)]).I : ((CScalar)a).I;
        if (args.FirstOrDefault(a => a is CAgg { Type: VectorType }) is CAgg { Type: VectorType vt })
            return new CAgg(vt, [.. Enumerable.Range(0, vt.Size).Select(i => (CVal)new CScalar(vt.Component, 0, f([.. args.Select(a => Lane(a, i))])))]);
        return new CScalar(((CScalar)args[0]).Kind, 0, f([.. args.Select(a => ((CScalar)a).I)]));
    }

    // Applies a component-wise fold across scalar/vector arguments, broadcasting scalars. Integer-typed
    // arguments (min/max/clamp on ints) are left to lower normally rather than folded through double.
    CVal? MapComponentwise(Func<double[], double> fold, List<CVal> args)
    {
        // Only the floating built-ins fold through double; a bool selector (mix) is not among them.
        if (args.Any(a => !IsFloating(BaseScalar(CValType(a))))) return null;
        double Lane(CVal a, int i) => a is CAgg { Parts: var p } ? ((CScalar)p[Math.Min(i, p.Count - 1)]).F : ((CScalar)a).F;

        if (args.FirstOrDefault(a => a is CAgg { Type: VectorType }) is CAgg { Type: VectorType vt })
        {
            var parts = Enumerable.Range(0, vt.Size)
                .Select(i => (CVal)new CScalar(vt.Component, fold([.. args.Select(a => Lane(a, i))]), 0));
            return new CAgg(vt, [.. parts]);
        }
        var kind = ((CScalar)args[0]).Kind;
        return new CScalar(kind, fold([.. args.Select(a => ((CScalar)a).F)]), 0);
    }

    // The float components of a folded scalar or vector, or null if it is neither (or is an integer,
    // which the geometric built-ins never take).
    static double[]? FloatParts(CVal v) => v switch
    {
        CScalar s when IsFloating(s.Kind) => [s.F],
        CAgg { Type: VectorType vt } a when IsFloating(vt.Component) => [.. a.Parts.Cast<CScalar>().Select(p => p.F)],
        _ => null,
    };

    static Scalar ComponentOf(CVal v) => v is CAgg { Type: VectorType vt } ? vt.Component : ((CScalar)v).Kind;

    static CVal Rebuild(CVal shape, Scalar kind, double[] parts) => shape is CAgg { Type: VectorType vt }
        ? new CAgg(vt, [.. parts.Select(p => (CVal)new CScalar(kind, p, 0))])
        : new CScalar(kind, parts[0], 0);

    // glslang folds these in double: the sum of squares, its square root, then the divide.
    static CVal? ConstLength(bool normalize, CVal v)
    {
        if (FloatParts(v) is not { } p) return null;
        double length = Math.Sqrt(p.Sum(c => c * c));
        return normalize ? Rebuild(v, ComponentOf(v), [.. p.Select(c => c / length)]) : new CScalar(ComponentOf(v), length, 0);
    }

    static CVal? ConstDot(CVal a, CVal b)
    {
        if (FloatParts(a) is not { } x || FloatParts(b) is not { } y || x.Length != y.Length) return null;
        return new CScalar(ComponentOf(a), x.Zip(y).Sum(t => t.First * t.Second), 0);
    }

    static CVal? ConstDistance(CVal a, CVal b)
    {
        if (FloatParts(a) is not { } x || FloatParts(b) is not { } y || x.Length != y.Length) return null;
        return new CScalar(ComponentOf(a), Math.Sqrt(x.Zip(y).Sum(t => (t.First - t.Second) * (t.First - t.Second))), 0);
    }

    static CVal? ConstCross(CAgg a, CAgg b)
    {
        if (FloatParts(a) is not { } x || FloatParts(b) is not { } y) return null;
        return Rebuild(a, ComponentOf(a),
            [x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0]]);
    }

    static double Dot(double[] a, double[] b) => a.Zip(b).Sum(t => t.First * t.Second);

    // reflect(I, N) = I - 2*dot(N,I)*N.
    static CVal? ConstReflect(CVal iv, CVal nv)
    {
        if (FloatParts(iv) is not { } i || FloatParts(nv) is not { } n) return null;
        double d = 2.0 * Dot(i, n);
        return Rebuild(iv, ComponentOf(iv), [.. i.Zip(n).Select(t => t.First - d * t.Second)]);
    }

    // refract(I, N, eta): k = 1 - eta^2*(1 - dot(N,I)^2); k<0 -> 0, else eta*I - (eta*dot + sqrt(k))*N.
    static CVal? ConstRefract(CVal iv, CVal nv, CVal etaV)
    {
        if (FloatParts(iv) is not { } i || FloatParts(nv) is not { } n || etaV is not CScalar eta) return null;
        double d = Dot(i, n);
        double k = 1.0 - eta.F * eta.F * (1.0 - d * d);
        double[] r = k < 0.0 ? new double[i.Length]
            : [.. i.Zip(n).Select(t => eta.F * t.First - (eta.F * d + Math.Sqrt(k)) * t.Second)];
        return Rebuild(iv, ComponentOf(iv), r);
    }

    // faceforward(N, I, Nref): dot(I, Nref) < 0 ? N : -N.
    static CVal? ConstFaceForward(CVal nv, CVal iv, CVal nref)
    {
        if (FloatParts(nv) is not { } n || FloatParts(iv) is not { } i || FloatParts(nref) is not { } r) return null;
        bool keep = Dot(i, r) < 0.0;
        return Rebuild(nv, ComponentOf(nv), [.. n.Select(c => keep ? c : -c)]);
    }


    // The component-wise unary math built-ins, mirroring glslang's folded set.
    static Func<double, double>? UnaryMathFold(string name) => name switch
    {
        "radians" => v => v * Math.PI / 180.0,
        "degrees" => v => v * 180.0 / Math.PI,
        "sin" => Math.Sin, "cos" => Math.Cos, "tan" => Math.Tan,
        "asin" => Math.Asin, "acos" => Math.Acos, "atan" => Math.Atan,
        // The hyperbolics are deliberately absent: glslang does not fold them, it emits the GLSL.std.450
        // sequence even for a constant argument, so folding here would diverge.
        "exp" => Math.Exp, "log" => Math.Log,
        "exp2" => v => Math.Pow(2.0, v), "log2" => Math.Log2,
        "sqrt" => Math.Sqrt, "inversesqrt" => v => 1.0 / Math.Sqrt(v),
        "floor" => Math.Floor, "ceil" => Math.Ceiling, "trunc" => Math.Truncate,
        "round" => v => Math.Round(v, MidpointRounding.AwayFromZero),
        "roundEven" => v => Math.Round(v, MidpointRounding.ToEven),
        "abs" => Math.Abs,
        "sign" => v => Math.Sign(v),
        "fract" => v => v - Math.Floor(v),
        _ => null,
    };

    // Applies a folded math function across a float scalar or vector; null for anything else (an
    // integer argument is left to lower normally rather than folded to the wrong type).
    static CVal? MapMath(CVal v, Func<double, double> f) => v switch
    {
        CScalar s when IsFloating(s.Kind) => s with { F = f(s.F) },
        CAgg { Type: VectorType } a => MapScalars(a, s => IsFloating(s.Kind) ? s with { F = f(s.F) } : null),
        _ => null,
    };

    // outerProduct(c, r) is the matrix whose element [col][row] is c[row] * r[col].
    CVal? ConstOuterProduct(CAgg c, CAgg r)
    {
        var ct = (VectorType)c.Type;
        var rt = (VectorType)r.Type;
        var columnType = new VectorType(ct.Component, ct.Size);
        var columns = new List<CVal>(rt.Size);
        for (int col = 0; col < rt.Size; col++)
        {
            var rows = new List<CVal>(ct.Size);
            for (int row = 0; row < ct.Size; row++)
            {
                if (ScalarBinary("*", (CScalar)c.Parts[row], (CScalar)r.Parts[col]) is not CScalar s) return null;
                rows.Add(s);
            }
            columns.Add(new CAgg(columnType, rows));
        }
        return new CAgg(new MatrixType(rt.Size, ct.Size, ct.Component), columns);
    }

    List<CVal>? EvalConstAll(IReadOnlyList<Expr> args)
    {
        var result = new List<CVal>(args.Count);
        foreach (var a in args) { if (EvalConst(a) is not CVal v) return null; result.Add(v); }
        return result;
    }

    static CScalar? FoldLiteral(Literal lit) => lit.Kind switch
    {
        // Folded arithmetic runs in double and only narrows when the constant is materialized (glslang
        // does the same), so a float literal must not round-trip through float first: `1.0/2.2` differs
        // by an ULP if 2.2 is narrowed before the divide.
        TokenKind.FloatConst => new CScalar(Scalar.Float, ParseDouble(lit.Text), 0),
        TokenKind.DoubleConst => new CScalar(Scalar.Double, ParseDouble(lit.Text), 0),
        TokenKind.HalfConst => new CScalar(Scalar.Half, ParseDouble(lit.Text), 0),
        TokenKind.Int64Const => new CScalar(Scalar.Int64, 0, unchecked((long)ParseInt64Literal(lit.Text))),
        TokenKind.Uint64Const => new CScalar(Scalar.UInt64, 0, unchecked((long)ParseInt64Literal(lit.Text))),
        TokenKind.IntConst => new CScalar(Scalar.Int, 0, (int)ParseIntLiteral(lit.Text)),
        TokenKind.UintConst => new CScalar(Scalar.UInt, 0, ParseIntLiteral(lit.Text)),
        TokenKind.BoolConst => new CScalar(Scalar.Bool, 0, lit.Text == "true" ? 1 : 0),
        _ => null,
    };

    CVal? EvalConstUnary(Unary u)
    {
        if (EvalConst(u.Operand) is not CVal a) return null;
        return a switch
        {
            CScalar s => ScalarUnary(u.Op, s),
            CAgg { Type: VectorType } v => MapScalars(v, s => ScalarUnary(u.Op, s)),
            _ => null,
        };
    }

    static CScalar? ScalarUnary(string op, CScalar a) => op switch
    {
        "+" => a,
        "-" => IsFloating(a.Kind) ? a with { F = -a.F } : a with { I = -a.I },
        "!" => a.Kind == Scalar.Bool ? Bool(a.I == 0) : null,
        "~" => IsIntegral(a.Kind) ? a with { I = ~a.I } : null,
        _ => null,
    };

    // Binary operator on folded values: scalar/scalar, component-wise vector, vector/scalar,
    // and matrix component-wise (matrix products fall through to null -> not folded).
    CVal? ConstBinary(string op, CVal l, CVal r)
    {
        if (l is CScalar ls && r is CScalar rs) return ScalarBinary(op, ls, rs);

        if (l is CAgg { Type: VectorType } lv && r is CAgg { Type: VectorType } rv)
        {
            if (op is "==" or "!=")   // whole-vector comparison operator -> single bool
            {
                bool equal = lv.Parts.Zip(rv.Parts).All(p => ((CScalar)ScalarBinary("==", (CScalar)p.First, (CScalar)p.Second)!).I != 0);
                return Bool(op == "==" ? equal : !equal);
            }
            return ZipScalars(lv, rv, op);
        }
        if (l is CAgg { Type: VectorType } lvec && r is CScalar rsc) return MapScalars(lvec, s => ScalarBinary(op, s, rsc));
        if (l is CScalar lsc && r is CAgg { Type: VectorType } rvec) return MapScalars(rvec, s => ScalarBinary(op, lsc, s));

        // Component-wise matrix ops (matrix * scalar / matrix +/- matrix); matrix products are not folded.
        if (l is CAgg { Type: MatrixType } lm && r is CScalar msc) return MapColumns(lm, s => ScalarBinary(op, s, msc));
        if (l is CScalar msc2 && r is CAgg { Type: MatrixType } rm && op == "*") return MapColumns(rm, s => ScalarBinary(op, s, msc2));
        if (l is CAgg { Type: MatrixType } lm2 && r is CAgg { Type: MatrixType } rm2 && op is "+" or "-")
            return new CAgg(lm2.Type, [.. lm2.Parts.Zip(rm2.Parts).Select(p => ZipScalars((CAgg)p.First, (CAgg)p.Second, op)!)]);

        // A matrix product is linear algebra rather than component-wise, so it folds by dot product.
        if (op == "*")
        {
            if (l is CAgg { Type: MatrixType } pa && r is CAgg { Type: MatrixType } pb) return ConstMatrixTimesMatrix(pa, pb);
            if (l is CAgg { Type: MatrixType } pm && r is CAgg { Type: VectorType } pv) return ConstMatrixTimesVector(pm, pv);
            if (l is CAgg { Type: VectorType } qv && r is CAgg { Type: MatrixType } qm) return ConstVectorTimesMatrix(qv, qm);
        }

        return null;
    }

    static CScalar MatrixElement(CAgg m, int column, int row) => (CScalar)((CAgg)m.Parts[column]).Parts[row];

    // Sums the pairwise products of two equal-length scalar runs; null if any step is not foldable.
    CScalar? DotFold(int length, Func<int, CScalar> left, Func<int, CScalar> right)
    {
        CScalar? sum = null;
        for (int i = 0; i < length; i++)
        {
            if (ScalarBinary("*", left(i), right(i)) is not CScalar term) return null;
            sum = sum is null ? term : ScalarBinary("+", sum, term);
            if (sum is null) return null;
        }
        return sum;
    }

    CVal? ConstMatrixTimesVector(CAgg m, CAgg v)   // matCxR * vecC -> vecR
    {
        var mt = (MatrixType)m.Type;
        if (((VectorType)v.Type).Size != mt.Columns) return null;
        var parts = new List<CVal>(mt.Rows);
        for (int row = 0; row < mt.Rows; row++)
        {
            if (DotFold(mt.Columns, c => MatrixElement(m, c, row), c => (CScalar)v.Parts[c]) is not CScalar s) return null;
            parts.Add(s);
        }
        return new CAgg(new VectorType(mt.Component, mt.Rows), parts);
    }

    CVal? ConstVectorTimesMatrix(CAgg v, CAgg m)   // vecR * matCxR -> vecC
    {
        var mt = (MatrixType)m.Type;
        if (((VectorType)v.Type).Size != mt.Rows) return null;
        var parts = new List<CVal>(mt.Columns);
        for (int col = 0; col < mt.Columns; col++)
        {
            if (DotFold(mt.Rows, r => (CScalar)v.Parts[r], r => MatrixElement(m, col, r)) is not CScalar s) return null;
            parts.Add(s);
        }
        return new CAgg(new VectorType(mt.Component, mt.Columns), parts);
    }

    CVal? ConstMatrixCompMult(CAgg a, CAgg b)   // component-wise product; both matrices must share dimensions
    {
        var at = (MatrixType)a.Type;
        var bt = (MatrixType)b.Type;
        if (at.Columns != bt.Columns || at.Rows != bt.Rows) return null;
        var columns = new List<CVal>(at.Columns);
        for (int col = 0; col < at.Columns; col++)
        {
            var rows = new List<CVal>(at.Rows);
            for (int row = 0; row < at.Rows; row++)
            {
                if (ScalarBinary("*", (CScalar)MatrixElement(a, col, row), (CScalar)MatrixElement(b, col, row)) is not { } s) return null;
                rows.Add(s);
            }
            columns.Add(new CAgg(new VectorType(at.Component, at.Rows), rows));
        }
        return new CAgg(at, columns);
    }

    CVal? ConstMatrixTimesMatrix(CAgg a, CAgg b)   // matA(Ca,Ra) * matB(Cb,Rb) -> mat(Cb,Ra), needs Ca == Rb
    {
        var at = (MatrixType)a.Type;
        var bt = (MatrixType)b.Type;
        if (at.Columns != bt.Rows) return null;
        var columns = new List<CVal>(bt.Columns);
        for (int col = 0; col < bt.Columns; col++)
        {
            var rows = new List<CVal>(at.Rows);
            for (int row = 0; row < at.Rows; row++)
            {
                if (DotFold(at.Columns, k => MatrixElement(a, k, row), k => MatrixElement(b, col, k)) is not CScalar s) return null;
                rows.Add(s);
            }
            columns.Add(new CAgg(new VectorType(at.Component, at.Rows), rows));
        }
        return new CAgg(new MatrixType(bt.Columns, at.Rows, at.Component), columns);
    }

    CScalar? ScalarBinary(string op, CScalar l, CScalar r)
    {
        if (op is "&&" or "||" or "^^")
        {
            if (l.Kind != Scalar.Bool || r.Kind != Scalar.Bool) return null;
            bool a = l.I != 0, b = r.I != 0;
            return Bool(op switch { "&&" => a && b, "||" => a || b, _ => a ^ b });
        }

        bool anyFloat = IsFloating(l.Kind) || IsFloating(r.Kind);
        if (op is "<" or ">" or "<=" or ">=" or "==" or "!=")
            return Bool(anyFloat ? CompareD(op, AsF(l), AsF(r)) : CompareL(op, l.I, r.I));

        if (anyFloat)
        {
            if (op is "&" or "|" or "^" or "<<" or ">>" or "%") return null;
            double a = AsF(l), c = AsF(r);
            // IEEE folding matches glslang exactly here (probed): 1.0/0.0 -> +Inf, -1.0/0.0 -> -Inf,
            // 0.0/0.0 -> NaN - the same OpConstant bit patterns glslang materializes.
            // The result keeps the wider operand's floating kind: hardcoding Float here made a folded
            // double/half vector materialize float components, which is invalid SPIR-V.
            var fkind = Wider(l.Kind, r.Kind);
            return new CScalar(IsFloating(fkind) ? fkind : Scalar.Float, op switch { "+" => a + c, "-" => a - c, "*" => a * c, "/" => a / c, _ => double.NaN }, 0);
        }

        var kind = Wider(l.Kind, r.Kind);
        bool unsigned = IsUnsigned(kind);
        long x = l.I, y = r.I;
        // Div/mod/shift corner cases follow glslang's x86-style constant folding (probed against
        // glslang -V, which folds rather than rejects):
        //   int  x/0 -> 0x7FFFFFFF (INT_MAX, sign-independent)      uint x/0u -> 0xFFFFFFFF
        //   x % 0 -> x (the left operand, sign preserved)
        //   shift counts mask to the low 5 bits (1<<-1 -> INT_MIN, 1<<33 -> 2, -8>>100 -> -1)
        int sh = (int)(y & 31);
        long result = op switch
        {
            "+" => x + y, "-" => x - y, "*" => x * y,
            "/" => y == 0 ? (unsigned ? 0xFFFFFFFFL : 0x7FFFFFFFL)
                          : unsigned ? (long)((uint)x / (uint)y) : x / y,
            "%" => y == 0 ? x : unsigned ? (long)((uint)x % (uint)y) : x % y,
            "&" => x & y, "|" => x | y, "^" => x ^ y,
            "<<" => x << sh,
            ">>" => unsigned ? (long)((uint)x >> sh) : x >> sh,
            _ => throw new GlslParseException($"operator '{op}' is not foldable"),
        };
        // GLSL int/uint are 32-bit and wrap on overflow, so keep the folded value in range: `0u - 1u`
        // is 4294967295u, not -1, and a signed overflow wraps rather than widening into the host long.
        // The 64-bit kinds already span the host long, so they need no narrowing.
        if (Is64Bit(kind)) return new CScalar(kind, 0, result);
        return new CScalar(kind, 0, unsigned ? (uint)result : (int)result);
    }

    // Apply a scalar op across a vector's / matrix's components, null if any component fails.
    static CAgg? MapScalars(CAgg vec, Func<CScalar, CScalar?> f)
    {
        var parts = new List<CVal>(vec.Parts.Count);
        foreach (var p in vec.Parts) { if (f((CScalar)p) is not CScalar s) return null; parts.Add(s); }
        return new CAgg(vec.Type, parts);
    }

    CAgg? ZipScalars(CAgg a, CAgg b, string op)
    {
        if (a.Parts.Count != b.Parts.Count) return null;
        var parts = new List<CVal>(a.Parts.Count);
        for (int i = 0; i < a.Parts.Count; i++)
        {
            if (ScalarBinary(op, (CScalar)a.Parts[i], (CScalar)b.Parts[i]) is not CScalar s) return null;
            parts.Add(s);
        }
        return new CAgg(a.Type, parts);
    }

    static CAgg? MapColumns(CAgg matrix, Func<CScalar, CScalar?> f)
    {
        var columns = new List<CVal>(matrix.Parts.Count);
        foreach (var col in matrix.Parts) { if (MapScalars((CAgg)col, f) is not CAgg c) return null; columns.Add(c); }
        return new CAgg(matrix.Type, columns);
    }

    static CScalar Bool(bool b) => new(Scalar.Bool, 0, b ? 1 : 0);
    static double AsF(CScalar s) => IsFloating(s.Kind) ? s.F : s.I;
    static bool CompareD(string op, double a, double b) => op switch { "<" => a < b, ">" => a > b, "<=" => a <= b, ">=" => a >= b, "==" => a == b, _ => a != b };
    static bool CompareL(string op, long a, long b) => op switch { "<" => a < b, ">" => a > b, "<=" => a <= b, ">=" => a >= b, "==" => a == b, _ => a != b };

    // Vector/array/struct member access and swizzles on folded constants.
    CVal? EvalConstMember(Member m)
    {
        if (EvalConst(m.Target) is not CVal t) return null;
        if (t is CAgg { Type: StructType st } sv) { int i = IndexOfField(st, m.Name); return i >= 0 ? sv.Parts[i] : null; }
        if (t is CAgg { Type: VectorType vt } vv)
        {
            if (!IsSwizzleName(m.Name)) return null;
            ValidateSwizzle(vt, m.Name, writeMask: false);
            int[] sw = [.. m.Name.Select(SwizzleIndex)];
            return sw.Length == 1 ? vv.Parts[sw[0]] : new CAgg(new VectorType(vt.Component, sw.Length), [.. sw.Select(i => vv.Parts[i])]);
        }
        return null;
    }

    CVal? EvalConstIndex(Index ix)
    {
        if (EvalConst(ix.Target) is not CAgg agg || EvalConst(ix.Idx) is not CScalar { Kind: Scalar.Int or Scalar.UInt } idx) return null;
        return idx.I >= 0 && idx.I < agg.Parts.Count ? agg.Parts[(int)idx.I] : null;
    }

    CVal? EvalConstConstructor(Call c)
    {
        var type = Resolve(c.Name);
        if (EvalConstAll(c.Args) is not { } args) return null;
        return type switch
        {
            ScalarType st => args is [CScalar s] ? ConvertConst(s, st.Kind) : null,
            VectorType vt => BuildConstVector(vt, args),
            MatrixType mt => BuildConstMatrix(mt, args),
            _ => null,
        };
    }

    CVal? BuildConstVector(VectorType vt, List<CVal> args)
    {
        var comps = new List<CScalar>();
        foreach (var a in args)
        {
            if (a is CScalar s) comps.Add(s);
            else if (a is CAgg { Type: VectorType } v) comps.AddRange(v.Parts.Cast<CScalar>());
            else return null;
        }
        if (comps.Count == 1 && vt.Size > 1) comps = [.. Enumerable.Repeat(comps[0], vt.Size)];   // splat
        // Truncation is only valid from a single vector source (vec2(vec4)); an over-provided scalar list
        // is invalid, so return null and let the runtime constructor report the component-count error.
        else if (comps.Count > vt.Size && args is [CAgg { Type: VectorType }]) comps = [.. comps.Take(vt.Size)];
        if (comps.Count != vt.Size) return null;
        return new CAgg(vt, [.. comps.Select(s => (CVal)ConvertConst(s, vt.Component))]);
    }

    CVal? BuildConstMatrix(MatrixType mt, List<CVal> args)
    {
        var column = new VectorType(mt.Component, mt.Rows);
        if (args.Count == mt.Columns && args.All(a => a is CAgg { Type: VectorType }))   // from column vectors
            return new CAgg(mt, args);
        if (args.All(a => a is CScalar))
        {
            var scalars = args.Select(a => ConvertConst((CScalar)a, mt.Component)).ToList();
            if (scalars.Count == mt.Columns * mt.Rows)   // column-major scalar list
                return new CAgg(mt, [.. Enumerable.Range(0, mt.Columns).Select(col =>
                    (CVal)new CAgg(column, [.. scalars.Skip(col * mt.Rows).Take(mt.Rows).Cast<CVal>()]))]);
            if (scalars.Count == 1)   // diagonal
            {
                var diag = scalars[0];
                var zero = new CScalar(mt.Component, 0, 0);
                return new CAgg(mt, [.. Enumerable.Range(0, mt.Columns).Select(col =>
                    (CVal)new CAgg(column, [.. Enumerable.Range(0, mt.Rows).Select(row => (CVal)(row == col ? diag : zero))]))]);
            }
        }
        return null;
    }

    static CScalar ConvertConst(CScalar a, Scalar target)
    {
        if (a.Kind == target) return a;
        bool sourceFloat = IsFloating(a.Kind);
        return target switch
        {
            Scalar.Float or Scalar.Half or Scalar.Double => new CScalar(target, AsF(a), 0),
            Scalar.Int => new CScalar(Scalar.Int, 0, sourceFloat ? (int)a.F : (int)a.I),
            Scalar.UInt => new CScalar(Scalar.UInt, 0, sourceFloat ? (uint)a.F : (uint)a.I),
            Scalar.Int64 => new CScalar(Scalar.Int64, 0, sourceFloat ? (long)a.F : a.I),
            Scalar.UInt64 => new CScalar(Scalar.UInt64, 0, unchecked((long)(sourceFloat ? (ulong)a.F : (ulong)a.I))),
            Scalar.Int8 => new CScalar(Scalar.Int8, 0, sourceFloat ? (sbyte)a.F : (sbyte)a.I),
            Scalar.UInt8 => new CScalar(Scalar.UInt8, 0, sourceFloat ? (byte)a.F : (byte)a.I),
            Scalar.Int16 => new CScalar(Scalar.Int16, 0, sourceFloat ? (short)a.F : (short)a.I),
            Scalar.UInt16 => new CScalar(Scalar.UInt16, 0, sourceFloat ? (ushort)a.F : (ushort)a.I),
            Scalar.Bool => Bool(sourceFloat ? a.F != 0 : a.I != 0),
            _ => a,
        };
    }

    // Emits a folded constant as SPIR-V (OpConstant / OpConstantComposite), recursing into aggregates.
    Value Materialize(CVal v)
    {
        if (v is CScalar s) return s.Kind switch
        {
            Scalar.Float => new Value(_m.ConstantFloat(SpvType(Types.Float), (float)s.F), Types.Float, true),
            Scalar.Half => new Value(_m.ConstantHalf(SpvType(Types.Half), (float)s.F), Types.Half, true),
            Scalar.Double => new Value(_m.ConstantDouble(SpvType(Types.Double), s.F), Types.Double, true),
            Scalar.Int64 => new Value(_m.ConstantInt64(SpvType(Types.Int64), unchecked((ulong)s.I)), Types.Int64, true),
            Scalar.UInt64 => new Value(_m.ConstantInt64(SpvType(Types.UInt64), unchecked((ulong)s.I)), Types.UInt64, true),
            Scalar.Int => new Value(_m.ConstantInt(SpvType(Types.Int), unchecked((uint)(int)s.I)), Types.Int, true),
            Scalar.UInt => new Value(_m.ConstantInt(SpvType(Types.UInt), unchecked((uint)s.I)), Types.UInt, true),
            Scalar.Int8 => new Value(_m.ConstantInt(SpvType(Types.Int8), unchecked((uint)(sbyte)s.I)), Types.Int8, true),
            Scalar.UInt8 => new Value(_m.ConstantInt(SpvType(Types.UInt8), unchecked((uint)(byte)s.I)), Types.UInt8, true),
            Scalar.Int16 => new Value(_m.ConstantInt(SpvType(Types.Int16), unchecked((uint)(short)s.I)), Types.Int16, true),
            Scalar.UInt16 => new Value(_m.ConstantInt(SpvType(Types.UInt16), unchecked((uint)(ushort)s.I)), Types.UInt16, true),
            Scalar.Bool => new Value(_m.ConstantBool(SpvType(Types.Bool), s.I != 0), Types.Bool, true),
            _ => throw new GlslParseException("cannot materialize a void constant"),
        };
        var agg = (CAgg)v;
        // A const array/matrix is re-materialized on every index access (glslang's indexable-temp pattern);
        // its ConstantComposite dedups to one id and creates no new instructions after the first, so caching
        // the id on the (per-compile) value skips rebuilding the constituent array each time.
        if (agg.Materialized != 0) return new Value(agg.Materialized, agg.Type, true);
        uint typeId = SpvType(agg.Type);   // glslang creates the aggregate type before its constituents
        uint[] parts = [.. agg.Parts.Select(p => Materialize(p).Id)];
        uint id = _m.ConstantComposite(typeId, parts);
        agg.Materialized = id;
        return new Value(id, agg.Type, true);
    }

    void EmitBlock(InterfaceBlock block)
    {
        bool isBuffer = block.Qualifiers.Has("buffer");
        bool isPushConstant = block.Qualifiers.Layout.Any(i => i.Name == "push_constant");
        if (isPushConstant && (LayoutInt(block.Qualifiers, "binding") is not null || LayoutInt(block.Qualifiers, "set") is not null))
            throw new GlslParseException($"a push_constant block cannot have a set or binding");
        if (isPushConstant && _pushConstantSeen)
            throw new GlslParseException("a shader can declare at most one push_constant block");
        if (isPushConstant) _pushConstantSeen = true;
        bool isShared = block.Qualifiers.Has("shared");
        // A `layout(shaderRecordEXT) buffer` block lives in ShaderRecordBufferKHR storage with a Block
        // decoration and member offsets but no descriptor binding (the SBT record backs it).
        bool isShaderRecord = block.Qualifiers.Layout.Any(i => i.Name == "shaderRecordEXT");

        // A buffer_reference block is a pointer TYPE declaration (registered up front), not a variable.
        if (block.Qualifiers.Layout.Any(i => i.Name == "buffer_reference")) return;

        // A redeclaration of the built-in gl_PerVertex block (standard in geometry/tessellation shaders
        // to establish the in/out interface) is handled by our synthesized per-vertex block and its
        // gl_Position/gl_in/gl_out access paths; the explicit redeclaration is a no-op here. The NV
        // passthrough form is the exception: it is its own PassthroughNV-decorated Input block.
        if (block.BlockName == "gl_PerVertex")
        {
            if (block.Qualifiers.Layout.Any(i => i.Name == "passthrough")) EmitPassthroughPerVertex(block);
            // `out gl_PerVertex { ... };` (no instance array) redeclares the single output block that the
            // vertex/geometry/tess-eval stages write through, sizing gl_ClipDistance/gl_CullDistance and
            // possibly dropping gl_PointSize. `in gl_PerVertex { ... } gl_in[];` redeclares the geometry/
            // tessellation input array's element block the same way.
            else if (block.Qualifiers.Has("out") && block.InstanceArray.Count == 0
                     && stage is ShaderStage.Vertex or ShaderStage.Geometry or ShaderStage.TessEvaluation)
                _perVertexOut = PerVertexMembers(block);
            else if (block.Qualifiers.Has("in"))
                _perVertexIn = PerVertexMembers(block);
            return;
        }

        // in/out interface blocks (vertex->fragment data, etc.) are a Block-decorated struct in
        // Input/Output storage with a Location on the variable and no memory (std140) layout.
        if (block.Qualifiers.Has("in") || block.Qualifiers.Has("out"))
        {
            EmitInterfaceBlock(block, block.Qualifiers.Has("out") ? StorageClass.Output : StorageClass.Input);
            return;
        }
        if (!block.Qualifiers.Has("uniform") && !isBuffer && !isShared) return;

        var fields = block.Members
            .SelectMany(f => f.Declarators.Select(d => (d.Name, ApplyArrayDims(ResolveTypeRef(f.Type), d.Array))))
            .ToList();
        // glslang reports a duplicate member at the DUPLICATE's line, not the block's.
        var seenMembers = new HashSet<string>();
        foreach (var f in block.Members)
            foreach (var d in f.Declarators)
                if (!seenMembers.Add(d.Name))
                    throw new GlslParseException($"interface block '{block.BlockName}' has duplicate member names", d.Line);
        // Only the FINAL member of a storage buffer may be a runtime array. glslang implicitly sizes any
        // other unsized member from its largest constant index; Prism does not implement that yet, so
        // reject it rather than emit an OpTypeRuntimeArray in an illegal position.
        for (int fi = 0; fi < fields.Count; fi++)
            if (fields[fi].Item2 is RuntimeArrayType && !(isBuffer && fi == fields.Count - 1))
                throw new GlslParseException(
                    $"member '{fields[fi].Name}' of block '{block.BlockName}' is an unsized array; only the last member of a buffer block may be unsized");
        // Samplers/images/acceleration structures are descriptors, not data, so they cannot live inside a
        // uniform or buffer block (the resulting OpTypeStruct is invalid in Vulkan).
        foreach (var fl in fields)
            if (ContainsOpaque(fl.Item2))
                throw new GlslParseException($"member '{fl.Name}' of block '{block.BlockName}' contains an opaque type, which is not allowed in a block");
        // A member may pin its own byte offset with layout(offset=N) instead of taking the packed one.
        var explicitOffsets = block.Members
            .SelectMany(f => f.Declarators.Select(_ => LayoutInt(f.Qualifiers, "offset")))
            .ToList();
        var aligns = block.Members
            .SelectMany(f => f.Declarators.Select(_ => LayoutInt(f.Qualifiers, "align")))
            .ToList();
        // A row_major/column_major qualifier is per member; the block default (if any) applies to the rest.
        bool blockRowMajor = block.Qualifiers.Layout.Any(i => i.Name == "row_major");
        var rowMajor = block.Members
            .SelectMany(f => f.Declarators.Select(_ =>
                f.Qualifiers.Layout.Any(i => i.Name == "row_major")
                || (blockRowMajor && !f.Qualifiers.Layout.Any(i => i.Name == "column_major"))))
            .ToList();
        // Memory qualifiers may also sit on an individual member, in addition to any on the block itself.
        var memberMemory = block.Members
            .SelectMany(f => f.Declarators.Select(_ => MemoryDecorationsOf(f.Qualifiers)))
            .ToList();
        var st = new StructType(block.BlockName, fields);
        _blockStructs.Add(st);   // its bool members are stored as uint; see BlockStored
        if (isBuffer) _bufferBlockStructs.Add(st);
        // SPIR-V 1.3 made the StorageBuffer storage class + Block decoration the native way to declare an
        // SSBO; before that (and at 1.0) it is Uniform storage + BufferBlock.
        // GL_EXT_buffer_reference opts the module into it at any version (glslang setUseStorageBuffer), through
        // SPV_KHR_storage_buffer_storage_class below 1.3.
        bool nativeStorageBuffer = isBuffer && (version >= SpirVVersion.V1_3 || _bufferRefs.Count > 0);
        if (nativeStorageBuffer) AddIncorporatedExtension("SPV_KHR_storage_buffer_storage_class", SpirVVersion.V1_3);
        var storage = isShaderRecord ? StorageClass.ShaderRecordBufferKHR
                    : isShared ? StorageClass.Workgroup
                    : isPushConstant ? StorageClass.PushConstant
                    : nativeStorageBuffer ? StorageClass.StorageBuffer
                    : StorageClass.Uniform;
        var layout = BlockRulesetOf(block.Qualifiers, isBuffer || isPushConstant || isShared);
        _blockLayout[st] = layout;
        var blockMem = _vmmPragma ? BlockMemQual(block.Qualifiers) : MemQual.None;

        // An instance array (`Block {...} u[N];`) is a descriptor array (OpTypeArray of the Block struct);
        // an unsized one (`u[]`) is a runtime descriptor array.
        GlslType globalType = st;
        if (block.InstanceArray.Count > 0)
        {
            if (block.InstanceName is null) throw new GlslParseException($"block '{block.BlockName}' with an instance array needs an instance name");
            if (isPushConstant) throw new GlslParseException("a push_constant block cannot be an array");
            globalType = ApplyArrayDims(st, block.InstanceArray) switch
            {
                ArrayType a => a,
                RuntimeArrayType r => r,
                _ => throw new GlslParseException($"block '{block.BlockName}' instance array must have a constant size"),
            };
        }

        // glslang materializes the block struct+variable lazily, at first reference in the body.
        Global BuildBlock()
        {
            uint[] memberIds = [.. st.Fields.Select((f, i) => BlockFieldType(f.Type, layout, rowMajor[i]))];
            // A narrow (8/16-bit) member in a block needs the storage-class-specific access capability.
            if (st.Fields.Any(f => ContainsWidth(f.Type, 16)))
            {
                _m.AddCapability(isPushConstant ? Capability.StoragePushConstant16
                    : isBuffer ? Capability.StorageBuffer16BitAccess : Capability.UniformAndStorageBuffer16BitAccess);
                AddIncorporatedExtension("SPV_KHR_16bit_storage", SpirVVersion.V1_3);
            }
            if (st.Fields.Any(f => ContainsWidth(f.Type, 8)))
            {
                // Keyed by the block's storage class, not by buffer-vs-uniform: below 1.3 an SSBO is a
                // Uniform-class BufferBlock, which glslang (and spirv-val) cover with the Uniform cap.
                _m.AddCapability(isPushConstant ? Capability.StoragePushConstant8
                    : nativeStorageBuffer ? Capability.StorageBuffer8BitAccess : Capability.UniformAndStorageBuffer8BitAccess);
                AddIncorporatedExtension("SPV_KHR_8bit_storage", SpirVVersion.V1_5);
            }
            uint structType = _m.TypeStructUnique(memberIds);
            var refMembers = st.Fields.Where(f => f.Type is BufferReferenceType).Select(f => ((BufferReferenceType)f.Type).Name).ToArray();
            // A reference member forward-declares its pointer above; glslang defines the pointee struct
            // and the pointer right after this enclosing struct, its names after this struct's names,
            // and its decorations after this struct's decorations.
            var definedRefs = refMembers.SelectMany(BufferRefTypes).ToList();
            _m.AddName(structType, block.BlockName);
            for (int i = 0; i < st.Fields.Count; i++) _m.AddMemberName(structType, i, st.Fields[i].Name);
            foreach (var r in definedRefs) BufferRefNames(r);
            // Per-member memory qualifiers (glslang emits these before the member's Offset).
            var memberQuals = new List<Decoration>();
            if (isBuffer && block.Qualifiers.Has("readonly")) memberQuals.Add(Decoration.NonWritable);
            if (isBuffer && block.Qualifiers.Has("writeonly")) memberQuals.Add(Decoration.NonReadable);
            // Under the Vulkan memory model, coherent/volatile are per-access memory operands, not member
            // decorations, so they are dropped here and applied on each load/store instead.
            foreach (var (word, decoration) in MemoryQualifiers)
                if (block.Qualifiers.Has(word) && !(_vmmPragma && decoration is Decoration.Coherent or Decoration.Volatile))
                    memberQuals.Add(decoration);
            DecorateLayout(st, structType, memberIds, layout, memberQuals, explicitOffsets, rowMajor, aligns, memberMemory);
            _m.Decorate(structType, isBuffer && !nativeStorageBuffer && !isShaderRecord ? Decoration.BufferBlock : Decoration.Block);
            if (isShared)
            {
                _m.AddCapability(Capability.WorkgroupMemoryExplicitLayoutKHR);
                _m.AddExtension("SPV_KHR_workgroup_memory_explicit_layout");
            }
            foreach (var r in definedRefs) BufferRefDecorate(r);

            uint varType = structType;
            if (globalType is ArrayType arr)
            {
                _blockStructIds[st] = structType;   // so SpvType(st) reuses the decorated block struct
                varType = _m.TypeArray(structType, _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)arr.Length));
            }
            else if (globalType is RuntimeArrayType)
            {
                _blockStructIds[st] = structType;
                DeclareRuntimeDescriptorArray();
                varType = _m.TypeRuntimeArray(structType);
            }
            uint ptr = _m.TypePointer(storage, varType);
            uint var = _m.GlobalVariable(ptr, storage);
            _m.AddName(var, block.InstanceName ?? "");
            if (!isPushConstant && !isShared && !isShaderRecord)   // push-constant/shared/shader-record blocks are not descriptor-bound
            {
                if (LayoutInt(block.Qualifiers, "binding") is null)
                    throw new GlslParseException($"'binding' : uniform/buffer blocks require layout(binding=X)", block.Line);
                _m.Decorate(var, Decoration.DescriptorSet, (uint)(LayoutInt(block.Qualifiers, "set") ?? 0));
                _m.Decorate(var, Decoration.Binding, (uint)(LayoutInt(block.Qualifiers, "binding") ?? 0));
            }
            // glslang applies the block's memory qualifiers (readonly/writeonly/coherent/volatile/restrict) to
            // the buffer variable itself, not just its members (GlslangToSpv TranslateMemoryDecoration).
            if (block.Qualifiers.Has("readonly")) { _m.Decorate(var, Decoration.NonWritable); _readOnlyVars.Add(var); }
            // Uniform and push-constant storage is read-only; a store to either is invalid SPIR-V.
            if (!isBuffer && !isShared) _readOnlyVars.Add(var);
            // A single member may also be readonly while the block as a whole is writable.
            for (int mi = 0; mi < memberMemory.Count; mi++)
                if (memberMemory[mi].Contains(Decoration.NonWritable)) _readOnlyMembers.Add((var, mi));
            if (block.Qualifiers.Has("writeonly")) { _m.Decorate(var, Decoration.NonReadable); _writeOnlyVars.Add(var); }
            for (int mi = 0; mi < memberMemory.Count; mi++)
                if (memberMemory[mi].Contains(Decoration.NonReadable)) _writeOnlyMembers.Add((var, mi));
            foreach (var (word, decoration) in MemoryQualifiers)
                if (block.Qualifiers.Has(word) && !(_vmmPragma && decoration is Decoration.Coherent or Decoration.Volatile))
                    _m.Decorate(var, decoration);
            return new Global(globalType, storage, var, blockMem);
        }

        if (block.InstanceName is not null)
        {
            _pendingGlobals[block.InstanceName] = (globalType, BuildBlock);
            return;
        }
        // An anonymous block exposes its members by bare name, but the block itself still materializes
        // lazily at the first member reference, exactly like a named one.
        uint built = 0;
        uint Var() => built == 0 ? built = BuildBlock().VarId : built;
        RegisterAnonymousMembers(st, Var, storage, trivialSymbol: !isBuffer, mem: blockMem);
        _pendingBlockBuilds[block] = Var;
    }

    static MemQual BlockMemQual(Qualifiers q) => q.Has("volatile") ? MemQual.Volatile : q.Has("coherent") ? MemQual.Coherent : MemQual.None;

    // Members of an anonymous block are addressable by their bare names (`uniform M { mat4 m; };` -> `m`).
    void RegisterAnonymousMembers(StructType st, Func<uint> var, StorageClass storage, bool trivialSymbol = false, MemQual mem = MemQual.None)
    {
        for (int i = 0; i < st.Fields.Count; i++)
        {
            if (_blockMembers.ContainsKey(st.Fields[i].Name) || GlobalExists(st.Fields[i].Name))
                throw new GlslParseException($"'{st.Fields[i].Name}' from anonymous block collides with an existing global");
            _blockMembers[st.Fields[i].Name] = new BlockMember(var, i, st.Fields[i].Type, storage, BlockRuleset(st), trivialSymbol, mem);
        }
    }

    // GL_NV_geometry_shader_passthrough redeclares gl_PerVertex as a PassthroughNV Input block whose
    // members map to their built-ins; it materializes with the other linker objects at end of translation.
    void EmitPassthroughPerVertex(InterfaceBlock block)
    {
        var members = block.Members
            .SelectMany(f => f.Declarators.Select(d => (d.Name, Type: ApplyArrayDims(ResolveTypeRef(f.Type), d.Array))))
            .ToList();
        uint built = 0;
        uint Build()
        {
            if (built != 0) return built;
            _m.AddCapability(Capability.GeometryShaderPassthroughNV);
            _m.AddExtension("SPV_NV_geometry_shader_passthrough");
            uint structType = _m.TypeStructUnique([.. members.Select(m => SpvType(m.Type))]);
            _m.AddName(structType, "gl_PerVertex");
            for (int i = 0; i < members.Count; i++) _m.AddMemberName(structType, i, members[i].Name);
            for (int i = 0; i < members.Count; i++) _m.MemberDecorate(structType, i, Decoration.BuiltIn, (uint)PerVertexBuiltIn(members[i].Name));
            _m.Decorate(structType, Decoration.Block);
            uint ptr = _m.TypePointer(StorageClass.Input, structType);
            built = _m.GlobalVariable(ptr, StorageClass.Input);
            _m.AddName(built, "");
            _m.Decorate(built, Decoration.PassthroughNV);
            _interface.Add(built);
            return built;
        }
        _pendingBlockBuilds[block] = Build;
    }

    void EmitInterfaceBlock(InterfaceBlock block, StorageClass storage)
    {
        // Flatten members, keeping each one's owning field so its interpolation qualifiers can be
        // applied per member (glslang forbids interpolation qualifiers on the block itself).
        var members = block.Members
            .SelectMany(f => f.Declarators.Select(d => (Field: f, d.Name, Type: ApplyArrayDims(ResolveTypeRef(f.Type), d.Array))))
            .ToList();
        var st = new StructType(block.BlockName, members.Select(m => (m.Name, m.Type)).ToList());

        // An integer fragment input cannot be interpolated: reject it whether or not the block is used.
        foreach (var m in members)
            if (storage == StorageClass.Input && stage == ShaderStage.Fragment && !m.Field.Qualifiers.Has("flat")
                && BaseScalar(m.Type) is Scalar.Int or Scalar.UInt)
                throw new GlslParseException($"integer fragment input '{block.BlockName}.{m.Name}' must be declared 'flat'");

        // A per-instance array (`out V {...} vs[N];`, or a geometry/tess input block `in B {...} b[];`
        // sized to the incoming vertex count) wraps the block struct in an array.
        int instArray = block.InstanceArray.Count > 0
            ? (block.InstanceArray[0] is { } dim
                ? (EvalConst(dim) is CScalar cs ? (int)cs.I : throw new GlslParseException($"block '{block.BlockName}' instance array needs a constant size"))
                : storage == StorageClass.Input ? _inVertexCount : _outVertexCount)
            : -1;
        GlslType instType = instArray >= 0 ? new ArrayType(st, instArray) : st;

        // As with a uniform block, glslang materializes the struct + variable lazily, at first reference.
        Global BuildBlock()
        {
            uint structType = _m.TypeStructUnique([.. st.Fields.Select(f => SpvType(f.Type))]);
            _m.AddName(structType, block.BlockName);
            for (int i = 0; i < st.Fields.Count; i++) _m.AddMemberName(structType, i, st.Fields[i].Name);
            for (int i = 0; i < members.Count; i++)
            {
                var q = members[i].Field.Qualifiers;
                if (LayoutInt(q, "location") is int mloc) _m.MemberDecorate(structType, i, Decoration.Location, (uint)mloc);
                if (q.Has("flat")) _m.MemberDecorate(structType, i, Decoration.Flat);
                if (q.Has("noperspective")) _m.MemberDecorate(structType, i, Decoration.NoPerspective);
                if (q.Has("centroid")) _m.MemberDecorate(structType, i, Decoration.Centroid);
                if (q.Has("sample")) { _m.AddCapability(Capability.SampleRateShading); _m.MemberDecorate(structType, i, Decoration.Sample); }
            }
            _m.Decorate(structType, Decoration.Block);

            uint varType = instArray >= 0
                ? _m.TypeArray(structType, _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)instArray))
                : structType;
            uint ptr = _m.TypePointer(storage, varType);
            uint var = _m.GlobalVariable(ptr, storage);
            _m.AddName(var, block.InstanceName ?? "");
            if (LayoutInt(block.Qualifiers, "location") is int location)
                _m.Decorate(var, Decoration.Location, (uint)location);
            // A user-defined input/output block needs a location on the block or on every member; without
            // one the module is invalid ("Member index N is missing a location assignment"). glslang errors
            // here rather than emitting it, so match that instead of producing an invalid module.
            else if (!AutoMapLocations && !members.All(mem => LayoutInt(mem.Field.Qualifiers, "location") is int))
                throw new GlslParseException("'location' : SPIR-V requires location for user input/output", block.Line);
            if (block.Qualifiers.Layout.Any(i => i.Name == "passthrough"))
            {
                _m.AddCapability(Capability.GeometryShaderPassthroughNV);
                _m.AddExtension("SPV_NV_geometry_shader_passthrough");
                _m.Decorate(var, Decoration.PassthroughNV);
            }
            _interface.Add(var);
            return new Global(instType, storage, var);
        }

        if (block.InstanceName is not null)
        {
            _pendingGlobals[block.InstanceName] = (instType, BuildBlock);
            return;
        }
        uint built = 0;
        uint Var() => built == 0 ? built = BuildBlock().VarId : built;
        RegisterAnonymousMembers(st, Var, storage);
        _pendingBlockBuilds[block] = Var;
    }

    Value LowerMember(Member m)
    {
        // A member or swizzle of a constant folds to a constant (glslang folds it in the AST), so e.g.
        // `gl_WorkGroupSize.x` is a literal rather than an extract from the composite.
        if (EvalConst(m) is { } folded) return Materialize(folded);
        if (IsPerVertexPosition(m)) { var a = ResolveAccess(m); return Load(a.Type, a.Pointer, a.Layout); }
        // A run of member/single-component-swizzle accesses over an r-value (e.g. `P(...).a.x`) is ONE
        // OpCompositeExtract with a flat index path, not an access chain.
        if (RValueChain(m) is (Expr baseE, var idxs)) return RValueExtract(baseE, idxs, InferType(m));
        if (InferType(m.Target) is StructType or BufferReferenceType)
        {
            var a = ResolveAccess(m);
            return FromStored(LoadFrom(a), InferType(m));   // a block's bool member loads as uint
        }
        return LowerSwizzle(m);
    }

    // Collects a run of struct-member / single-component-swizzle accesses over a non-addressable (r-value)
    // composite into a flat index path (base outward). Each index carries whether it is a swizzle
    // component (glslang types member indices signed, swizzle components unsigned). Null if e is not one.
    (Expr Base, List<(int Idx, bool Unsigned)> Indices)? RValueChain(Expr e)
    {
        if (e is not Member) return null;
        // Only a chain rooted at an r-value STRUCT (a constructor or struct-returning call) collapses to a
        // flat OpCompositeExtract; a swizzle of an r-value vector is left to LowerSwizzle. Check the root
        // structurally before touching InferType (undefined for some addressable roots such as gl_in).
        Expr root = e;
        while (root is Member mm) root = mm.Target;
        return (root is Call or Binary or Conditional or Method or Unary) && InferType(root) is StructType ? CollectRValueChain(e) : null;
    }

    (Expr Base, List<(int Idx, bool Unsigned)> Indices)? CollectRValueChain(Expr e)
    {
        if (e is not Member m) return (e, []);
        (int, bool) here;
        if (InferType(m.Target) is StructType st) here = (IndexOfField(st, m.Name), false);
        else if (IsSwizzleName(m.Name) && m.Name.Length == 1) here = (SwizzleIndex(m.Name[0]), true);
        else return null;   // a multi-component swizzle is a shuffle, not a flat extract
        if (CollectRValueChain(m.Target) is (Expr b, var list)) { list.Add(here); return (b, list); }
        return null;
    }

    // Emits the collapsed OpCompositeExtract for an r-value chain; glslang creates each index constant
    // (as a side effect of building its access chain) before emitting the extract with literal indices.
    Value RValueExtract(Expr baseE, List<(int Idx, bool Unsigned)> indices, GlslType resultType)
    {
        var baseVal = LowerExpr(baseE);
        foreach (var (idx, unsigned) in indices)
            if (unsigned) _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)idx); else MemberIndexConst(idx);
        var inst = InstrPool.Rent(Op.CompositeExtract, SpvType(resultType), _m.AllocId()).Add(baseVal.Id);
        foreach (var (idx, _) in indices) inst.Add((uint)idx);
        _code.Add(inst);
        return new Value(inst.ResultId, resultType, false);
    }

    // Reading/writing gl_PointSize pulls in the stage's point-size capability.
    void NeedPointSizeCap()
    {
        if (stage == ShaderStage.Geometry) _m.AddCapability(Capability.GeometryPointSize);
        else if (stage is ShaderStage.TessControl or ShaderStage.TessEvaluation) _m.AddCapability(Capability.TessellationPointSize);
    }

    static bool IsPerVertexPosition(Member m) => m is { Name: "gl_Position", Target: Index { Target: Ident { Name: "gl_in" or "gl_out" } } };
    static bool IsSwizzleName(string s) => s.Length is >= 1 and <= 4 && s.All(c => "xyzwrgbastpq".Contains(c));

    // Rejects a swizzle whose components fall outside the vector, and (on the left of an assignment) a
    // write mask that names the same component twice.
    static void ValidateSwizzle(VectorType vec, string name, bool writeMask)
    {
        var seen = new HashSet<int>();
        foreach (char c in name)
        {
            int idx = SwizzleIndex(c);
            if (idx >= vec.Size)
                throw new GlslParseException($"'{Types.Name(vec)}' has no component '{c}'");
            if (writeMask && !seen.Add(idx))
                throw new GlslParseException($"swizzle component '{c}' is written more than once in an assignment");
        }
    }

    // ---- statements ----

    // Lowers a statement list, dropping unreachable statements after a terminator. glslang lowers such
    // dead code into a block it strips, so the constants and ids it creates survive but the instructions
    // do not; we mirror that by lowering the statement, then truncating the instructions it appended.
    void LowerStatements(IReadOnlyList<Stmt> statements)
    {
        foreach (var s in statements)
        {
            if (!_terminated) { LowerStatement(s); continue; }
            int mark = _code.Count;
            _terminated = false;
            LowerStatement(s);
            _terminated = true;
            _code.RemoveRange(mark, _code.Count - mark);
        }
    }

    int _lowerLine;   // the source line currently being lowered, for attributing semantic errors
    int _exprDepth;   // AST recursion depth guard (see Parser.MaxNestingDepth)
    Block? _currentBody;   // the function body being lowered, for implicit array sizing

    // Runs a top-level declaration's emission, attributing any line-less error to that declaration.
    void AtDecl(Decl decl, Action body)
    {
        if (decl.Line > 0) _lowerLine = decl.Line;
        try { body(); }
        catch (GlslParseException ex) { throw ex.WithLine(_lowerLine); }
    }

    static GlslParseException MulMismatch(GlslType l, GlslType r) => new($"cannot multiply '{Types.Name(l)}' by '{Types.Name(r)}': inner dimensions do not match");

    // Lowers an if/loop/ternary condition, which must be a scalar boolean.
    Value LowerCondition(Expr e)
    {
        var v = LowerExpr(e);
        if (v.Type is not ScalarType { Kind: Scalar.Bool })
            throw new GlslParseException($"a condition must be a boolean expression, not '{Types.Name(v.Type)}'");
        return v;
    }

    void LowerStatement(Stmt stmt)
    {
        if (stmt.Line > 0) _lowerLine = stmt.Line;
        try { LowerStatementCore(stmt); }
        catch (GlslParseException ex) { throw ex.WithLine(_lowerLine); }
    }

    void LowerStatementCore(Stmt stmt)
    {
        switch (stmt)
        {
            case Block b: InScope(() => LowerStatements(b.Statements)); break;
            case VarDeclStmt decl: LowerLocalDecl(decl); break;
            case ExprStmt e: LowerDiscardedExpr(e.Expression); break;
            case IfStmt s: LowerIf(s); break;
            case ForStmt s: LowerFor(s); break;
            case WhileStmt s: LowerWhile(s); break;
            case DoWhileStmt s: LowerDoWhile(s); break;
            case SwitchStmt s: LowerSwitch(s); break;
            case ReturnStmt s: LowerReturn(s); break;
            case JumpStmt s: LowerJump(s); break;
            default: throw new GlslParseException($"statement '{stmt.GetType().Name}' is not lowered yet");
        }
    }

    // Declaration-time shape check for a non-void function: any bare `return;` is an error at its own
    // line, and a body with no value-return at all does not return a value (glslang rejects both,
    // uncalled functions included). Path completeness is NOT checked here - that stays the lowering
    // path's concern - so a body with some value-return passes this check (no false rejects).
    void CheckFunctionReturns(FunctionDef f)
    {
        bool sawValueReturn = false;
        int? voidReturnLine = null;
        void Walk(Stmt s)
        {
            switch (s)
            {
                case ReturnStmt { Value: null } r: voidReturnLine ??= r.Line; break;
                case ReturnStmt: sawValueReturn = true; break;
                case Block b: foreach (var st in b.Statements) Walk(st); break;
                case IfStmt i: Walk(i.Then); if (i.Else is { } e) Walk(e); break;
                case ForStmt fo: Walk(fo.Body); break;
                case WhileStmt w: Walk(w.Body); break;
                case DoWhileStmt d: Walk(d.Body); break;
                case SwitchStmt sw: foreach (var c in sw.Cases) foreach (var st in c.Body) Walk(st); break;
            }
        }
        foreach (var st in f.Body.Statements) Walk(st);
        if (voidReturnLine is int line)
            throw new GlslParseException("a non-void function must return a value", line);
        if (!sawValueReturn)
            throw new GlslParseException($"function does not return a value: {f.Name}", f.Line);
    }

    void LowerReturn(ReturnStmt s)
    {
        if (s.Value is null && _currentReturnType is not ScalarType { Kind: Scalar.Void })
            throw new GlslParseException("a non-void function must return a value");
        if (s.Value is not null && _currentReturnType is ScalarType { Kind: Scalar.Void })
            throw new GlslParseException("void function cannot return a value");
        if (s.Value is null) _code.Add(InstrPool.Rent(Op.Return));
        else
        {
            var value = LowerCoerced(s.Value, _currentReturnType, "return");
            // A block member (Offset-decorated struct, bools stored as uint) is a different SPIR-V type than
            // the function's plain return type: glslang copies it through an unnamed Function temporary with
            // multiTypeStore and returns the reload.
            if (SpvType(value.Type, value.Layout) != SpvType(_currentReturnType))
            {
                uint ptrType = _m.TypePointer(StorageClass.Function, SpvType(_currentReturnType));
                uint temp = _m.AllocId();
                _vars.Add(InstrPool.Rent(Op.Variable, ptrType, temp).AddEnum(StorageClass.Function));
                MultiTypeStore(new Chain(temp, [], _currentReturnType, StorageClass.Function), value);
                value = Load(_currentReturnType, temp);
            }
            _code.Add(InstrPool.Rent(Op.ReturnValue).Add(value.Id));
        }
        _terminated = true;
        // glslang follows every explicit return with an unreachable "post-return" block (a fresh id).
        // The block is dead and stripped from the output, but the id was consumed.
        _m.AllocId();
    }

    void LowerJump(JumpStmt s)
    {
        switch (s.Keyword)
        {
            // break/continue also branch and then start an unreachable post-block (a stripped id).
            case "break" when _loops.Count == 0: throw new GlslParseException("'break' is only valid inside a loop or switch");
            case "continue" when _loops.Count == 0: throw new GlslParseException("'continue' is only valid inside a loop");
            case "break": Branch(_loops.Peek().Merge); _m.AllocId(); break;
            case "continue": Branch(_loops.Peek().Continue); _m.AllocId(); break;
            // glslang follows OpKill with an unreachable "post-discard" block (a fresh, stripped id).
            case "discard" when stage != ShaderStage.Fragment: throw new GlslParseException("'discard' is only valid in a fragment shader");
            case "discard": _code.Add(InstrPool.Rent(Op.Kill)); _terminated = true; _m.AllocId(); break;
            case "demote":   // demote to a helper invocation; execution continues, so this is not a terminator.
                _m.AddCapability(Capability.DemoteToHelperInvocation);
                _m.AddExtension("SPV_EXT_demote_to_helper_invocation");
                _code.Add(InstrPool.Rent(Op.DemoteToHelperInvocationEXT));
                break;
            case "terminateInvocation":
                _m.AddExtension("SPV_KHR_terminate_invocation");
                _code.Add(InstrPool.Rent(Op.TerminateInvocation));
                _terminated = true; _m.AllocId();
                break;
            case "ignoreIntersectionEXT":
                _code.Add(InstrPool.Rent(Op.IgnoreIntersectionKHR)); _terminated = true; _m.AllocId(); break;
            case "terminateRayEXT":
                _code.Add(InstrPool.Rent(Op.TerminateRayKHR)); _terminated = true; _m.AllocId(); break;
            default: throw new GlslParseException($"jump '{s.Keyword}' is not lowered yet");
        }
    }

    void LowerIf(IfStmt s)
    {
        // Mirror glslang's Builder::If: allocate the then and merge blocks up front, but allocate the
        // else block only after the then-body is emitted, and insert the OpBranchConditional back into
        // the header (after OpSelectionMerge) at the end. This reproduces glslang's exact id order.
        var cond = LowerCondition(s.Cond);
        uint thenL = _m.AllocId();
        uint mergeL = _m.AllocId();

        SelectionMerge(mergeL, s.SelControl);
        int branchAt = _code.Count;   // the OpBranchConditional belongs here, after the merge instruction

        Label(thenL);
        LowerStatement(s.Then);
        Branch(mergeL);

        uint falseTarget = mergeL;
        // An `else { }` carries no statements, and glslang builds no else block for it: the false edge
        // goes straight to the merge, exactly as when there is no else clause at all.
        if (s.Else is not null and not Block { Statements.Count: 0 })
        {
            uint elseL = _m.AllocId();
            falseTarget = elseL;
            Label(elseL);
            LowerStatement(s.Else);
            Branch(mergeL);
        }

        _code.Insert(branchAt, InstrPool.Rent(Op.BranchConditional).Add(cond.Id, thenL, falseTarget));
        Label(mergeL);
    }

    // A local name is visible only within its block; declaring one shadows (does not overwrite) any
    // outer binding, and the outer binding is restored on exit. Without this, `int i=5; { int i; }`
    // or nested loops reusing an index silently read/modify the wrong variable.
    void InScope(Action body)
    {
        var savedLocals = new Dictionary<string, (GlslType, uint)>(_locals);
        var savedPendingLocals = new Dictionary<string, (GlslType, bool)>(_pendingLocals);
        var savedPendingPrivates = new Dictionary<string, GlslType>(_pendingPrivates);
        var savedConstants = new Dictionary<string, Value>(_constants);
        var savedConstValues = new Dictionary<string, CVal>(_constValues);
        _scopeNames.Push([]);
        body();
        _scopeNames.Pop();
        // A local declared in an OUTER scope but first materialized inside this one (e.g. `vec3 c;` at
        // function scope, assigned inside an if-branch) must keep its materialization - it is the same
        // variable, so it must not be re-created in a sibling scope.
        var keepMaterialized = _locals
            .Where(kv => savedPendingLocals.ContainsKey(kv.Key) && !savedLocals.ContainsKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        Restore(_locals, savedLocals);
        Restore(_pendingLocals, savedPendingLocals);
        Restore(_pendingPrivates, savedPendingPrivates);
        foreach (var kv in keepMaterialized) { _locals[kv.Key] = kv.Value; _pendingLocals.Remove(kv.Key); _pendingPrivates.Remove(kv.Key); }

        // Only a const DECLARED in this scope is scoped. A global const or specialization constant that
        // merely happened to be first referenced here keeps its materialization: its pending entry has
        // already been consumed, so dropping it would leave the name unresolvable.
        var keepConstants = _constants
            .Where(kv => !savedConstants.ContainsKey(kv.Key)
                && (savedConstValues.ContainsKey(kv.Key) || _specConstNames.Contains(kv.Key)))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        Restore(_constants, savedConstants);
        Restore(_constValues, savedConstValues);
        foreach (var kv in keepConstants) _constants[kv.Key] = kv.Value;
    }

    static void Restore<T>(Dictionary<string, T> dict, Dictionary<string, T> saved)
    {
        dict.Clear();
        foreach (var kv in saved) dict[kv.Key] = kv.Value;
    }

    void LowerFor(ForStmt s) => InScope(() => LowerForScoped(s));   // the init variable is scoped to the loop

    void LowerForScoped(ForStmt s)
    {
        if (s.Init is not null) LowerStatement(s.Init);
        // glslang's makeNewLoop allocates head, body, merge, continue together, then the test block last
        // and only when the loop actually has a test: without one the header branches straight to the body.
        uint header = _m.AllocId(), body = _m.AllocId(), merge = _m.AllocId(), cont = _m.AllocId();

        Branch(header);
        Label(header);
        LoopMerge(merge, cont, s.LoopControl, s.DepLength);
        if (s.Cond is not null)
        {
            uint check = _m.AllocId();
            Branch(check);
            Label(check);
            BranchConditional(LowerCondition(s.Cond).Id, body, merge);
        }
        else Branch(body);
        Label(body);
        _loops.Push((merge, cont));
        LowerStatement(s.Body);
        _loops.Pop();
        Branch(cont);
        Label(cont);
        if (s.Update is not null) LowerExpr(s.Update);
        Branch(header);
        Label(merge);
    }

    void LowerWhile(WhileStmt s)
    {
        uint header = _m.AllocId(), body = _m.AllocId(), merge = _m.AllocId(), cont = _m.AllocId(), check = _m.AllocId();
        Branch(header);
        Label(header);
        LoopMerge(merge, cont, s.LoopControl, s.DepLength);
        Branch(check);
        Label(check);
        BranchConditional(LowerCondition(s.Cond).Id, body, merge);
        Label(body);
        _loops.Push((merge, cont));
        LowerStatement(s.Body);
        _loops.Pop();
        Branch(cont);
        Label(cont);
        Branch(header);
        Label(merge);
    }

    void LowerDoWhile(DoWhileStmt s)
    {
        uint header = _m.AllocId(), body = _m.AllocId(), merge = _m.AllocId(), cont = _m.AllocId();
        Branch(header);
        Label(header);
        LoopMerge(merge, cont, s.LoopControl, s.DepLength);
        Branch(body);
        Label(body);
        _loops.Push((merge, cont));
        LowerStatement(s.Body);
        _loops.Pop();
        Branch(cont);
        Label(cont);
        BranchConditional(LowerCondition(s.Cond).Id, header, merge);
        Label(merge);
    }

    // An expression whose value is thrown away. glslang's access chain only becomes instructions when
    // something loads it, so a bare l-value path emits nothing but the side effects of its indices.
    void LowerDiscardedExpr(Expr e)
    {
        if (IsLValuePath(e)) BuildAccess(e);
        else LowerExpr(e);
    }

    static bool IsLValuePath(Expr e) => e switch
    {
        Ident => true,
        Member m => IsLValuePath(m.Target),
        Index ix => IsLValuePath(ix.Target),
        _ => false,
    };

    void LowerSwitch(SwitchStmt s)
    {
        // A switch with no cases at all is not a construct in glslang's AST: only the selector's side
        // effects survive, exactly as for a bare expression statement.
        if (s.Cases.Count == 0) { LowerDiscardedExpr(s.Value); return; }
        var selector = LowerExpr(s.Value);
        if (selector.Type is not ScalarType { Kind: Scalar.Int or Scalar.UInt })
            throw new GlslParseException($"a switch selector must be an integer, not '{Types.Name(selector.Type)}'");
        var defaults = s.Cases.Where(c => c.Label is null).ToList();
        if (defaults.Count > 1)
            throw new GlslParseException("a switch statement can have at most one default label", defaults[1].Line);
        var seenLabels = new HashSet<uint>();
        foreach (var sc in s.Cases)
            if (sc.Label is not null && !seenLabels.Add(CaseValue(sc.Label)))
                throw new GlslParseException("duplicate case label in switch statement", sc.Label.Line);

        // glslang emits one block per SEGMENT (a group of case labels sharing a body): an empty
        // fall-through case (`case 2:` with no statements) shares the following case's segment. The
        // segment blocks are allocated first, in order, then the merge block.
        int[] segOf = new int[s.Cases.Count];
        int seg = 0;
        for (int i = 0; i < s.Cases.Count; i++)
        {
            segOf[i] = seg;
            if (s.Cases[i].Body.Count > 0) seg++;
        }
        int segCount = s.Cases.Count == 0 ? 0 : segOf.Max() + 1;

        var segLabels = Enumerable.Range(0, segCount).Select(_ => _m.AllocId()).ToList();
        uint merge = _m.AllocId();

        int defaultSeg = -1;
        var pairs = new List<uint>();
        for (int i = 0; i < s.Cases.Count; i++)
        {
            if (s.Cases[i].Label is null) { defaultSeg = segOf[i]; continue; }
            pairs.Add(CaseValue(s.Cases[i].Label!));
            pairs.Add(segLabels[segOf[i]]);
        }
        uint defaultLabel = defaultSeg >= 0 ? segLabels[defaultSeg] : merge;

        SelectionMerge(merge);
        _code.Add(InstrPool.Rent(Op.Switch).Add(selector.Id, defaultLabel).Add([.. pairs]));
        _terminated = true;

        // break targets the merge; continue passes through to the enclosing loop, if any.
        uint continueTarget = _loops.Count > 0 ? _loops.Peek().Continue : 0;
        _loops.Push((merge, continueTarget));

        // Build the segment blocks contiguously in segment-index order so their ids (and the phantom
        // ids that breaks burn) allocate exactly as glslang's source-order traversal does. A fall-through
        // segment branches to the next segment by index; the last falls into the merge.
        int bodyStart = _code.Count;
        var ranges = new (int Start, int Len)[segCount];
        for (int block = 0; block < segCount; block++)
        {
            int start = _code.Count;
            Label(segLabels[block]);
            for (int i = 0; i < s.Cases.Count; i++)
                if (segOf[i] == block)
                    foreach (var stmt in s.Cases[i].Body) LowerStatement(stmt);
            Branch(block + 1 < segCount ? segLabels[block + 1] : merge);
            ranges[block] = (start, _code.Count - start);
        }
        // glslang's endSwitch runs a final implicit break on the trailing (non-terminated) block,
        // burning one more id; mirror it so downstream ids stay aligned.
        _m.AllocId();
        _loops.Pop();

        // glslang dumps blocks in readable (dominator) order: the default segment first, then the case
        // segments in source order, with the merge last. Reorder the built ranges to match.
        var chunks = new List<Instruction>[segCount];
        for (int block = 0; block < segCount; block++)
            chunks[block] = _code.GetRange(ranges[block].Start, ranges[block].Len);
        if (segCount > 0) _code.RemoveRange(bodyStart, _code.Count - bodyStart);

        var emitOrder = new List<int>();
        var seen = new HashSet<int>();
        if (defaultSeg >= 0) { emitOrder.Add(defaultSeg); seen.Add(defaultSeg); }
        for (int i = 0; i < s.Cases.Count; i++)
            if (s.Cases[i].Label is not null && seen.Add(segOf[i])) emitOrder.Add(segOf[i]);
        foreach (int block in emitOrder) _code.AddRange(chunks[block]);

        Label(merge);
    }

    static uint CaseValue(Expr label) => label switch
    {
        Literal lit => ParseIntLiteral(lit.Text),
        Unary { Op: "-", Operand: Literal lit } => unchecked((uint)-(int)ParseIntLiteral(lit.Text)),
        _ => throw new GlslParseException("switch case label must be an integer constant"),
    };

    // ---- basic-block helpers ----

    void Label(uint id) { _code.Add(InstrPool.Rent(Op.Label, resultId: id)); _currentBlock = id; _terminated = false; }
    void Branch(uint target) { if (_terminated) return; _code.Add(InstrPool.Rent(Op.Branch).Add(target)); _terminated = true; }
    void BranchConditional(uint cond, uint ifTrue, uint ifFalse) { _code.Add(InstrPool.Rent(Op.BranchConditional).Add(cond, ifTrue, ifFalse)); _terminated = true; }
    void SelectionMerge(uint merge, uint control = 0) => _code.Add(InstrPool.Rent(Op.SelectionMerge).Add(merge, control));
    void LoopMerge(uint merge, uint cont, uint control = 0, uint depLength = 0)
    {
        var instr = InstrPool.Rent(Op.LoopMerge).Add(merge, cont, control);
        if ((control & 8) != 0) instr.Add(depLength);   // DependencyLength carries an operand
        _code.Add(instr);
    }

    void LowerLocalDecl(VarDeclStmt decl)
    {
        foreach (var d in decl.Declarators) RejectReservedName(d.Name);
        var baseType = ResolveTypeRef(decl.Type);
        if (baseType is ScalarType { Kind: Scalar.Void })
            throw new GlslParseException("a variable cannot have type 'void'");
        // A rayQueryEXT object is lowered by glslang to a Private module-scope variable (in the entry-point
        // interface), materialized on FIRST REFERENCE - a used one's ids come after whatever the
        // declarations before its first use allocate (probed), and an unreferenced one emits nothing at
        // all (no variable, type, capability or extension). Deferral rides the same machinery as plain
        // uninitialized locals (ResolveLocalVar), with a Private-storage declaration on materialization.
        if (baseType is RayQueryType)
        {
            foreach (var d in decl.Declarators)
                _pendingPrivates[d.Name] = baseType;
            return;
        }
        // Opaque types (samplers/images) are handles bound through the descriptor set; they cannot be
        // declared as function-local variables.
        if (IsOpaque(baseType))
            throw new GlslParseException($"'{decl.Type.Name}' is an opaque type and cannot be a local variable");
        bool isConst = decl.Qualifiers.Has("const");
        foreach (var d in decl.Declarators)
        {
            if (_scopeNames.Count > 0 && !_scopeNames.Peek().Add(d.Name))
                throw new GlslParseException($"'{d.Name}' is already declared in this scope");
            if (isConst && d.Init is null)
                throw new GlslParseException($"variables with qualifier 'const' must be initialized: '{d.Name}'");
            // A local `const` is a compile-time constant (usable as an array size, in other const
            // expressions, etc.). When its initializer folds, register it for folding instead of
            // emitting a runtime variable, matching glslang.
            if (isConst && d.Init is not null && EvalConst(d.Init) is CVal cval)
            {
                if (ApplyArrayDims(baseType, d.Array) is ScalarType { Kind: var k } && cval is CScalar cs && cs.Kind != k)
                    cval = ConvertConst(cs, k);
                // Register for folding; the OpConstant is materialized lazily on first use (glslang emits
                // it only when referenced, so a const that only feeds other folded consts never appears).
                _constValues[d.Name] = cval;
                continue;
            }
            var type = ApplyArrayDims(baseType, d.Array);
            // GLSL implicit sizing for an unsized local with NO initializer (an initialized one is already
            // sized from it): the size is the largest constant index used on the variable, plus one. A
            // runtime array cannot be instantiated as an OpVariable, so leaving it unsized is invalid.
            if (type is RuntimeArrayType lra && d.Init is null && _currentBody is not null
                && MaxConstIndexOf(_currentBody, d.Name) is int mx)
                type = SizedArray(lra.Element, mx + 1);
            // An unsized local WITH an initializer takes its size from it (`float a[] = float[](1,2)`).
            if (type is RuntimeArrayType ira && d.Init is not null)
            {
                int length = d.Init is Call { IsArray: true, ArrayDims: null } brace
                    ? brace.Args.Count
                    : InferType(d.Init) is ArrayType sized ? sized.Length : 0;
                if (length > 0) type = SizedArray(ira.Element, length);
            }
            // An initialized declaration creates the variable now (before evaluating the initializer,
            // matching glslang); an uninitialized one is deferred until first use.
            if (d.Init is null)
            {
                _pendingLocals[d.Name] = (type, DeclRelaxed(type, decl.Qualifiers));
                continue;
            }
            uint varId = DeclareLocal(d.Name, type, DeclRelaxed(type, decl.Qualifiers));
            var init = LowerCoerced(d.Init, type, $"initializing '{d.Name}'");
            MultiTypeStore(new Chain(varId, [], type, StorageClass.Function), init);
        }
    }

    // Materializes an uninitialized local's OpVariable on first reference; returns null if unknown.
    (GlslType Type, uint VarId)? ResolveLocalVar(string name)
    {
        if (_locals.TryGetValue(name, out var local)) return local;
        if (_pendingLocals.TryGetValue(name, out var pending))
        {
            _pendingLocals.Remove(name);
            return (pending.Type, DeclareLocal(name, pending.Type, pending.Relaxed));   // DeclareLocal records into _locals
        }
        if (_pendingPrivates.TryGetValue(name, out var privateType))
        {
            // glslang materializes a rayQuery local on first reference, at module scope in Private,
            // listed in the entry-point interface.
            _pendingPrivates.Remove(name);
            uint ptr = _m.TypePointer(StorageClass.Private, SpvType(privateType));
            uint varId = _m.GlobalVariable(ptr, StorageClass.Private);
            _m.AddName(varId, name);
            _interface.Add(varId);
            _locals[name] = (privateType, varId);
            return (privateType, varId);
        }
        return null;
    }

    bool LocalExists(string name) => _locals.ContainsKey(name) || _pendingLocals.ContainsKey(name) || _pendingPrivates.ContainsKey(name);

    GlslType ApplyArrayDims(GlslType baseType, IReadOnlyList<Expr?> dims)
    {
        var type = baseType;
        for (int i = dims.Count - 1; i >= 0; i--)
            type = dims[i] switch
            {
                // Only the outermost dimension of an array of arrays may be implicitly sized.
                null when i > 0 => throw new GlslParseException("only outermost dimension of an array of arrays can be implicitly sized"),
                null => new RuntimeArrayType(type),
                Literal lit => SizedArray(type, unchecked((long)ParseInt64Literal(lit.Text))),
                Expr e when EvalConst(e) is CScalar { Kind: Scalar.Int or Scalar.UInt } n => SizedArray(type, n.I),
                // A specialization constant (or an arithmetic expression over them, e.g. `arr[N * M + 1]`)
                // fixes the length only at pipeline creation: it lowers to an OpSpecConstant / OpSpecConstantOp.
                Expr e when UsesSpecConstant(e) && IsSpecConstExpr(e) => new ArrayType(type, 0, e),
                _ => throw new GlslParseException("array length must be a constant integer expression"),
            };
        return type;
    }

    // The length is validated as a 64-bit value: truncating to int first would turn an out-of-range size
    // into a plausible small one (4294967297 would become 1).
    static ArrayType SizedArray(GlslType element, long length) =>
        length > 0 && length <= int.MaxValue
            ? new ArrayType(element, (int)length)
            : throw new GlslParseException($"array size must be greater than zero and at most {int.MaxValue}, but is {length}");

    uint DeclareLocal(string name, GlslType type, bool relaxed = false)
    {
        // A buffer_reference local (or array of them) is a full type use, not a member's forward reference:
        // glslang defines the pointee struct with its names and decorations before the variable's own.
        string? reference = (type is ArrayType { Element: BufferReferenceType ae } ? ae : type as BufferReferenceType)?.Name;
        if (reference is not null) BufferRefDefine(reference);
        uint ptrType = _m.TypePointer(StorageClass.Function, SpvType(type));
        uint varId = _m.AllocId();
        _vars.Add(InstrPool.Rent(Op.Variable, ptrType, varId).AddEnum(StorageClass.Function));
        _m.AddName(varId, name);   // glslang names user locals; the name is added at declaration order
        // A Function variable holding PhysicalStorageBuffer pointers must say whether they may alias; glslang
        // defaults to AliasedPointer.
        if (reference is not null) _m.Decorate(varId, Decoration.AliasedPointer);
        if (relaxed) Relax(varId);
        _locals[name] = (type, varId);
        return varId;
    }

    // ---- expressions ----

    Value LowerExpr(Expr expr)
    {
        if (expr.Line > 0) _lowerLine = expr.Line;
        if (++_exprDepth > Parser.MaxNestingDepth) { _exprDepth--; throw new GlslParseException($"expression nesting is too deep (limit {Parser.MaxNestingDepth})", _lowerLine); }
        try { return LowerExprCore(expr); }
        finally { _exprDepth--; }
    }

    Value LowerExprCore(Expr expr) => expr switch
    {
        Binary { Op: "," } seq => Sequence(seq),
        Binary { Op: "=" } a => LowerAssign(a),
        Binary b when IsCompoundAssign(b.Op) => LowerCompoundAssign(b),
        Binary b when b.Op is "&&" or "||" => LowerShortCircuit(b),
        // A constant operation folds to a constant (glslang folds it in the AST), so `PI / 2.0` is one
        // OpConstant rather than an OpFDiv of two. The assignment forms above never reach here.
        Binary b when EvalConst(b) is { } folded => Materialize(folded),
        // The same operation over a specialization constant cannot fold, but it still is not runtime
        // code: it stays specializable as an OpSpecConstantOp.
        Binary b when UsesSpecConstant(b) && IsSpecConstExpr(b) => SpecConstantExpr(b),
        Binary b => LowerBinary(b),
        // A unary on a constant folds too, so `-vec2(2.0, 0.0)` is one constant, not an OpFNegate.
        Unary u when EvalConst(u) is { } folded => Materialize(folded),
        Unary u when UsesSpecConstant(u) && IsSpecConstExpr(u) => SpecConstantExpr(u),
        Unary u => LowerUnary(u),
        Conditional c when UsesSpecConstant(c) && IsSpecConstExpr(c) => SpecConstantExpr(c),
        Conditional c => LowerConditional(c),
        Literal lit => LowerLiteral(lit),
        Ident id => LowerLoad(id),
        Member m => LowerMember(m),
        Index ix => LowerIndex(ix),
        Method meth => LowerMethod(meth),
        Call call when Types.IsTypeName(call.Name) && UsesSpecConstant(call) && IsSpecConstExpr(call) => SpecConstantExpr(call),
        Call call when Types.IsTypeName(call.Name) => LowerConstructor(call),
        Call { IsArray: true } call when _structs.ContainsKey(call.Name) => LowerArrayConstructor(call),
        Call call when _structs.ContainsKey(call.Name) => LowerStructConstructor(call),
        Call { IsArray: false } call when _bufferRefs.ContainsKey(call.Name) => LowerReferenceConstructor(call),
        Call call when CallsUserFunction(call) => LowerUserCall(call),
        // A built-in called on constants folds (glslang does it in the AST), so it is not a runtime op.
        Call call when EvalConstBuiltin(call) is { } folded => Materialize(folded),
        Call call => LowerBuiltinCall(call),
        _ => throw new GlslParseException($"expression '{expr.GetType().Name}' is not lowered yet"),
    };

    // GLSL.std.450 functions whose result type matches their (widest) argument, component-wise.
    static readonly Dictionary<string, GLSLstd450> ComponentwiseExt = new()
    {
        ["sin"] = GLSLstd450.Sin, ["cos"] = GLSLstd450.Cos, ["tan"] = GLSLstd450.Tan,
        ["asin"] = GLSLstd450.Asin, ["acos"] = GLSLstd450.Acos,
        ["exp"] = GLSLstd450.Exp, ["log"] = GLSLstd450.Log, ["exp2"] = GLSLstd450.Exp2, ["log2"] = GLSLstd450.Log2,
        ["sqrt"] = GLSLstd450.Sqrt, ["inversesqrt"] = GLSLstd450.InverseSqrt,
        ["floor"] = GLSLstd450.Floor, ["ceil"] = GLSLstd450.Ceil, ["fract"] = GLSLstd450.Fract,
        ["round"] = GLSLstd450.Round, ["trunc"] = GLSLstd450.Trunc,
        ["normalize"] = GLSLstd450.Normalize, ["radians"] = GLSLstd450.Radians, ["degrees"] = GLSLstd450.Degrees,
        ["pow"] = GLSLstd450.Pow, ["step"] = GLSLstd450.Step, ["smoothstep"] = GLSLstd450.SmoothStep,   // mix is handled separately (bool selector)
        ["sinh"] = GLSLstd450.Sinh, ["cosh"] = GLSLstd450.Cosh, ["tanh"] = GLSLstd450.Tanh,
        ["asinh"] = GLSLstd450.Asinh, ["acosh"] = GLSLstd450.Acosh, ["atanh"] = GLSLstd450.Atanh,
        ["roundEven"] = GLSLstd450.RoundEven, ["fma"] = GLSLstd450.Fma,
    };

    static readonly HashSet<string> AtomicOps = ["atomicAdd", "atomicAnd", "atomicOr", "atomicXor", "atomicMin", "atomicMax", "atomicExchange"];

    // Built-in function names a user function may not redefine (glslang: "can't redefine/overload built-in").
    static readonly HashSet<string> BuiltinFunctionNames =
    [
        "radians", "degrees", "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh", "asinh",
        "acosh", "atanh", "pow", "exp", "log", "exp2", "log2", "sqrt", "inversesqrt", "abs", "sign", "floor",
        "trunc", "round", "roundEven", "ceil", "fract", "mod", "modf", "min", "max", "clamp", "mix", "step",
        "smoothstep", "isnan", "isinf", "floatBitsToInt", "floatBitsToUint", "intBitsToFloat", "uintBitsToFloat",
        "fma", "frexp", "ldexp", "length", "distance", "dot", "cross", "normalize", "faceforward", "reflect",
        "refract", "matrixCompMult", "outerProduct", "transpose", "determinant", "inverse", "lessThan",
        "lessThanEqual", "greaterThan", "greaterThanEqual", "equal", "notEqual", "any", "all", "not",
        "uaddCarry", "usubBorrow", "umulExtended", "imulExtended", "bitfieldExtract", "bitfieldInsert",
        "bitfieldReverse", "bitCount", "findLSB", "findMSB", "packUnorm2x16", "packSnorm2x16", "packUnorm4x8",
        "packSnorm4x8", "unpackUnorm2x16", "unpackSnorm2x16", "unpackUnorm4x8", "unpackSnorm4x8", "packHalf2x16",
        "unpackHalf2x16", "packDouble2x32", "unpackDouble2x32", "packInt2x32", "unpackInt2x32", "packUint2x32",
        "unpackUint2x32", "dFdx", "dFdy", "fwidth", "dFdxCoarse", "dFdyCoarse", "dFdxFine", "dFdyFine",
        "fwidthCoarse", "fwidthFine", "interpolateAtCentroid", "interpolateAtSample", "interpolateAtOffset",
        "texture", "textureProj", "textureLod", "textureOffset", "texelFetch", "texelFetchOffset",
        "textureProjOffset", "textureLodOffset", "textureProjLod", "textureProjLodOffset", "textureGrad",
        "textureGradOffset", "textureProjGrad", "textureProjGradOffset", "textureGather", "textureGatherOffset",
        "textureGatherOffsets", "textureSize", "textureQueryLod", "textureQueryLevels", "textureSamples",
        "imageLoad", "imageStore", "imageSize", "imageAtomicAdd", "imageAtomicExchange", "subpassLoad",
        "atomicAdd", "atomicAnd", "atomicOr", "atomicXor", "atomicMin", "atomicMax", "atomicExchange",
        "atomicCompSwap", "atomicLoad", "atomicStore", "barrier", "memoryBarrier", "memoryBarrierBuffer",
        "memoryBarrierShared", "memoryBarrierImage", "groupMemoryBarrier", "EmitVertex", "EndPrimitive",
    ];


    static readonly HashSet<string> OutParamBuiltins = ["modf", "frexp", "uaddCarry", "usubBorrow", "umulExtended", "imulExtended"];
    static readonly HashSet<string> InterpolateBuiltins = ["interpolateAtCentroid", "interpolateAtSample", "interpolateAtOffset"];

    // Built-ins index their argument array positionally; a call with too few arguments would throw an
    // index-out-of-range, so convert that into a clean diagnostic instead of an internal crash.
    // Fixed argument counts for common built-ins (only those with a single arity; variable-arity ones like
    // atan, texture and the atomics are omitted and validated where they are lowered).
    static readonly Dictionary<string, int> BuiltinArity = new()
    {
        ["radians"] = 1, ["degrees"] = 1, ["sin"] = 1, ["cos"] = 1, ["tan"] = 1, ["asin"] = 1, ["acos"] = 1,
        ["sinh"] = 1, ["cosh"] = 1, ["tanh"] = 1, ["asinh"] = 1, ["acosh"] = 1, ["atanh"] = 1,
        ["exp"] = 1, ["log"] = 1, ["exp2"] = 1, ["log2"] = 1, ["sqrt"] = 1, ["inversesqrt"] = 1,
        ["floor"] = 1, ["ceil"] = 1, ["trunc"] = 1, ["round"] = 1, ["roundEven"] = 1, ["fract"] = 1,
        ["normalize"] = 1, ["length"] = 1, ["abs"] = 1, ["sign"] = 1,
        ["transpose"] = 1, ["determinant"] = 1, ["inverse"] = 1, ["isnan"] = 1, ["isinf"] = 1,
        ["dFdx"] = 1, ["dFdy"] = 1, ["fwidth"] = 1, ["dFdxCoarse"] = 1, ["dFdyCoarse"] = 1,
        ["dFdxFine"] = 1, ["dFdyFine"] = 1, ["fwidthCoarse"] = 1, ["fwidthFine"] = 1,
        ["pow"] = 2, ["mod"] = 2, ["min"] = 2, ["max"] = 2, ["step"] = 2, ["reflect"] = 2,
        ["distance"] = 2, ["dot"] = 2, ["cross"] = 2, ["outerProduct"] = 2, ["matrixCompMult"] = 2,
        ["clamp"] = 3, ["mix"] = 3, ["smoothstep"] = 3, ["faceforward"] = 3, ["refract"] = 3,
        ["textureLod"] = 3, ["textureGrad"] = 4,
    };

    // Built-ins whose arguments must be a numeric scalar or vector. Integer arguments are allowed because
    // GLSL implicitly converts them to floating-point; only bool, matrix, struct, etc. are rejected.
    static readonly HashSet<string> RequireNumericArgs = new()
    {
        "radians", "degrees", "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh",
        "asinh", "acosh", "atanh", "exp", "log", "exp2", "log2", "sqrt", "inversesqrt", "mod",
        "floor", "ceil", "trunc", "round", "roundEven", "fract", "pow", "step", "smoothstep",
        "normalize", "length", "distance", "dot", "cross", "reflect", "refract", "faceforward",
        "dFdx", "dFdy", "fwidth", "dFdxCoarse", "dFdyCoarse", "dFdxFine", "dFdyFine", "fwidthCoarse", "fwidthFine",
        "isnan", "isinf",
    };

    // Validates a built-in call's argument count and, for the numeric families, its argument types. Unknown
    // built-ins are left permissive. Uses the (non-emitting) inferred argument types.
    void CheckBuiltinArgs(Call call)
    {
        int n = call.Args.Count;
        if (BuiltinArity.TryGetValue(call.Name, out int want) && n != want)
            throw new GlslParseException($"'{call.Name}' takes {want} argument{(want == 1 ? "" : "s")}, but {n} {(n == 1 ? "was" : "were")} provided");

        if (RequireNumericArgs.Contains(call.Name))
            for (int i = 0; i < n; i++)
            {
                var t = InferType(call.Args[i]);
                if (t is not (ScalarType or VectorType) || BaseScalar(t) is Scalar.Bool or Scalar.Void)
                    throw new GlslParseException($"'{call.Name}' cannot be applied to '{Types.Name(t)}'");
            }

        if (call.Name is "transpose" or "determinant" or "inverse" && n == 1 && InferType(call.Args[0]) is not MatrixType)
            throw new GlslParseException($"'{call.Name}' requires a matrix argument, not '{Types.Name(InferType(call.Args[0]))}'");

        if (call.Name == "cross")
            for (int i = 0; i < n; i++)
                if (InferType(call.Args[i]) is not VectorType { Size: 3 })
                    throw new GlslParseException($"'cross' requires two vec3 arguments, but argument {i + 1} is '{Types.Name(InferType(call.Args[i]))}'");

        if (call.Name == "dot" && n == 2 && VecOrScalarSize(InferType(call.Args[0])) != VecOrScalarSize(InferType(call.Args[1])))
            throw new GlslParseException($"'dot' requires two arguments of the same size, but got '{Types.Name(InferType(call.Args[0]))}' and '{Types.Name(InferType(call.Args[1]))}'");

        // A sampling coordinate must be floating-point with at least the sampler's spatial dimensionality;
        // projection, array layers and shadow compares only ADD components, so this lower bound is safe.
        if (call.Name is "texture" or "textureLod" or "textureGrad" or "textureProj" or "textureGather"
            && n >= 2 && InferType(call.Args[0]) is SamplerType samp)
        {
            var coord = InferType(call.Args[1]);
            int need = SamplerSpatialDim(samp);
            if (!IsFloating(BaseScalar(coord)) || VecOrScalarSize(coord) < need)
                throw new GlslParseException($"the coordinate for '{call.Name}' on this sampler needs at least {need} floating-point component{(need == 1 ? "" : "s")}, not '{Types.Name(coord)}'");
        }
    }

    static int VecOrScalarSize(GlslType t) => t is VectorType v ? v.Size : 1;

    static int SamplerSpatialDim(SamplerType s) => s.Dim switch
    {
        Dim.D1 or Dim.Buffer => 1,
        Dim.D3 or Dim.Cube => 3,
        _ => 2,
    };

    Value LowerBuiltinCall(Call call)
    {
        try { return LowerBuiltinCallCore(call); }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new GlslParseException($"wrong number of arguments to '{call.Name}'");
        }
        catch (InvalidCastException)
        {
            throw new GlslParseException($"invalid argument type for '{call.Name}'");
        }
    }

    Value LowerBuiltinCallCore(Call call)
    {
        CheckBuiltinArgs(call);
        if (stage != ShaderStage.Geometry && call.Name is "EmitVertex" or "EndPrimitive" or "EmitStreamVertex" or "EndStreamPrimitive")
            throw new GlslParseException($"'{call.Name}' is only valid in a geometry shader");
        if (call.Name == "barrier" && stage is not (ShaderStage.Compute or ShaderStage.TessControl or ShaderStage.Mesh or ShaderStage.Task))
            throw new GlslParseException("'barrier' is only valid in compute, tessellation control, task, and mesh shaders");
        if (call.Name == "atomicCompSwap") return LowerAtomicCompSwap(call);
        // An image's access qualifier is carried by its variable, so resolve the argument expression back
        // to that variable rather than inspecting the loaded image value.
        if (call.Args.Count > 0 && call.Name.StartsWith("image", StringComparison.Ordinal)
            && RootVariableOf(call.Args[0]) is uint imgVar)
        {
            if (call.Name == "imageLoad") RejectWriteOnlyRead(imgVar, -1);
            else RejectReadOnlyWrite(imgVar);
        }
        // SetMeshOutputsEXT/EmitMeshTasksEXT take 32-bit UNSIGNED counts; lowering an int literal as int
        // produces operands spirv-val rejects, so coerce each argument to uint up front.
        if (call.Name is "SetMeshOutputsEXT" or "EmitMeshTasksEXT")
        {
            var counts = call.Args.Select(a => LowerCoerced(a, Types.UInt, call.Name)).ToArray();
            return call.Name == "SetMeshOutputsEXT"
                ? VoidOp(Op.SetMeshOutputsEXT, counts[0].Id, counts[1].Id)
                : EmitMeshTasks(counts);
        }
        if (call.Name.StartsWith("imageAtomic", StringComparison.Ordinal)) return LowerImageAtomic(call);
        if (AtomicOps.Contains(call.Name)) return LowerAtomic(call);   // first argument is an lvalue, not a value
        if (call.Name == "atomicLoad") return LowerAtomicLoad(call);
        if (call.Name == "atomicStore") return LowerAtomicStore(call);
        if (OutParamBuiltins.Contains(call.Name)) return LowerOutParamBuiltin(call);
        if (call.Name.StartsWith("sparse", StringComparison.Ordinal)) return LowerSparse(call);
        if (InterpolateBuiltins.Contains(call.Name)) return LowerInterpolate(call);   // interpolant is an lvalue
        if (call.Name is "anyInvocationARB" or "allInvocationsARB" or "allInvocationsEqualARB"
            or "ballotARB" or "readInvocationARB" or "readFirstInvocationARB") return LowerArbInvocation(call);
        // glslang gates the subgroup OPERATIONS on SPIR-V 1.3 ("'subgroup op' : requires SPIR-V 1.3")
        // but not the subgroup built-ins (gl_SubgroupSize emits with the capability anyway, a
        // glslang-shared invalid-for-target case). Rejecting here keeps every subgroup lowering out.
        if (call.Name.StartsWith("subgroup", StringComparison.Ordinal) && version < SpirVVersion.V1_3)
            throw new GlslParseException("'subgroup op' : requires SPIR-V 1.3", call.Line);
        if (call.Name == "subgroupPartitionNV") return SubgroupPartition(call);
        if (call.Name.StartsWith("subgroupPartitioned", StringComparison.Ordinal)) return SubgroupPartitioned(call);
        if (call.Name is "subgroupRotate" or "subgroupClusteredRotate") return SubgroupRotate(call);
        if (call.Name is "subgroupQuadAll" or "subgroupQuadAny") return SubgroupQuadControl(call);
        if (call.Name.StartsWith("subgroup", StringComparison.Ordinal) && SubgroupOp(call.Name) is not null) return LowerSubgroup(call);
        if (call.Name == "coopMatLoad") return CoopMatLoad(call);
        if (call.Name == "coopMatStore") return CoopMatStore(call);
        if (call.Name == "coopMatMulAdd") return CoopMatMulAdd(call);
        if (call.Name == "textureFootprintNV") return TextureFootprint(call);
        if (call.Name == "nonuniformEXT") return NonUniform(call);
        if (call.Name == "debugPrintfEXT") return DebugPrintf(call);
        if (call.Name == "controlBarrier" && call.Args.Count == 4) return ControlBarrierExplicit(call);
        if (call.Name == "memoryBarrier" && call.Args.Count == 3) return MemoryBarrierExplicit(call);
        if (call.Name == "traceRayEXT") return TraceRay(call, nv: false);
        if (call.Name == "traceNV") return TraceRay(call, nv: true);
        if (call.Name == "executeCallableEXT") return ExecuteCallable(call, nv: false);
        if (call.Name == "executeCallableNV") return ExecuteCallable(call, nv: true);
        if (call.Name == "rayQueryInitializeEXT") return RayQueryInitialize(call);
        if (call.Name.StartsWith("rayQuery", StringComparison.Ordinal)) return RayQueryCall(call);
        if (call.Name is "reportIntersectionEXT" or "reportIntersectionNV") return ReportIntersection(call);
        if (call.Name is "ignoreIntersectionEXT") return TerminatorOp(Op.IgnoreIntersectionKHR);
        if (call.Name is "terminateRayEXT") return TerminatorOp(Op.TerminateRayKHR);
        // The NV forms are plain void instructions, not block terminators (control flow continues past them).
        if (call.Name is "ignoreIntersectionNV") return VoidOp(Op.IgnoreIntersectionNV);
        if (call.Name is "terminateRayNV") return VoidOp(Op.TerminateRayNV);

        var args = call.Args.Select(LowerExpr).ToArray();
        var builtinResult = call.Name switch
        {
            "texture" => Texture(args),
            "helperInvocationEXT" => HelperInvocation(),
            "beginInvocationInterlockARB" => VoidOp(Op.BeginInvocationInterlockEXT),
            "endInvocationInterlockARB" => VoidOp(Op.EndInvocationInterlockEXT),
            "barrier" => Barrier(),
            "memoryBarrier" => MemoryBarrier(MemBarrierScope, AllMemory | AcquireRelease),
            "memoryBarrierBuffer" => MemoryBarrier(MemBarrierScope, UniformMemory | AcquireRelease),
            "memoryBarrierShared" => MemoryBarrier(MemBarrierScope, WorkgroupMemory | AcquireRelease),
            "memoryBarrierImage" => MemoryBarrier(MemBarrierScope, ImageMemory | AcquireRelease),
            "memoryBarrierAtomicCounter" => MemoryBarrier(MemBarrierScope, AtomicCounterMemory | AcquireRelease),
            "groupMemoryBarrier" => MemoryBarrier(ScopeWorkgroup, AllMemory | AcquireRelease),
            "subgroupElect" => SubgroupElect(),
            "subgroupBarrier" => SubgroupControlBarrier(),
            "subgroupMemoryBarrier" => SubgroupMemBarrier(AllMemory | AcquireRelease),
            "subgroupMemoryBarrierBuffer" => SubgroupMemBarrier(UniformMemory | AcquireRelease),
            "subgroupMemoryBarrierShared" => SubgroupMemBarrier(WorkgroupMemory | AcquireRelease),
            "subgroupMemoryBarrierImage" => SubgroupMemBarrier(ImageMemory | AcquireRelease),
            "EmitVertex" => VoidOp(Op.EmitVertex),
            "EndPrimitive" => VoidOp(Op.EndPrimitive),
            "EmitStreamVertex" => StreamOp(Op.EmitStreamVertex, args[0]),
            "EndStreamPrimitive" => StreamOp(Op.EndStreamPrimitive, args[0]),
            "SetMeshOutputsEXT" => VoidOp(Op.SetMeshOutputsEXT, args[0].Id, args[1].Id),
            "EmitMeshTasksEXT" => EmitMeshTasks(args),
            "textureLod" => SampleExplicit(args, ImageOperands.Lod, args[2..]),
            "textureGrad" => SampleExplicit(args, ImageOperands.Grad, args[2..]),
            "textureOffset" => SampleOffset(args),
            "textureGather" => TextureGather(args),
            "textureSize" => TextureSize(args),
            "texelFetch" => TexelFetch(args, offset: null),
            "texelFetchOffset" => TexelFetch(args[..3], args[3]),
            "textureProj" => TextureProj(args),
            "textureProjLod" => new Value(SampleProjExplicit(args, args[2]), Vec4, false),
            "textureClampARB" => SampleClamp(args, ImageOperands.MinLod, [args[2]], explicitLod: false),
            "textureOffsetClampARB" => SampleClamp(args, ImageOperands.ConstOffset | ImageOperands.MinLod, [args[2], args[3]], explicitLod: false),
            "textureGradClampARB" => SampleClamp(args, ImageOperands.Grad | ImageOperands.MinLod, [args[2], args[3], args[4]], explicitLod: true),
            "textureGradOffsetClampARB" => SampleClamp(args, ImageOperands.Grad | ImageOperands.ConstOffset | ImageOperands.MinLod, [args[2], args[3], args[4], args[5]], explicitLod: true),
            "textureProjOffset" => SampleProj(args, ImageOperands.ConstOffset, [args[2]], explicitLod: false),
            "textureProjGrad" => SampleProj(args, ImageOperands.Grad, [args[2], args[3]], explicitLod: true),
            "textureProjLodOffset" => SampleProj(args, ImageOperands.Lod | ImageOperands.ConstOffset, [args[2], args[3]], explicitLod: true),
            "textureProjGradOffset" => SampleProj(args, ImageOperands.Grad | ImageOperands.ConstOffset, [args[2], args[3], args[4]], explicitLod: true),
            "textureLodOffset" => SampleExplicit(args, ImageOperands.Lod | ImageOperands.ConstOffset, [args[2], args[3]]),
            "textureGradOffset" => SampleExplicit(args, ImageOperands.Grad | ImageOperands.ConstOffset, [args[2], args[3], args[4]]),
            "textureQueryLevels" => TextureQueryLevels(args),
            "textureSamples" => TextureSamples(args),
            "textureQueryLod" => TextureQueryLod(args),
            "imageLoad" => ImageLoad(args, ImageMem(call.Args[0])),
            "subpassLoad" => SubpassLoad(args),
            "imageStore" => ImageStore(args, ImageMem(call.Args[0])),
            "imageSize" => ImageSize(args),
            "textureGatherOffset" => TextureGatherOffset(call, args),
            "textureGatherOffsets" => TextureGatherOffsets(args),
            "dot" => Scalarwise(Op.Dot, args),
            "mix" => Mix(args),
            "any" => new Value(Emit(Op.Any, SpvType(Types.Bool), args[0].Id), Types.Bool, false),
            "all" => new Value(Emit(Op.All, SpvType(Types.Bool), args[0].Id), Types.Bool, false),
            "not" => new Value(Emit(Op.LogicalNot, SpvType(args[0].Type), args[0].Id), args[0].Type, false),
            "floatBitsToInt" => Reinterpret(args[0], Scalar.Int),
            "floatBitsToUint" => Reinterpret(args[0], Scalar.UInt),
            "intBitsToFloat" or "uintBitsToFloat" => Reinterpret(args[0], Scalar.Float),
            "isnan" => WithComponent(Op.IsNan, args[0], Scalar.Bool),
            "isinf" => WithComponent(Op.IsInf, args[0], Scalar.Bool),
            "bitCount" => WithComponent(Op.BitCount, args[0], Scalar.Int),
            "findLSB" => ExtFixed(GLSLstd450.FindILsb, ReplaceComponent(args[0].Type, Scalar.Int), args),
            "findMSB" => ExtFixed(BaseScalar(args[0].Type) == Scalar.Int ? GLSLstd450.FindSMsb : GLSLstd450.FindUMsb, ReplaceComponent(args[0].Type, Scalar.Int), args),
            "bitfieldReverse" => new Value(Emit(Op.BitReverse, SpvType(args[0].Type), args[0].Id), args[0].Type, false),
            "bitfieldExtract" => new Value(Emit(BaseScalar(args[0].Type) == Scalar.Int ? Op.BitFieldSExtract : Op.BitFieldUExtract, SpvType(args[0].Type), args[0].Id, args[1].Id, args[2].Id), args[0].Type, false),
            "bitfieldInsert" => new Value(Emit(Op.BitFieldInsert, SpvType(args[0].Type), args[0].Id, args[1].Id, args[2].Id, args[3].Id), args[0].Type, false),
            "matrixCompMult" => MatrixCompMult(args),
            "outerProduct" => OuterProduct(args),
            "lessThan" => Componentwise("<", args[0], args[1]),
            "lessThanEqual" => Componentwise("<=", args[0], args[1]),
            "greaterThan" => Componentwise(">", args[0], args[1]),
            "greaterThanEqual" => Componentwise(">=", args[0], args[1]),
            "equal" => Componentwise("==", args[0], args[1]),
            "notEqual" => Componentwise("!=", args[0], args[1]),
            "transpose" => new Value(Emit(Op.Transpose, SpvType(Transposed(args[0].Type)), args[0].Id), Transposed(args[0].Type), false),
            "inverse" => ExtFixed(GLSLstd450.MatrixInverse, args[0].Type, args),
            "determinant" => ExtFixed(GLSLstd450.Determinant, new ScalarType(BaseScalar(args[0].Type)), args),
            // The 2x32 packers just reinterpret the bits of a 64-bit integer as a pair of 32-bit ones.
            "packInt2x32" => Reinterpret(args[0], Types.Int64),
            "unpackInt2x32" => Reinterpret(args[0], new VectorType(Scalar.Int, 2)),
            "packDouble2x32" => ExtFixed(GLSLstd450.PackDouble2x32, Types.Double, args),
            "unpackDouble2x32" => ExtFixed(GLSLstd450.UnpackDouble2x32, new VectorType(Scalar.UInt, 2), args),
            "packUint2x32" => Reinterpret(args[0], Types.UInt64),
            "unpackUint2x32" => Reinterpret(args[0], new VectorType(Scalar.UInt, 2)),
            "packHalf2x16" => ExtFixed(GLSLstd450.PackHalf2x16, Types.UInt, args),
            "unpackHalf2x16" => ExtFixed(GLSLstd450.UnpackHalf2x16, new VectorType(Scalar.Float, 2), args),
            "packUnorm2x16" => ExtFixed(GLSLstd450.PackUnorm2x16, Types.UInt, args),
            "unpackUnorm2x16" => ExtFixed(GLSLstd450.UnpackUnorm2x16, new VectorType(Scalar.Float, 2), args),
            "packSnorm2x16" => ExtFixed(GLSLstd450.PackSnorm2x16, Types.UInt, args),
            "unpackSnorm2x16" => ExtFixed(GLSLstd450.UnpackSnorm2x16, new VectorType(Scalar.Float, 2), args),
            "packUnorm4x8" => ExtFixed(GLSLstd450.PackUnorm4x8, Types.UInt, args),
            "unpackUnorm4x8" => ExtFixed(GLSLstd450.UnpackUnorm4x8, Vec4, args),
            "packSnorm4x8" => ExtFixed(GLSLstd450.PackSnorm4x8, Types.UInt, args),
            "unpackSnorm4x8" => ExtFixed(GLSLstd450.UnpackSnorm4x8, Vec4, args),
            "mod" => Core(Op.FMod, args),
            "dFdx" => new Value(Emit(Op.DPdx, SpvType(args[0].Type), args[0].Id), args[0].Type, false),
            "dFdy" => new Value(Emit(Op.DPdy, SpvType(args[0].Type), args[0].Id), args[0].Type, false),
            "fwidth" => new Value(Emit(Op.Fwidth, SpvType(args[0].Type), args[0].Id), args[0].Type, false),
            "dFdxFine" => Derivative(Op.DPdxFine, args[0]),
            "dFdxCoarse" => Derivative(Op.DPdxCoarse, args[0]),
            "dFdyFine" => Derivative(Op.DPdyFine, args[0]),
            "dFdyCoarse" => Derivative(Op.DPdyCoarse, args[0]),
            "fwidthFine" => Derivative(Op.FwidthFine, args[0]),
            "fwidthCoarse" => Derivative(Op.FwidthCoarse, args[0]),
            "ldexp" => ExtFixed(GLSLstd450.Ldexp, args[0].Type, args),
            "length" => ExtFixed(GLSLstd450.Length, new ScalarType(BaseScalar(args[0].Type)), args),
            "distance" => ExtFixed(GLSLstd450.Distance, new ScalarType(BaseScalar(args[0].Type)), args),
            "cross" => ExtFixed(GLSLstd450.Cross, args[0].Type, args),
            "reflect" => ExtFixed(GLSLstd450.Reflect, args[0].Type, args),
            "refract" => ExtFixed(GLSLstd450.Refract, args[0].Type, args),
            "faceforward" => ExtFixed(GLSLstd450.FaceForward, args[0].Type, args),
            "atan" => Ext(args.Length == 2 ? GLSLstd450.Atan2 : GLSLstd450.Atan, args),
            "abs" => Ext(IsFloating(BaseScalar(Widest(args))) ? GLSLstd450.FAbs : GLSLstd450.SAbs, args),
            "sign" => Ext(IsFloating(BaseScalar(Widest(args))) ? GLSLstd450.FSign : GLSLstd450.SSign, args),
            "min" => Ext(Fsu(Widest(args), GLSLstd450.FMin, GLSLstd450.SMin, GLSLstd450.UMin), args),
            "max" => Ext(Fsu(Widest(args), GLSLstd450.FMax, GLSLstd450.SMax, GLSLstd450.UMax), args),
            "clamp" => Ext(Fsu(Widest(args), GLSLstd450.FClamp, GLSLstd450.SClamp, GLSLstd450.UClamp), args),
            _ when ComponentwiseExt.TryGetValue(call.Name, out var g) => Ext(g, args),
            _ => throw new GlslParseException($"call to unknown function '{call.Name}'"),
        };
        // A texture-sampling / image-load result takes the sampler's (mediump/lowp) precision, so it is
        // relaxed when the sampler operand is (glslang: resultPrecision = the sampler argument's precision).
        return args.Length > 0 && args[0].Relaxed && SamplingResultFromSampler.Contains(call.Name)
                && BaseScalar(builtinResult.Type) is not Scalar.Bool
            ? builtinResult with { Id = Relax(builtinResult.Id), Relaxed = true }
            : builtinResult;
    }

    // Sampling / image-load builtins whose result carries the sampler's precision (not the max of all args).
    static readonly HashSet<string> SamplingResultFromSampler =
    [
        "texture", "textureLod", "textureGrad", "textureOffset", "textureProj", "textureProjLod",
        "textureLodOffset", "textureGradOffset", "textureProjOffset", "textureProjGrad",
        "textureProjLodOffset", "textureProjGradOffset", "texelFetch", "texelFetchOffset",
        "textureGather", "textureGatherOffset", "textureGatherOffsets",
        "textureClampARB", "textureOffsetClampARB", "textureGradClampARB", "textureGradOffsetClampARB",
        "imageLoad", "subpassLoad",
    ];

    // An ES sampler's precision: mediump/lowp when declared, else the lowp default of a float sampler2D or
    // samplerCube (other sampler types have no ES default). Non-ES samplers are always highp.
    bool SamplerRelaxed(GlslType baseType, Qualifiers quals) =>
        baseType is SamplerType or PureSamplerType or TextureType or ImageType
            && (IsRelaxed(quals)
                || (_isEs && baseType is SamplerType { Sampled: Scalar.Float, Arrayed: false, Shadow: false, Multisampled: false, Dim: Dim.D2 or Dim.Cube }));

    // GLSL passes arguments by copy-in/copy-out: each parameter is a Function pointer, so the
    // caller stores the argument into a temp, passes the temp, and copies back for out/inout.
    Value LowerUserCall(Call call)
    {
        var fn = ResolveOverload(_userFunctions[call.Name], call.Args);
        int n = fn.Params.Count;

        // glslang's handleUserFunctionCall is a strict four-step process. Following it exactly keeps
        // id allocation aligned: (1) evaluate every argument before any copy temp is created, (2)
        // allocate the temps and copy inputs in, (3) make the call, (4) copy outputs back.

        // Step 1: evaluate arguments. Opaque args resolve to a direct pointer; addressable in/inout
        // and out args save their access chain (materializing any lazily-created local now); other
        // in args evaluate as r-values.
        var opaquePtr = new uint?[n];
        var savedChain = new Chain?[n];
        var savedRValue = new Value?[n];
        var savedSwizzle = new (Value Base, int[] Select)?[n];
        var savedIndexable = new (uint ConstId, Value Index, ArrayType Type)?[n];
        for (int i = 0; i < n; i++)
        {
            var p = fn.Params[i];
            // Opaque types (samplers/images/textures) cannot be copied into a Function-storage local;
            // they are passed by their UniformConstant pointer directly. The pointee types must match
            // exactly, so a mismatch throws rather than emitting a call with the wrong argument type.
            if (IsOpaque(p.Type))
            {
                var opaque = ResolveAccess(call.Args[i]);
                if (!opaque.Type.Equals(p.Type))
                    throw new GlslParseException($"argument '{p.Name}': an opaque argument must match the parameter type exactly (samplers/images, including image format)");
                opaquePtr[i] = opaque.Pointer;
            }
            // A swizzle is not code: glslang keeps it in the access chain and only resolves it in step 2,
            // after the copy temp exists. This holds whether the swizzled vector is addressable or an
            // r-value, so only the base is evaluated here.
            // A dynamic index into a const array is a chain over the constant. Nothing is copied while
            // it is built, so only the index is evaluated here and the "indexable" temp waits for step 2.
            else if (!p.ByValue && p.IsIn && !p.IsOut && call.Args[i] is Index cix && cix.Target is Ident cid
                     && _constValues.TryGetValue(cid.Name, out var carr) && carr is CAgg { Type: ArrayType cat }
                     && EvalConst(cix.Idx) is null && cat.Element.Equals(p.Type))
                savedIndexable[i] = (Materialize(carr).Id, LowerExpr(cix.Idx), cat);
            // A single component of an addressable vector is an access chain of its own, handled below;
            // every other swizzle defers to step 2.
            else if (!p.ByValue && call.Args[i] is Member sw && IsSwizzleName(sw.Name)
                     && (sw.Name.Length > 1 || !IsAddressable(sw.Target))
                     && InferType(sw.Target) is VectorType svt
                     && InferType(call.Args[i]).Equals(p.Type))
            {
                int[] sel = [.. sw.Name.Select(SwizzleIndex)];
                bool identity = IsIdentitySwizzle(sel, svt);
                if (IsAddressable(sw.Target))
                {
                    var baseChain = BuildAccess(sw.Target);
                    savedChain[i] = identity ? baseChain : baseChain with { Swizzle = sel };
                }
                else if (p.IsOut)
                    throw new GlslParseException($"argument '{p.Name}': an out/inout argument must be assignable");
                else
                {
                    var baseVal = LowerExpr(sw.Target);
                    if (identity) savedRValue[i] = baseVal;
                    else savedSwizzle[i] = (baseVal, sel);
                }
            }
            // A branching ternary leaves its temporary as the argument's l-value (copied in step 2).
            else if (!p.ByValue && p.IsIn && !p.IsOut && call.Args[i] is Conditional cnd && InferType(cnd).Equals(p.Type))
            {
                var v = LowerConditional(cnd, keepTemp: true, out var tc);
                if (tc is { } c) savedChain[i] = c; else savedRValue[i] = v;
            }
            // An argument needing an implicit conversion is a conversion node in glslang's AST, so it is
            // fully evaluated here and becomes an r-value; only an exact-typed l-value stays a chain.
            // A `const in` param always takes the r-value. An out/inout argument must match exactly.
            else if (p.ByValue || (p.IsIn && !p.IsOut && (!IsAddressable(call.Args[i]) || !InferType(call.Args[i]).Equals(p.Type))))
                savedRValue[i] = LowerCoerced(call.Args[i], p.Type, $"argument '{p.Name}'");
            else
                savedChain[i] = BuildAccess(call.Args[i]);
        }

        // Step 2: allocate the copy temporaries ("param") and copy the inputs into them.
        uint[] argPtrs = new uint[n];
        var copyBack = new List<(uint Temp, Chain Target, GlslType Type)>();
        for (int i = 0; i < n; i++)
        {
            var p = fn.Params[i];
            if (opaquePtr[i] is { } ptr) { argPtrs[i] = ptr; continue; }
            // Nothing can write a `const in` parameter, so it needs no copy: pass the value itself.
            if (p.ByValue) { argPtrs[i] = savedRValue[i]!.Value.Id; continue; }
            uint ptrType = _m.TypePointer(StorageClass.Function, SpvType(p.Type));
            uint temp = _m.AllocId();
            _m.AddName(temp, "param");
            if (ReferenceIn(p.Type) is not null) _m.Decorate(temp, Decoration.AliasedPointer);
            _vars.Add(InstrPool.Rent(Op.Variable, ptrType, temp).AddEnum(StorageClass.Function));
            // A saved r-value was already converted in step 1; a saved chain may be a block member whose
            // stored form differs from the plain param, so copy it through MultiTypeStore.
            if (p.IsIn)
            {
                var inValue = savedChain[i] is { } chain ? LoadChain(chain)
                            : savedSwizzle[i] is { } sz ? Shuffle(sz.Base, sz.Select)
                            : savedIndexable[i] is { } ixa ? IndexableLoad(ixa.ConstId, ixa.Index, ixa.Type)
                            : savedRValue[i]!.Value;
                MultiTypeStore(new Chain(temp, [], p.Type, StorageClass.Function), inValue);
            }
            if (p.IsOut) copyBack.Add((temp, savedChain[i]!.Value, p.Type));
            argPtrs[i] = temp;
        }

        // Step 3: the call.
        uint result = Emit(Op.FunctionCall, SpvType(fn.ReturnType), [fn.Id, .. argPtrs]);

        // Step 4: copy out/inout results back into their saved l-values.
        foreach (var (temp, target, type) in copyBack)
            StoreThroughChain(target, Load(type, temp));

        return new Value(result, fn.ReturnType, false);
    }

    static bool ContainsUserStruct(GlslType t) => t switch
    {
        StructType => true,
        ArrayType a => ContainsUserStruct(a.Element),
        RuntimeArrayType r => ContainsUserStruct(r.Element),
        _ => false,
    };

    // A user function may overload a built-in name (legal GLSL); the built-in stays reachable for calls
    // no user overload accepts, so dispatch only routes to user code when one of them fits.
    bool CallsUserFunction(Call c) =>
        _userFunctions.TryGetValue(c.Name, out var overloads)
        && (!BuiltinFunctionNames.Contains(c.Name) || AnyOverloadFits(overloads, c.Args));

    bool AnyOverloadFits(List<UserFunction> overloads, IReadOnlyList<Expr> args)
    {
        var candidates = overloads.Where(o => o.Params.Count == args.Count).ToList();
        if (candidates.Count == 0) return false;
        var argTypes = args.Select(InferType).ToList();
        return candidates.Any(o => o.Params.Zip(argTypes).All(p =>
            p.First.Type.Equals(p.Second) || CanImplicitlyConvert(p.Second, p.First.Type)));
    }

    UserFunction ResolveOverload(List<UserFunction> overloads, IReadOnlyList<Expr> args)
    {
        var candidates = overloads.Where(o => o.Params.Count == args.Count).ToList();
        if (candidates.Count == 0)
            throw new GlslParseException($"no matching overload for the call: none takes {args.Count} argument{(args.Count == 1 ? "" : "s")}");
        if (candidates.Count == 1) return candidates[0];

        var argTypes = args.Select(InferType).ToList();
        bool Fits(UserFunction o, bool exact) => o.Params.Zip(argTypes).All(p =>
            p.First.Type.Equals(p.Second) || (!exact && CanImplicitlyConvert(p.Second, p.First.Type)));

        var exact = candidates.Where(o => Fits(o, exact: true)).ToList();
        if (exact.Count == 1) return exact[0];   // an exact match wins outright

        var convertible = candidates.Where(o => Fits(o, exact: false)).ToList();
        if (convertible.Count == 1) return convertible[0];
        if (convertible.Count == 0) return candidates[0];   // no viable overload (ill-typed): defer to lowering's error
        throw new GlslParseException($"ambiguous call to an overloaded function: {convertible.Count} overloads match equally well");
    }

    // Whether a value of `from` can be passed where `to` is expected via GLSL's implicit widening.
    static bool CanImplicitlyConvert(GlslType from, GlslType to) => (from, to) switch
    {
        (ScalarType a, ScalarType b) => ImplicitAllowed(a.Kind, b.Kind),
        (VectorType a, VectorType b) => a.Size == b.Size && ImplicitAllowed(a.Component, b.Component),
        _ => false,
    };

    // A scalar/vector type with the same shape as `t` but a different component type.
    static GlslType ReplaceComponent(GlslType t, Scalar component) =>
        t is VectorType v ? new VectorType(component, v.Size) : new ScalarType(component);

    Value Reinterpret(Value v, Scalar target)   // bit-preserving reinterpret (floatBitsToInt etc.)
    {
        var type = ReplaceComponent(v.Type, target);
        return new Value(Emit(Op.Bitcast, SpvType(type), v.Id), type, false);
    }

    Value WithComponent(Op op, Value v, Scalar component)   // op whose result swaps the component type
    {
        var type = ReplaceComponent(v.Type, component);
        return new Value(Emit(op, SpvType(type), v.Id), type, false);
    }

    Value MatrixCompMult(Value[] args)   // component-wise matrix multiply (distinct from '*')
    {
        var m = (MatrixType)args[0].Type;
        var column = new VectorType(m.Component, m.Rows);   // a dmat multiplies as double columns
        uint colType = SpvType(column);
        uint[] columns = [.. Enumerable.Range(0, m.Columns).Select(c =>
            Emit(Op.FMul, colType, Emit(Op.CompositeExtract, colType, args[0].Id, (uint)c), Emit(Op.CompositeExtract, colType, args[1].Id, (uint)c)))];
        return new Value(Emit(Op.CompositeConstruct, SpvType(m), columns), m, false);
    }

    Value OuterProduct(Value[] args)
    {
        var col = (VectorType)args[0].Type;
        var row = (VectorType)args[1].Type;
        // The product keeps the operands' component type (dvec outer dvec yields a dmat).
        var result = new MatrixType(Columns: row.Size, Rows: col.Size, col.Component);
        return new Value(Emit(Op.OuterProduct, SpvType(result), args[0].Id, args[1].Id), result, false);
    }

    // mix(x, y, a): with a bool/bvec selector it is a component-wise select (y where a is true),
    // otherwise the GLSL.std.450 linear interpolation.
    Value Mix(Value[] args)
    {
        if (BaseScalar(args[2].Type) == Scalar.Bool)
            return new Value(Emit(Op.Select, SpvType(args[0].Type), args[2].Id, args[1].Id, args[0].Id), args[0].Type, false);
        return Ext(GLSLstd450.FMix, args);
    }

    // A function whose result is a scalar of the operands' component type (e.g. dot -> float / float16_t).
    Value Scalarwise(Op op, Value[] args)
    {
        var result = new ScalarType(BaseScalar(args[0].Type));
        return Relaxed(new Value(Emit(op, SpvType(result), args[0].Id, args[1].Id), result, false), args);
    }

    Value Texture(Value[] args)
    {
        if (args[0].Type is SamplerType { Shadow: true } shadow && args[1].Type is VectorType coord)
        {
            // samplerCubeArrayShadow passes the compare value as a separate argument (the coordinate is
            // already full); every other shadow sampler takes it as the coordinate's last component, and a
            // trailing argument after that is the LOD bias.
            bool separateDref = shadow.Dim == Dim.Cube && shadow.Arrayed;
            int biasIndex = separateDref ? 3 : 2;
            uint dref = separateDref ? args[2].Id : Emit(Op.CompositeExtract, SpvType(Types.Float), args[1].Id, (uint)(coord.Size - 1));
            if (stage == ShaderStage.Fragment)
            {
                var inst = InstrPool.Rent(Op.ImageSampleDrefImplicitLod, SpvType(Types.Float), _m.AllocId()).Add(args[0].Id, args[1].Id, dref);
                if (args.Length > biasIndex) inst.Add((uint)ImageOperands.Bias).Add(args[biasIndex].Id);
                _code.Add(inst);
                return new Value(inst.ResultId, Types.Float, false);
            }
            if (args.Length > biasIndex) throw new GlslParseException("shadow texture() with a bias is only meaningful in a fragment shader");
            _code.Add(InstrPool.Rent(Op.ImageSampleDrefExplicitLod, SpvType(Types.Float), _m.AllocId())
                .Add(args[0].Id, args[1].Id, dref).Add((uint)ImageOperands.Lod).Add(_m.ConstantFloat(SpvType(Types.Float), 0f)));
            return new Value(_code[^1].ResultId, Types.Float, false);
        }
        // Only fragment has implicit derivatives, so elsewhere an implicit-LOD sample is invalid; sample LOD 0.
        if (stage != ShaderStage.Fragment)
        {
            if (args.Length != 2) throw new GlslParseException("texture() with a bias argument is only meaningful in a fragment shader");
            // glslang materializes the sample's result type before synthesizing the explicit LOD-0 operand.
            SpvType(SampleResult((SamplerType)args[0].Type));
            return SampleExplicit(args, ImageOperands.Lod, [new Value(_m.ConstantFloat(SpvType(Types.Float), 0f), Types.Float, true)]);
        }
        var result = SampleResult((SamplerType)args[0].Type);   // vec4 / ivec4 / uvec4
        if (args.Length == 3)   // texture(sampler, coord, bias)
        {
            _code.Add(InstrPool.Rent(Op.ImageSampleImplicitLod, SpvType(result), _m.AllocId())
                .Add(args[0].Id, args[1].Id).Add((uint)ImageOperands.Bias).Add(args[2].Id));
            return new Value(_code[^1].ResultId, result, false);
        }
        if (args.Length != 2) throw new GlslParseException($"'texture' takes 2 or 3 arguments, but {args.Length} were provided");
        return new Value(Emit(Op.ImageSampleImplicitLod, SpvType(result), args[0].Id, args[1].Id), result, false);
    }

    static bool IsOpaque(GlslType t) => t switch
    {
        SamplerType or TextureType or ImageType or PureSamplerType or AccelStructType => true,
        ArrayType a => IsOpaque(a.Element),
        _ => false,
    };

    // Like IsOpaque, but also looks inside structs. A block member may not contain an opaque type
    // anywhere in its shape, so the whole shape has to be inspected.
    static bool ContainsOpaque(GlslType t) => t switch
    {
        SamplerType or TextureType or ImageType or PureSamplerType or AccelStructType => true,
        ArrayType a => ContainsOpaque(a.Element),
        RuntimeArrayType r => ContainsOpaque(r.Element),
        StructType st => st.Fields.Any(f => ContainsOpaque(f.Type)),
        _ => false,
    };

    // A function parameter of opaque type is a UniformConstant pointer (passed by reference to the
    // descriptor); everything else is a Function-storage copy-in/copy-out local.
    static StorageClass ParamStorage(GlslType t) => IsOpaque(t) ? StorageClass.UniformConstant : StorageClass.Function;

    Value SampleExplicit(Value[] args, ImageOperands mask, Value[] extra)
    {
        // A shadow sampler compares against a reference (the coordinate's last component) and yields a scalar.
        if (args[0].Type is SamplerType { Shadow: true } && args[1].Type is VectorType coord)
        {
            uint dref = Emit(Op.CompositeExtract, SpvType(Types.Float), args[1].Id, (uint)(coord.Size - 1));
            var shadow = InstrPool.Rent(Op.ImageSampleDrefExplicitLod, SpvType(Types.Float), _m.AllocId()).Add(args[0].Id, args[1].Id, dref).Add((uint)mask);
            foreach (var e in extra) shadow.Add(e.Id);
            _code.Add(shadow);
            return new Value(shadow.ResultId, Types.Float, false);
        }
        var result = SampleResult((SamplerType)args[0].Type);   // vec4 / ivec4 / uvec4
        var instr = InstrPool.Rent(Op.ImageSampleExplicitLod, SpvType(result), _m.AllocId()).Add(args[0].Id, args[1].Id).Add((uint)mask);
        foreach (var e in extra) instr.Add(e.Id);
        _code.Add(instr);
        return new Value(instr.ResultId, result, false);
    }

    Value SampleOffset(Value[] args)
    {
        // textureOffset: implicit-LOD sample with a ConstOffset. A shadow sampler compares and yields a
        // scalar; an integer sampler yields ivec4/uvec4.
        var sampler = (SamplerType)args[0].Type;
        if (sampler.Shadow && args[1].Type is VectorType coord)
        {
            uint dref = Emit(Op.CompositeExtract, SpvType(Types.Float), args[1].Id, (uint)(coord.Size - 1));
            _code.Add(InstrPool.Rent(Op.ImageSampleDrefImplicitLod, SpvType(Types.Float), _m.AllocId())
                .Add(args[0].Id, args[1].Id, dref).Add((uint)ImageOperands.ConstOffset).Add(args[2].Id));
            return new Value(_code[^1].ResultId, Types.Float, false);
        }
        var result = SampleResult(sampler);
        _code.Add(InstrPool.Rent(Op.ImageSampleImplicitLod, SpvType(result), _m.AllocId())
            .Add(args[0].Id, args[1].Id).Add((uint)ImageOperands.ConstOffset).Add(args[2].Id));
        return new Value(_code[^1].ResultId, result, false);
    }

    // The query/fetch builtins also accept a bare texture (GL_EXT_samplerless_texture_functions). A bare
    // texture IS the image, so it needs no OpImage extraction; its sampling shape matches a float sampler.
    (uint Image, SamplerType Sampler) QueryImage(Value v) => v.Type is TextureType t
        ? (v.Id, new SamplerType(t.Dim, t.Arrayed, t.Shadow))
        : (Emit(Op.Image, ImageTypeOf((SamplerType)v.Type), v.Id), (SamplerType)v.Type);

    Value TextureSize(Value[] args)
    {
        _m.AddCapability(Capability.ImageQuery);
        var (image, sampler) = QueryImage(args[0]);
        var resultType = CoordType(sampler);
        // Buffer, Rect, and multisampled images have no mip levels, so there is no LOD argument and
        // glslang uses the LOD-less query.
        bool noLod = sampler.Dim is Dim.Buffer or Dim.Rect || sampler.Multisampled;
        uint result = noLod
            ? Emit(Op.ImageQuerySize, SpvType(resultType), image)
            : Emit(Op.ImageQuerySizeLod, SpvType(resultType), image, args[1].Id);
        return new Value(result, resultType, false);
    }

    Value TexelFetch(Value[] args, Value? offset)
    {
        var (image, sampler) = QueryImage(args[0]);
        var result = SampleResult(sampler);
        // Buffer and Rect images have no mip levels, so texelFetch takes no LOD and emits no operand.
        if (sampler.Dim is Dim.Buffer or Dim.Rect)
            return new Value(Emit(Op.ImageFetch, SpvType(result), image, args[1].Id), result, false);
        // For a multisample sampler the third argument is the sample index (Sample operand), else the LOD.
        var mask = sampler.Multisampled ? ImageOperands.Sample : ImageOperands.Lod;
        if (offset is not null) mask |= ImageOperands.ConstOffset;
        var instr = InstrPool.Rent(Op.ImageFetch, SpvType(result), _m.AllocId()).Add(image, args[1].Id).Add((uint)mask).Add(args[2].Id);
        if (offset is Value o) instr.Add(o.Id);
        _code.Add(instr);
        return new Value(instr.ResultId, result, false);
    }

    Value TextureProj(Value[] args)   // projective sample: the coordinate's last component is the divisor
    {
        var sampler = (SamplerType)args[0].Type;
        if (sampler.Shadow && args[1].Type is VectorType pv && stage == ShaderStage.Fragment)
        {
            // Projective shadow: the compare value sits at the spatial slot, the divisor last; move the
            // divisor into the spatial slot, and compare with OpImageSampleProjDref.
            int spatial = sampler.Dim switch { Dim.D1 => 1, Dim.D3 => 3, _ => 2 };
            uint dref = Emit(Op.CompositeExtract, SpvType(Types.Float), args[1].Id, (uint)spatial);
            uint div = Emit(Op.CompositeExtract, SpvType(Types.Float), args[1].Id, (uint)(pv.Size - 1));
            uint pc = Emit(Op.CompositeInsert, SpvType(pv), div, args[1].Id, (uint)spatial);
            return new Value(Emit(Op.ImageSampleProjDrefImplicitLod, SpvType(Types.Float), args[0].Id, pc, dref), Types.Float, false);
        }
        var result = SampleResult((SamplerType)args[0].Type);
        if (stage == ShaderStage.Compute)
            return new Value(SampleProjExplicit(args, new Value(_m.ConstantFloat(SpvType(Types.Float), 0f), Types.Float, true)), result, false);
        return new Value(Emit(Op.ImageSampleProjImplicitLod, SpvType(result), args[0].Id, ProjCoord(args[0], args[1])), result, false);
    }

    uint SampleProjExplicit(Value[] args, Value lod)
    {
        uint coord = ProjCoord(args[0], args[1]);   // before allocating the sample's result id
        _code.Add(InstrPool.Rent(Op.ImageSampleProjExplicitLod, SpvType(SampleResult((SamplerType)args[0].Type)), _m.AllocId())
            .Add(args[0].Id, coord).Add((uint)ImageOperands.Lod).Add(lod.Id));
        return _code[^1].ResultId;
    }

    // The GL_ARB_sparse_texture_clamp textureClamp* family: a sample carrying a min-LOD clamp operand.
    Value SampleClamp(Value[] args, ImageOperands mask, Value[] extra, bool explicitLod)
    {
        _m.AddCapability(Capability.MinLod);
        var result = SampleResult((SamplerType)args[0].Type);
        Op op = explicitLod ? Op.ImageSampleExplicitLod : Op.ImageSampleImplicitLod;
        var instr = InstrPool.Rent(op, SpvType(result), _m.AllocId()).Add(args[0].Id, args[1].Id).Add((uint)mask);
        foreach (var e in extra) instr.Add(e.Id);
        _code.Add(instr);
        return new Value(instr.ResultId, result, false);
    }

    // Projective sample with extra image operands (offset/grad/lod), the textureProj*Offset/Grad family.
    Value SampleProj(Value[] args, ImageOperands mask, Value[] extra, bool explicitLod)
    {
        var result = SampleResult((SamplerType)args[0].Type);
        uint coord = ProjCoord(args[0], args[1]);   // before allocating the sample's result id
        Op op = explicitLod ? Op.ImageSampleProjExplicitLod : Op.ImageSampleProjImplicitLod;
        var instr = InstrPool.Rent(op, SpvType(result), _m.AllocId()).Add(args[0].Id, coord).Add((uint)mask);
        foreach (var e in extra) instr.Add(e.Id);
        _code.Add(instr);
        return new Value(instr.ResultId, result, false);
    }

    // glslang moves an over-provided projective divisor (the coordinate's last component) into the slot
    // right after the spatial coordinates, so OpImageSampleProj* reads it there as the divisor.
    uint ProjCoord(Value sampler, Value coord)
    {
        int spatial = ((SamplerType)sampler.Type).Dim switch { Dim.D1 => 1, Dim.D3 => 3, Dim.Cube => 3, _ => 2 };
        if (coord.Type is VectorType v && v.Size > spatial + 1)
        {
            uint comp = Emit(Op.CompositeExtract, SpvType(new ScalarType(v.Component)), coord.Id, (uint)(v.Size - 1));
            return Emit(Op.CompositeInsert, SpvType(v), comp, coord.Id, (uint)spatial);
        }
        return coord.Id;
    }

    Value TextureQueryLevels(Value[] args)
    {
        _m.AddCapability(Capability.ImageQuery);
        uint image = Emit(Op.Image, ImageTypeOf((SamplerType)args[0].Type), args[0].Id);
        return new Value(Emit(Op.ImageQueryLevels, SpvType(Types.Int), image), Types.Int, false);
    }

    Value TextureSamples(Value[] args)   // the sample count of a multisampled texture
    {
        _m.AddCapability(Capability.ImageQuery);
        uint image = Emit(Op.Image, ImageTypeOf((SamplerType)args[0].Type), args[0].Id);
        return new Value(Emit(Op.ImageQuerySamples, SpvType(Types.Int), image), Types.Int, false);
    }

    Value TextureQueryLod(Value[] args)   // returns (mip level of detail, clamped LOD)
    {
        _m.AddCapability(Capability.ImageQuery);
        return new Value(Emit(Op.ImageQueryLod, SpvType(Vec2), args[0].Id, args[1].Id), Vec2, false);
    }

    Value VoidOp(Op op)
    {
        _code.Add(InstrPool.Rent(op));
        return new Value(0, Types.Void, false);
    }

    Value VoidOp(Op op, params uint[] operands)
    {
        _code.Add(InstrPool.Rent(op).Add(operands));
        return new Value(0, Types.Void, false);
    }

    // EmitMeshTasksEXT(x, y, z) launches the mesh work groups; the task's payload variable is passed
    // as a fourth operand. It terminates the task shader (glslang emits nothing after it).
    Value EmitMeshTasks(Value[] args)
    {
        var op = InstrPool.Rent(Op.EmitMeshTasksEXT).Add(args[0].Id, args[1].Id, args[2].Id);
        if (_taskPayloadName is not null && ResolveGlobal(_taskPayloadName) is { } payload) op.Add(payload.VarId);
        _code.Add(op);
        _terminated = true;
        _m.AllocId();   // glslang opens an unreachable block after this terminator, consuming an id
        return new Value(0, Types.Void, false);
    }

    // traceRayEXT(accel, rayFlags, cullMask, sbtOffset, sbtStride, missIndex, origin, tMin, dir, tMax, payloadLoc):
    // OpTraceRayKHR with the loaded accel structure, the coerced scalar/vector args, and the payload VARIABLE
    // that the trailing (constant) location index names.
    Value TraceRay(Call call, bool nv)
    {
        string name = nv ? "traceNV" : "traceRayEXT";
        if (call.Args.Count != 11) throw new GlslParseException($"{name} takes 11 arguments");
        var t = new GlslType[] { Types.UInt, Types.UInt, Types.UInt, Types.UInt, Types.UInt, Vec3, Types.Float, Vec3, Types.Float };
        var ops = new List<uint> { LowerExpr(call.Args[0]).Id };
        for (int i = 1; i <= 9; i++) ops.Add(LowerCoerced(call.Args[i], t[i - 1], $"{name} argument").Id);
        // OpTraceNV takes the payload as a plain int location; OpTraceRayKHR takes the payload variable it names.
        ops.Add(nv
            ? LowerCoerced(call.Args[10], Types.Int, "traceNV payload").Id
            : RtPayloadVar(call.Args[10], StorageClass.RayPayloadKHR, StorageClass.IncomingRayPayloadKHR));
        return VoidOp(nv ? Op.TraceNV : Op.TraceRayKHR, [.. ops]);
    }

    // executeCallableEXT(sbtIndex, callableDataLoc): OpExecuteCallableKHR sbtIndex, callableDataVar.
    Value ExecuteCallable(Call call, bool nv)
    {
        string name = nv ? "executeCallableNV" : "executeCallableEXT";
        if (call.Args.Count != 2) throw new GlslParseException($"{name} takes 2 arguments");
        uint sbt = LowerCoerced(call.Args[0], Types.UInt, $"{name} argument").Id;
        // OpExecuteCallableNV takes the callable-data as a plain int location; OpExecuteCallableKHR takes the variable.
        uint data = nv
            ? LowerCoerced(call.Args[1], Types.Int, "executeCallableNV callable data").Id
            : RtPayloadVar(call.Args[1], StorageClass.CallableDataKHR, StorageClass.IncomingCallableDataKHR);
        return VoidOp(nv ? Op.ExecuteCallableNV : Op.ExecuteCallableKHR, sbt, data);
    }

    // reportIntersectionEXT(hitT, hitKind) -> bool OpReportIntersectionKHR (intersection stage).
    // The arguments are coerced with the GLSL implicit-conversion rules (LowerCoerced), NOT force-coerced:
    // a bool hitT or a float hitKind has no legal conversion and would emit an OpReportIntersectionKHR
    // spirv-val rejects ("Hit must be a 32-bit int scalar" / "Hit Kind must be a 32-bit unsigned int").
    Value ReportIntersection(Call call)
    {
        if (call.Args.Count != 2) throw new GlslParseException("reportIntersectionEXT takes 2 arguments");
        uint hitT = LowerCoerced(call.Args[0], Types.Float, "reportIntersectionEXT hitT argument").Id;
        uint kind = LowerCoerced(call.Args[1], Types.UInt, "reportIntersectionEXT hitKind argument").Id;
        return new Value(Emit(Op.ReportIntersectionKHR, SpvType(Types.Bool), hitT, kind), Types.Bool, false);
    }

    // OpIgnoreIntersectionKHR / OpTerminateRayKHR are block terminators (like OpKill).
    Value TerminatorOp(Op op)
    {
        _code.Add(InstrPool.Rent(op));
        _terminated = true;
        _m.AllocId();
        return new Value(0, Types.Void, false);
    }

    // rayQueryInitializeEXT(rq, accel, flags, cullMask, origin, tMin, dir, tMax): OpRayQueryInitializeKHR with
    // the ray-query POINTER, the loaded accel structure, and the coerced ray parameters.
    Value RayQueryInitialize(Call call)
    {
        if (call.Args.Count != 8) throw new GlslParseException("rayQueryInitializeEXT takes 8 arguments");
        uint rq = ResolveAccess(call.Args[0]).Pointer;
        var t = new GlslType[] { Types.UInt, Types.UInt, Vec3, Types.Float, Vec3, Types.Float };
        var ops = new List<uint> { rq, LowerExpr(call.Args[1]).Id };
        for (int i = 2; i < 8; i++) ops.Add(LowerCoerced(call.Args[i], t[i - 2], "rayQueryInitializeEXT argument").Id);
        return VoidOp(Op.RayQueryInitializeKHR, [.. ops]);
    }

    // name -> (op, return type or null=void, extra value-arg types after the ray-query pointer). The "committed"
    // selector (a GLSL bool) is passed as the SPIR-V 32-bit int the ray-query ops expect.
    static readonly Dictionary<string, (Op Op, GlslType? Ret, GlslType[] Args)> RayQueryOps = new()
    {
        ["rayQueryTerminateEXT"] = (Op.RayQueryTerminateKHR, null, []),
        ["rayQueryConfirmIntersectionEXT"] = (Op.RayQueryConfirmIntersectionKHR, null, []),
        ["rayQueryGenerateIntersectionEXT"] = (Op.RayQueryGenerateIntersectionKHR, null, [Types.Float]),
        ["rayQueryProceedEXT"] = (Op.RayQueryProceedKHR, Types.Bool, []),
        ["rayQueryGetRayTMinEXT"] = (Op.RayQueryGetRayTMinKHR, Types.Float, []),
        ["rayQueryGetRayFlagsEXT"] = (Op.RayQueryGetRayFlagsKHR, Types.UInt, []),
        ["rayQueryGetWorldRayDirectionEXT"] = (Op.RayQueryGetWorldRayDirectionKHR, Vec3, []),
        ["rayQueryGetWorldRayOriginEXT"] = (Op.RayQueryGetWorldRayOriginKHR, Vec3, []),
        ["rayQueryGetIntersectionCandidateAABBOpaqueEXT"] = (Op.RayQueryGetIntersectionCandidateAABBOpaqueKHR, Types.Bool, []),
        ["rayQueryGetIntersectionTypeEXT"] = (Op.RayQueryGetIntersectionTypeKHR, Types.UInt, [Types.Int]),
        ["rayQueryGetIntersectionTEXT"] = (Op.RayQueryGetIntersectionTKHR, Types.Float, [Types.Int]),
        ["rayQueryGetIntersectionInstanceCustomIndexEXT"] = (Op.RayQueryGetIntersectionInstanceCustomIndexKHR, Types.Int, [Types.Int]),
        ["rayQueryGetIntersectionInstanceIdEXT"] = (Op.RayQueryGetIntersectionInstanceIdKHR, Types.Int, [Types.Int]),
        ["rayQueryGetIntersectionInstanceShaderBindingTableRecordOffsetEXT"] = (Op.RayQueryGetIntersectionInstanceShaderBindingTableRecordOffsetKHR, Types.UInt, [Types.Int]),
        ["rayQueryGetIntersectionGeometryIndexEXT"] = (Op.RayQueryGetIntersectionGeometryIndexKHR, Types.Int, [Types.Int]),
        ["rayQueryGetIntersectionPrimitiveIndexEXT"] = (Op.RayQueryGetIntersectionPrimitiveIndexKHR, Types.Int, [Types.Int]),
        ["rayQueryGetIntersectionBarycentricsEXT"] = (Op.RayQueryGetIntersectionBarycentricsKHR, Vec2, [Types.Int]),
        ["rayQueryGetIntersectionFrontFaceEXT"] = (Op.RayQueryGetIntersectionFrontFaceKHR, Types.Bool, [Types.Int]),
        ["rayQueryGetIntersectionObjectRayDirectionEXT"] = (Op.RayQueryGetIntersectionObjectRayDirectionKHR, Vec3, [Types.Int]),
        ["rayQueryGetIntersectionObjectRayOriginEXT"] = (Op.RayQueryGetIntersectionObjectRayOriginKHR, Vec3, [Types.Int]),
        ["rayQueryGetIntersectionObjectToWorldEXT"] = (Op.RayQueryGetIntersectionObjectToWorldKHR, new MatrixType(4, 3), [Types.Int]),
        ["rayQueryGetIntersectionWorldToObjectEXT"] = (Op.RayQueryGetIntersectionWorldToObjectKHR, new MatrixType(4, 3), [Types.Int]),
    };

    Value RayQueryCall(Call call)
    {
        if (!RayQueryOps.TryGetValue(call.Name, out var spec))
            throw new GlslParseException($"'{call.Name}' is not a supported ray-query function");
        if (call.Args.Count != 1 + spec.Args.Length)
            throw new GlslParseException($"{call.Name} takes {1 + spec.Args.Length} arguments");
        uint rq = ResolveAccess(call.Args[0]).Pointer;
        var ops = new List<uint> { rq };
        // The Int-typed argument in these tables is the GLSL `bool committed` selector, which the ray-query ops
        // take as a 32-bit int; anything else (a tHit float) coerces normally.
        for (int i = 0; i < spec.Args.Length; i++)
            ops.Add(spec.Args[i] == Types.Int ? CommittedInt(call.Args[i + 1]) : LowerCoerced(call.Args[i + 1], spec.Args[i], $"{call.Name} argument").Id);
        if (spec.Ret is null) return VoidOp(spec.Op, [.. ops]);
        return new Value(Emit(spec.Op, SpvType(spec.Ret), [.. ops]), spec.Ret, false);
    }

    // The ray-query `bool committed` selector, lowered as the 32-bit int the SPIR-V ops require. glslang still
    // materializes the bool literal, then converts it (const-folded to an int constant, or OpSelect otherwise).
    uint CommittedInt(Expr e)
    {
        var b = LowerExpr(e);
        if (EvalConst(e) is CScalar cs) return Materialize(new CScalar(Scalar.Int, 0, cs.I != 0 ? 1 : 0)).Id;
        return Emit(Op.Select, SpvType(Types.Int), b.Id, Materialize(new CScalar(Scalar.Int, 0, 1)).Id, Materialize(new CScalar(Scalar.Int, 0, 0)).Id);
    }

    // The ray payload / callable-data variable that a traceRayEXT/executeCallableEXT location index names.
    uint RtPayloadVar(Expr locExpr, params StorageClass[] storages)
    {
        if (EvalConst(locExpr) is not CScalar loc)
            throw new GlslParseException("a ray payload / callable data argument must be a constant location");
        LowerExpr(locExpr);   // glslang still materializes the literal location constant, then uses the variable
        foreach (var sc in storages)
            if (_rtByLoc.TryGetValue((sc, (int)loc.I), out var name)) return ResolveGlobal(name)!.VarId;
        throw new GlslParseException($"no ray payload or callable data declared at location {loc.I}");
    }

    // Memory-semantics constants (SPIR-V): AcquireRelease | the memory classes a Shader touches.
    const uint AcquireRelease = 0x8;
    const uint UniformMemory = 0x40, WorkgroupMemory = 0x100, AtomicCounterMemory = 0x400, ImageMemory = 0x800;
    const uint AllMemory = UniformMemory | WorkgroupMemory | AtomicCounterMemory | ImageMemory;
    const uint ScopeDevice = 1;
    const uint ScopeWorkgroup = 2;
    const uint ScopeSubgroup = 3;
    const uint ScopeInvocation = 4;
    const uint ScopeQueueFamily = 5;

    // subgroupElect(): true in exactly one invocation of the subgroup. OpGroupNonUniformElect.
    Value SubgroupElect()
    {
        _m.AddCapability(Capability.GroupNonUniform);
        uint boolType = SpvType(Types.Bool);   // glslang makes the result type before the scope constant
        uint scope = _m.ConstantInt(SpvType(Types.UInt), ScopeSubgroup);
        return new Value(Emit(Op.GroupNonUniformElect, boolType, scope), Types.Bool, false);
    }

    Value SubgroupMemBarrier(uint semantics)
    {
        _m.AddCapability(Capability.GroupNonUniform);
        return MemoryBarrier(ScopeSubgroup, semantics);
    }

    const uint GroupReduce = 0, GroupInclusiveScan = 1, GroupExclusiveScan = 2, GroupClustered = 3;

    // The capability a subgroup function requires (null = not a subgroup op we handle).
    static Capability? SubgroupOp(string name) => name switch
    {
        "subgroupAll" or "subgroupAny" or "subgroupAllEqual" => Capability.GroupNonUniformVote,
        "subgroupBroadcast" or "subgroupBroadcastFirst" or "subgroupBallot" or "subgroupInverseBallot"
            or "subgroupBallotBitExtract" or "subgroupBallotBitCount" or "subgroupBallotInclusiveBitCount"
            or "subgroupBallotExclusiveBitCount" or "subgroupBallotFindLSB" or "subgroupBallotFindMSB"
            => Capability.GroupNonUniformBallot,
        "subgroupShuffle" or "subgroupShuffleXor" => Capability.GroupNonUniformShuffle,
        "subgroupShuffleUp" or "subgroupShuffleDown" => Capability.GroupNonUniformShuffleRelative,
        "subgroupQuadBroadcast" or "subgroupQuadSwapHorizontal" or "subgroupQuadSwapVertical" or "subgroupQuadSwapDiagonal"
            => Capability.GroupNonUniformQuad,
        _ when name.Contains("Clustered", StringComparison.Ordinal) && ArithmeticSubgroupOp(name) is not null => Capability.GroupNonUniformClustered,
        _ when ArithmeticSubgroupOp(name) is not null => Capability.GroupNonUniformArithmetic,
        _ => null,
    };

    // The reduction/scan subgroup ops: the base op (Add/Mul/Min/Max/And/Or/Xor) plus the scan mode, chosen
    // from the "Inclusive"/"Exclusive" prefix. Returns null when the name is not one of these.
    static (string Base, uint Scan)? ArithmeticSubgroupOp(string name)
    {
        if (!name.StartsWith("subgroup", StringComparison.Ordinal)) return null;
        string rest = name["subgroup".Length..];
        uint scan = GroupReduce;
        if (rest.StartsWith("Inclusive", StringComparison.Ordinal)) { scan = GroupInclusiveScan; rest = rest["Inclusive".Length..]; }
        else if (rest.StartsWith("Exclusive", StringComparison.Ordinal)) { scan = GroupExclusiveScan; rest = rest["Exclusive".Length..]; }
        else if (rest.StartsWith("Clustered", StringComparison.Ordinal)) { scan = GroupClustered; rest = rest["Clustered".Length..]; }
        return rest is "Add" or "Mul" or "Min" or "Max" or "And" or "Or" or "Xor" ? (rest, scan) : null;
    }

    // All GL_KHR_shader_subgroup_* functions except elect/barriers: OpGroupNonUniform* with a Subgroup
    // scope, the arithmetic ones also carrying a reduce/scan operation literal.
    Value LowerSubgroup(Call call)
    {
        _m.AddCapability(Capability.GroupNonUniform);
        _m.AddCapability(SubgroupOp(call.Name)!.Value);
        var a = call.Args.Select(LowerExpr).ToArray();
        var argType = a.Length > 0 ? a[0].Type : Types.UInt;

        if (ArithmeticSubgroupOp(call.Name) is (string baseOp, uint scan))
        {
            var scalar = BaseScalar(argType);
            Op op = baseOp switch
            {
                "Add" => IsFloating(scalar) ? Op.GroupNonUniformFAdd : Op.GroupNonUniformIAdd,
                "Mul" => IsFloating(scalar) ? Op.GroupNonUniformFMul : Op.GroupNonUniformIMul,
                "Min" => IsFloating(scalar) ? Op.GroupNonUniformFMin : IsSigned(scalar) ? Op.GroupNonUniformSMin : Op.GroupNonUniformUMin,
                "Max" => IsFloating(scalar) ? Op.GroupNonUniformFMax : IsSigned(scalar) ? Op.GroupNonUniformSMax : Op.GroupNonUniformUMax,
                "And" => scalar == Scalar.Bool ? Op.GroupNonUniformLogicalAnd : Op.GroupNonUniformBitwiseAnd,
                "Or" => scalar == Scalar.Bool ? Op.GroupNonUniformLogicalOr : Op.GroupNonUniformBitwiseOr,
                _ => scalar == Scalar.Bool ? Op.GroupNonUniformLogicalXor : Op.GroupNonUniformBitwiseXor,
            };
            uint rt = SpvType(argType);
            uint sc = ScopeConst();
            // A clustered reduction carries the cluster size (a constant) as a trailing operand.
            return scan == GroupClustered
                ? new Value(Emit(op, rt, sc, scan, a[0].Id, a[1].Id), argType, RelaxedResult(a[0]))
                : new Value(Emit(op, rt, sc, scan, a[0].Id), argType, RelaxedResult(a[0]));
        }

        // Non-arithmetic ops: result type per function, then scope, then operands.
        var (opcode, result, extraLiteral) = call.Name switch
        {
            "subgroupAll" or "subgroupAny" or "subgroupAllEqual" =>
                (call.Name == "subgroupAll" ? Op.GroupNonUniformAll : call.Name == "subgroupAny" ? Op.GroupNonUniformAny : Op.GroupNonUniformAllEqual, (GlslType)Types.Bool, (uint?)null),
            "subgroupBroadcast" => (Op.GroupNonUniformBroadcast, argType, null),
            "subgroupBroadcastFirst" => (Op.GroupNonUniformBroadcastFirst, argType, null),
            "subgroupBallot" => (Op.GroupNonUniformBallot, new VectorType(Scalar.UInt, 4), null),
            "subgroupInverseBallot" => (Op.GroupNonUniformInverseBallot, Types.Bool, null),
            "subgroupBallotBitExtract" => (Op.GroupNonUniformBallotBitExtract, Types.Bool, null),
            "subgroupBallotBitCount" => (Op.GroupNonUniformBallotBitCount, Types.UInt, GroupReduce),
            "subgroupBallotInclusiveBitCount" => (Op.GroupNonUniformBallotBitCount, Types.UInt, GroupInclusiveScan),
            "subgroupBallotExclusiveBitCount" => (Op.GroupNonUniformBallotBitCount, Types.UInt, GroupExclusiveScan),
            "subgroupBallotFindLSB" => (Op.GroupNonUniformBallotFindLSB, Types.UInt, null),
            "subgroupBallotFindMSB" => (Op.GroupNonUniformBallotFindMSB, Types.UInt, null),
            "subgroupShuffle" => (Op.GroupNonUniformShuffle, argType, null),
            "subgroupShuffleXor" => (Op.GroupNonUniformShuffleXor, argType, null),
            "subgroupShuffleUp" => (Op.GroupNonUniformShuffleUp, argType, null),
            "subgroupShuffleDown" => (Op.GroupNonUniformShuffleDown, argType, null),
            "subgroupQuadBroadcast" => (Op.GroupNonUniformQuadBroadcast, argType, null),
            "subgroupQuadSwapHorizontal" => (Op.GroupNonUniformQuadSwap, argType, 0u),
            "subgroupQuadSwapVertical" => (Op.GroupNonUniformQuadSwap, argType, 1u),
            "subgroupQuadSwapDiagonal" => (Op.GroupNonUniformQuadSwap, argType, 2u),
            _ => throw new GlslParseException($"subgroup function '{call.Name}' is not lowered yet"),
        };
        uint resultType = SpvType(result);
        uint scope = ScopeConst();
        var operands = new List<uint> { scope };
        if (extraLiteral is uint lit && opcode == Op.GroupNonUniformBallotBitCount)
            operands.Add(lit);   // BallotBitCount's group operation is a literal, before the value
        operands.AddRange(a.Select(v => v.Id));
        if (extraLiteral is uint dir && opcode == Op.GroupNonUniformQuadSwap)
            operands.Add(_m.ConstantInt(SpvType(Types.UInt), dir));   // QuadSwap's direction is a constant id
        return new Value(Emit(opcode, resultType, [.. operands]), result, false);
    }

    // GL_NV_shader_subgroup_partitioned: subgroupPartitionNV(v) yields a per-partition ballot (uvec4).
    Value SubgroupPartition(Call call)
    {
        _m.AddCapability(Capability.GroupNonUniformPartitionedNV);
        _m.AddExtension("SPV_NV_shader_subgroup_partitioned");
        var v = LowerExpr(call.Args[0]);
        var uvec4 = new VectorType(Scalar.UInt, 4);
        return new Value(Emit(Op.GroupNonUniformPartitionNV, SpvType(uvec4), v.Id), uvec4, false);
    }

    // subgroupPartitioned[Inclusive|Exclusive]<Base>NV(value, ballot): an OpGroupNonUniform* with the
    // PartitionedReduce/InclusiveScan/ExclusiveScan group operation and the ballot as a trailing operand.
    Value SubgroupPartitioned(Call call)
    {
        _m.AddCapability(Capability.GroupNonUniformPartitionedNV);
        _m.AddExtension("SPV_NV_shader_subgroup_partitioned");
        var value = LowerExpr(call.Args[0]);
        var ballot = LowerExpr(call.Args[1]);
        var scalar = BaseScalar(value.Type);
        string rest = call.Name["subgroupPartitioned".Length..];
        if (rest.EndsWith("NV", StringComparison.Ordinal)) rest = rest[..^2];
        uint groupOp = 6;   // PartitionedReduceNV
        if (rest.StartsWith("Inclusive", StringComparison.Ordinal)) { groupOp = 7; rest = rest["Inclusive".Length..]; }
        else if (rest.StartsWith("Exclusive", StringComparison.Ordinal)) { groupOp = 8; rest = rest["Exclusive".Length..]; }
        Op op = rest switch
        {
            "Add" => IsFloating(scalar) ? Op.GroupNonUniformFAdd : Op.GroupNonUniformIAdd,
            "Mul" => IsFloating(scalar) ? Op.GroupNonUniformFMul : Op.GroupNonUniformIMul,
            "Min" => IsFloating(scalar) ? Op.GroupNonUniformFMin : IsSigned(scalar) ? Op.GroupNonUniformSMin : Op.GroupNonUniformUMin,
            "Max" => IsFloating(scalar) ? Op.GroupNonUniformFMax : IsSigned(scalar) ? Op.GroupNonUniformSMax : Op.GroupNonUniformUMax,
            "And" => scalar == Scalar.Bool ? Op.GroupNonUniformLogicalAnd : Op.GroupNonUniformBitwiseAnd,
            "Or" => scalar == Scalar.Bool ? Op.GroupNonUniformLogicalOr : Op.GroupNonUniformBitwiseOr,
            "Xor" => scalar == Scalar.Bool ? Op.GroupNonUniformLogicalXor : Op.GroupNonUniformBitwiseXor,
            _ => throw new GlslParseException($"partitioned op '{call.Name}' is not lowered yet"),
        };
        return new Value(Emit(op, SpvType(value.Type), ScopeConst(), groupOp, value.Id, ballot.Id), value.Type, RelaxedResult(value));
    }

    // subgroupRotate(v, delta) / subgroupClusteredRotate(v, delta, clusterSize): OpGroupNonUniformRotateKHR
    // with (scope, value, delta[, clusterSize]). glslang emits only the RotateKHR capability, not the base.
    Value SubgroupRotate(Call call)
    {
        _m.AddCapability(Capability.GroupNonUniformRotateKHR);
        _m.AddExtension("SPV_KHR_subgroup_rotate");
        var a = call.Args.Select(LowerExpr).ToArray();
        var operands = new List<uint> { ScopeConst(), a[0].Id, a[1].Id };
        if (a.Length > 2) operands.Add(a[2].Id);
        return new Value(Emit(Op.GroupNonUniformRotateKHR, SpvType(a[0].Type), [.. operands]), a[0].Type, RelaxedResult(a[0]));
    }

    // subgroupQuadAll/subgroupQuadAny: OpGroupNonUniformQuadAll/AnyKHR, which take ONLY the value (no
    // execution-scope operand), under the QuadControlKHR capability.
    Value SubgroupQuadControl(Call call)
    {
        _m.AddCapability(Capability.GroupNonUniform);
        _m.AddCapability(Capability.GroupNonUniformVote);
        _m.AddCapability(Capability.QuadControlKHR);
        _m.AddExtension("SPV_KHR_quad_control");
        var a = LowerExpr(call.Args[0]);
        ScopeConst();   // glslang materialises the Subgroup scope constant even though QuadAll/Any omit it
        Op op = call.Name == "subgroupQuadAll" ? Op.GroupNonUniformQuadAllKHR : Op.GroupNonUniformQuadAnyKHR;
        return new Value(Emit(op, SpvType(Types.Bool), a.Id), Types.Bool, false);
    }

    // GL_ARB_shader_group_vote / GL_ARB_shader_ballot: the older subgroup ops, which take no scope
    // operand. ballotARB returns a uint64 assembled from the low two lanes of an OpSubgroupBallotKHR.
    Value LowerArbInvocation(Call call)
    {
        var a = call.Args.Select(LowerExpr).ToArray();
        switch (call.Name)
        {
            case "anyInvocationARB":
            case "allInvocationsARB":
            case "allInvocationsEqualARB":
                _m.AddCapability(Capability.SubgroupVoteKHR);
                _m.AddExtension("SPV_KHR_subgroup_vote");
                Op vop = call.Name == "anyInvocationARB" ? Op.SubgroupAnyKHR : call.Name == "allInvocationsARB" ? Op.SubgroupAllKHR : Op.SubgroupAllEqualKHR;
                return new Value(Emit(vop, SpvType(Types.Bool), a[0].Id), Types.Bool, false);
            case "readInvocationARB":
                _m.AddCapability(Capability.SubgroupBallotKHR);
                _m.AddExtension("SPV_KHR_shader_ballot");
                return new Value(Emit(Op.SubgroupReadInvocationKHR, SpvType(a[0].Type), a[0].Id, a[1].Id), a[0].Type, false);
            case "readFirstInvocationARB":
                _m.AddCapability(Capability.SubgroupBallotKHR);
                _m.AddExtension("SPV_KHR_shader_ballot");
                return new Value(Emit(Op.SubgroupFirstInvocationKHR, SpvType(a[0].Type), a[0].Id), a[0].Type, false);
            default:   // ballotARB -> uint64 assembled from ballot.xy
                _m.AddCapability(Capability.SubgroupBallotKHR);
                _m.AddExtension("SPV_KHR_shader_ballot");
                uint ballot = Emit(Op.SubgroupBallotKHR, SpvType(new VectorType(Scalar.UInt, 4)), a[0].Id);
                uint x = Emit(Op.CompositeExtract, SpvType(Types.UInt), ballot, 0);
                uint y = Emit(Op.CompositeExtract, SpvType(Types.UInt), ballot, 1);
                uint uvec2 = Emit(Op.CompositeConstruct, SpvType(new VectorType(Scalar.UInt, 2)), x, y);
                return new Value(Emit(Op.Bitcast, SpvType(Types.UInt64), uvec2), Types.UInt64, false);
        }
    }

    // GL_ARB_sparse_texture2: the sparse* family returns an int residency code and writes the texel to
    // an out parameter, via an OpImageSparse* that yields a {code, texel} struct (named "ResType").
    Value LowerSparse(Call call)
    {
        _m.AddCapability(Capability.SparseResidency);
        if (call.Name == "sparseTexelsResidentARB")
        {
            uint code = LowerExpr(call.Args[0]).Id;   // before the bool result type, matching glslang
            return new Value(Emit(Op.ImageSparseTexelsResident, SpvType(Types.Bool), code), Types.Bool, false);
        }

        var s = LowerExpr(call.Args[0]);
        var coord = LowerExpr(call.Args[1]);
        GlslType texel = s.Type is ImageType it ? new VectorType(it.Sampled, 4) : SampleResult((SamplerType)s.Type);
        // Lower the value args and resolve the out-param texel in SOURCE order (the lod precedes the
        // out texel; for gather the component follows it), matching glslang's evaluation and id order.
        uint extra = 0; Access outArg;
        if (call.Name is "sparseTextureLodARB" or "sparseTexelFetchARB")
        {
            extra = LowerExpr(call.Args[2]).Id;
            outArg = ResolveAccess(call.Args[3]);
        }
        else if (call.Name == "sparseTextureGatherARB")
        {
            outArg = ResolveAccess(call.Args[2]);
            extra = call.Args.Count > 3 ? LowerExpr(call.Args[3]).Id : _m.ConstantInt(SpvType(Types.Int), 0);
        }
        else outArg = ResolveAccess(call.Args[2]);
        // texelFetch extracts the underlying image (OpImage) before the ResType struct.
        uint fetchImage = call.Name == "sparseTexelFetchARB" ? Emit(Op.Image, ImageTypeOf((SamplerType)s.Type), s.Id) : 0;
        uint structType = ResTypeStruct2(Types.Int, texel);
        uint res = call.Name switch
        {
            "sparseTextureARB" => Emit(Op.ImageSparseSampleImplicitLod, structType, s.Id, coord.Id),
            "sparseImageLoadARB" => Emit(Op.ImageSparseRead, structType, s.Id, coord.Id),
            "sparseTextureLodARB" => Emit(Op.ImageSparseSampleExplicitLod, structType, s.Id, coord.Id, (uint)ImageOperands.Lod, extra),
            "sparseTexelFetchARB" => Emit(Op.ImageSparseFetch, structType, fetchImage, coord.Id, (uint)ImageOperands.Lod, extra),
            _ => Emit(Op.ImageSparseGather, structType, s.Id, coord.Id, extra),
        };
        Store(outArg.Pointer, Emit(Op.CompositeExtract, SpvType(texel), res, 1));
        return new Value(Emit(Op.CompositeExtract, SpvType(Types.Int), res, 0), Types.Int, false);
    }

    // coopMatLoad(out m, buf, index, stride, layout): OpCooperativeMatrixLoadKHR reads &buf[index] into a
    // value that is stored straight to the destination m (glslang stores the load result to m directly).
    Value CoopMatLoad(Call call)
    {
        var destType = (CoopMatType)InferType(call.Args[0]);
        uint coopType = SpvType(destType);
        var destAccess = BuildAccess(call.Args[0]);   // glslang materialises the destination matrix before the buffer access
        uint bufPtr = CoopMatBufPtr(call.Args[1], call.Args[2]);
        uint layout = LowerExpr(call.Args[4]).Id;
        uint stride = LowerCoerced(call.Args[3], Types.UInt, "coopmat stride").Id;
        uint res = Emit(Op.CooperativeMatrixLoadKHR, coopType, bufPtr, layout, stride, 0);
        StoreThroughChain(destAccess, new Value(res, destType, false));
        return new Value(0, Types.Void, false);
    }

    // coopMatStore(m, buf, index, stride, layout): OpCooperativeMatrixStoreKHR writes m to &buf[index].
    Value CoopMatStore(Call call)
    {
        var obj = LowerExpr(call.Args[0]);
        uint bufPtr = CoopMatBufPtr(call.Args[1], call.Args[2]);
        uint layout = LowerExpr(call.Args[4]).Id;
        uint stride = LowerCoerced(call.Args[3], Types.UInt, "coopmat stride").Id;
        _code.Add(InstrPool.Rent(Op.CooperativeMatrixStoreKHR).Add(bufPtr, obj.Id, layout, stride, 0));
        return new Value(0, Types.Void, false);
    }

    // The pointer to buf[index] a coopmat load/store addresses; the element index is unsigned.
    uint CoopMatBufPtr(Expr buffer, Expr index)
    {
        var chain = BuildAccess(buffer);
        var element = chain.Type switch
        {
            RuntimeArrayType r => r.Element,
            ArrayType a => a.Element,
            _ => throw new GlslParseException("a cooperative-matrix load/store needs an array buffer"),
        };
        uint idx = LowerCoerced(index, Types.UInt, "coopmat index").Id;
        return Collapse(chain with { Indices = [.. chain.Indices, idx], Type = element });
    }

    // coopMatMulAdd(a, b, c): a*b + c, yielding the accumulator's type.
    Value CoopMatMulAdd(Call call)
    {
        var a = LowerExpr(call.Args[0]);
        var b = LowerExpr(call.Args[1]);
        var c = LowerExpr(call.Args[2]);
        return new Value(Emit(Op.CooperativeMatrixMulAddKHR, SpvType(c.Type), a.Id, b.Id, c.Id), c.Type, false);
    }

    // The gl_TextureFootprint2DNV struct a textureFootprintNV out-param fills.
    static readonly StructType FootprintStruct = new("gl_TextureFootprint2DNV",
    [
        ("anchor", new VectorType(Scalar.UInt, 2)),
        ("offset", new VectorType(Scalar.UInt, 2)),
        ("mask", new VectorType(Scalar.UInt, 2)),
        ("lod", new ScalarType(Scalar.UInt)),
        ("granularity", new ScalarType(Scalar.UInt)),
    ]);

    // textureFootprintNV(sampler, coord, granularity, coarse, out footprint): OpImageSampleFootprintNV
    // returns { bool, anchor, offset, mask, lod, granularity }; the trailing five fill the out struct,
    // member for member, and the leading bool is the call's own result.
    Value TextureFootprint(Call call)
    {
        _m.AddCapability(Capability.ImageFootprintNV);
        _m.AddExtension("SPV_NV_shader_image_footprint");
        var s = LowerExpr(call.Args[0]);
        var coord = LowerExpr(call.Args[1]);
        uint granularity = LowerExpr(call.Args[2]).Id;
        uint coarse = LowerExpr(call.Args[3]).Id;
        var outArg = ResolveAccess(call.Args[4]);
        uint resType = _m.TypeStruct([.. new[] { SpvType(Types.Bool) }.Concat(FootprintStruct.Fields.Select(f => SpvType(f.Type)))]);
        if (_resTypeNamed.Add(resType)) _m.AddName(resType, "ResType");
        uint res = Emit(Op.ImageSampleFootprintNV, resType, s.Id, coord.Id, granularity, coarse);
        for (int i = 0; i < FootprintStruct.Fields.Count; i++)
        {
            var ft = FootprintStruct.Fields[i].Type;
            uint idx = MemberIndexConst(i);   // glslang builds the target index before extracting the value
            uint extracted = Emit(Op.CompositeExtract, SpvType(ft), res, (uint)(i + 1));
            uint ptr = Emit(Op.AccessChain, _m.TypePointer(StorageClass.Function, SpvType(ft)), outArg.Pointer, idx);
            Store(ptr, extracted);
        }
        return new Value(Emit(Op.CompositeExtract, SpvType(Types.Bool), res, 0), Types.Bool, false);
    }

    // nonuniformEXT(i): asserts a dynamically non-uniform value. glslang copies it and decorates the
    // copy NonUniform; the taint then propagates through access chains and loads (see Collapse/Load).
    Value NonUniform(Call call)
    {
        var arg = LowerExpr(call.Args[0]);
        _m.AddCapability(Capability.ShaderNonUniform);
        AddIncorporatedExtension("SPV_EXT_descriptor_indexing", SpirVVersion.V1_5);
        uint id = Emit(Op.CopyObject, SpvType(arg.Type), arg.Id);
        _m.Decorate(id, Decoration.NonUniform);
        _nonUniformIds.Add(id);
        return new Value(id, arg.Type, false);
    }

    Value DebugPrintf(Call call)
    {
        if (call.Args[0] is not Literal { Kind: TokenKind.StringConst } fmt)
            throw new NotSupportedException("debugPrintfEXT requires a string literal format");
        uint strId = _m.StringConstant(fmt.Text);
        var argVals = call.Args.Skip(1).Select(LowerExpr).ToArray();
        _m.AddExtension("SPV_KHR_non_semantic_info");
        uint set = _m.ImportExtInstSet("NonSemantic.DebugPrintf");
        var instr = InstrPool.Rent(Op.ExtInst, SpvType(Types.Void), _m.AllocId()).Add(set).Add(1u).Add(strId);
        foreach (var a in argVals) instr.Add(a.Id);
        _code.Add(instr);
        return new Value(0, Types.Void, false);
    }

    Value StreamOp(Op op, Value stream)
    {
        _m.AddCapability(Capability.GeometryStreams);
        _code.Add(InstrPool.Rent(op).Add(stream.Id));
        return new Value(0, Types.Void, false);
    }

    Value HelperInvocation()
    {
        _m.AddCapability(Capability.DemoteToHelperInvocation);
        _m.AddExtension("SPV_EXT_demote_to_helper_invocation");
        return new Value(Emit(Op.IsHelperInvocationEXT, SpvType(Types.Bool)), Types.Bool, false);
    }

    uint ScopeConst() => _m.ConstantInt(SpvType(Types.UInt), ScopeSubgroup);

    // subgroupBarrier(): a control + memory barrier at subgroup scope over all memory.
    Value SubgroupControlBarrier()
    {
        _m.AddCapability(Capability.GroupNonUniform);
        uint scope = _m.ConstantInt(SpvType(Types.UInt), ScopeSubgroup);
        uint semantics = _m.ConstantInt(SpvType(Types.UInt), AllMemory | AcquireRelease);
        _code.Add(InstrPool.Rent(Op.ControlBarrier).Add(scope, scope, semantics));
        return new Value(0, Types.Void, false);
    }

    Value Barrier()
    {
        uint workgroup = _m.ConstantInt(SpvType(Types.UInt), ScopeWorkgroup);
        // A tessellation-control barrier only orders the per-patch invocations (Invocation scope, no
        // memory semantics); other stages fence workgroup memory with acquire-release.
        if (stage == ShaderStage.TessControl)
        {
            uint invocation = _m.ConstantInt(SpvType(Types.UInt), ScopeInvocation);
            uint none = _m.ConstantInt(SpvType(Types.UInt), 0);
            _code.Add(InstrPool.Rent(Op.ControlBarrier).Add(workgroup, invocation, none));
            return new Value(0, Types.Void, false);
        }
        uint semantics = _m.ConstantInt(SpvType(Types.UInt), AcquireRelease | WorkgroupMemory);
        _code.Add(InstrPool.Rent(Op.ControlBarrier).Add(workgroup, workgroup, semantics));
        return new Value(0, Types.Void, false);
    }

    Value MemoryBarrier(uint scope, uint semantics)
    {
        _code.Add(InstrPool.Rent(Op.MemoryBarrier)
            .Add(_m.ConstantInt(SpvType(Types.UInt), scope), _m.ConstantInt(SpvType(Types.UInt), semantics)));
        return new Value(0, Types.Void, false);
    }

    const int VmmSemanticsBits = 0xF000;   // OutputMemory | MakeAvailable | MakeVisible | Volatile

    // Under the Vulkan memory model glslang widens the device-scoped memoryBarrier* fences to QueueFamily.
    uint MemBarrierScope => _vmmPragma ? ScopeQueueFamily : ScopeDevice;

    // Using QueueFamily scope or an availability/visibility/volatile semantics bit pulls the whole
    // module into the Vulkan memory model. An explicit Device scope under the model needs its own cap.
    void MaybeEnableVmm(int scope, int semantics)
    {
        if (scope == (int)ScopeQueueFamily || (semantics & VmmSemanticsBits) != 0) EnableVulkanMemoryModel();
        if (_vmmPragma && scope == 1) _m.AddCapability(Capability.VulkanMemoryModelDeviceScope);
    }

    void EnableVulkanMemoryModel()
    {
        if (_vmmEnabled) return;
        _vmmEnabled = true;
        _m.AddCapability(Capability.VulkanMemoryModel);
        AddIncorporatedExtension("SPV_KHR_vulkan_memory_model", SpirVVersion.V1_5);
        _m.SetMemoryModel(_addressingModel, MemoryModel.Vulkan);
    }

    // controlBarrier(execScope, memScope, storageSem, semantics) and memoryBarrier(scope, storageSem,
    // semantics) from GL_KHR_memory_scope_semantics: the args are int GLSL constants, the SPIR-V
    // operands uint (the semantics being storageSem|semantics). glslang lowers the int args first, then
    // the uint operands, exactly like the extended atomics.
    Value ControlBarrierExplicit(Call call)
    {
        foreach (var arg in call.Args) Materialize(RequiredConst(arg));
        int memScope = ConstInt(call.Args[1]);
        int sem = ConstInt(call.Args[2]) | ConstInt(call.Args[3]);
        MaybeEnableVmm(memScope, sem);
        uint exec = _m.ConstantInt(SpvType(Types.UInt), unchecked((uint)ConstInt(call.Args[0])));
        uint mem = _m.ConstantInt(SpvType(Types.UInt), unchecked((uint)memScope));
        uint semId = _m.ConstantInt(SpvType(Types.UInt), unchecked((uint)sem));
        _code.Add(InstrPool.Rent(Op.ControlBarrier).Add(exec, mem, semId));
        return new Value(0, Types.Void, false);
    }

    Value MemoryBarrierExplicit(Call call)
    {
        foreach (var arg in call.Args) Materialize(RequiredConst(arg));
        int scopeVal = ConstInt(call.Args[0]);
        int sem = ConstInt(call.Args[1]) | ConstInt(call.Args[2]);
        MaybeEnableVmm(scopeVal, sem);
        uint scope = _m.ConstantInt(SpvType(Types.UInt), unchecked((uint)scopeVal));
        uint semId = _m.ConstantInt(SpvType(Types.UInt), unchecked((uint)sem));
        _code.Add(InstrPool.Rent(Op.MemoryBarrier).Add(scope, semId));
        return new Value(0, Types.Void, false);
    }

    // Whether a name could resolve to a value (locals/globals/members/builtins/consts/flag tables) --
    // used so an undeclared identifier gets the resolution-quality message rather than a generic
    // "not a constant" one.
    bool NameResolvable(string n) =>
        _locals.ContainsKey(n) || _pendingLocals.ContainsKey(n)
        || _globals.ContainsKey(n) || _pendingGlobals.ContainsKey(n)
        || _blockMembers.ContainsKey(n) || Builtins.ContainsKey(n) || _builtinVars.ContainsKey(n)
        || _valueParams.ContainsKey(n)
        || _constValues.ContainsKey(n) || _constants.ContainsKey(n)
        || _specConstNames.Contains(n) || _pendingSpecConstants.ContainsKey(n) || _pendingSpecExprs.ContainsKey(n)
        || ShadingRateFlags.ContainsKey(n) || RayFlags.ContainsKey(n);

    // An argument that must be a compile-time constant (memory scopes/semantics are immediate SPIR-V
    // operands): an undeclared identifier gets the resolution-quality message; anything else that does
    // not fold rejects as a constant-expression error -- never a null-deref or cast crash deeper down.
    CVal RequiredConst(Expr e)
    {
        if (EvalConst(e) is { } cv) return cv;
        if (e is Ident { Name: var n } && !NameResolvable(n))
            throw new GlslParseException($"unresolved identifier '{n}'", e.Line);
        throw new GlslParseException("expected a constant expression", e.Line);
    }

    int ConstInt(Expr e) => RequiredConst(e) switch
    {
        CScalar s => (int)s.I,
        _ => throw new GlslParseException("expected a scalar constant expression", e.Line),
    };

    // GL_EXT_shader_atomic_float(2): float/double/float16 atomics use dedicated opcodes, per-width
    // capabilities, and the SPV_EXT_shader_atomic_float* extensions.
    Op FloatAtomicOp(string name, Scalar scalar)
    {
        switch (name)
        {
            case "atomicAdd":
                _m.AddExtension("SPV_EXT_shader_atomic_float_add");
                if (scalar == Scalar.Half) { _m.AddExtension("SPV_EXT_shader_atomic_float16_add"); _m.AddCapability(Capability.AtomicFloat16AddEXT); }
                else _m.AddCapability(scalar == Scalar.Double ? Capability.AtomicFloat64AddEXT : Capability.AtomicFloat32AddEXT);
                return Op.AtomicFAddEXT;
            case "atomicMin":
            case "atomicMax":
                _m.AddExtension("SPV_EXT_shader_atomic_float_min_max");
                _m.AddCapability(scalar switch { Scalar.Half => Capability.AtomicFloat16MinMaxEXT, Scalar.Double => Capability.AtomicFloat64MinMaxEXT, _ => Capability.AtomicFloat32MinMaxEXT });
                return name == "atomicMin" ? Op.AtomicFMinEXT : Op.AtomicFMaxEXT;
            case "atomicExchange":
                return Op.AtomicExchange;
            default:
                throw new GlslParseException($"float atomic '{name}' is not lowered yet");
        }
    }

    // atomicLoad/atomicStore (GL_KHR_memory_scope_semantics): the scope operand is the int scope arg,
    // the semantics operand the uint OR of the two masks, with the same discarded Device/Relaxed
    // default pair glslang seeds for every atomic.
    // Mirror glslang's ParseHelper atomic memory-scope validation (ParseHelper.cpp ~2843): reject invalid or
    // inconsistent memory-semantics / storage-class-semantics combinations at compile time, as glslang does.
    static void ValidateAtomicSemantics(int storage, int semantics, string op, bool isStore = false)
    {
        const int Acquire = 0x2, Release = 0x4, AcqRel = 0x8, NonRelaxed = Acquire | Release | AcqRel;
        const int MakeAvailable = 0x2000, MakeVisible = 0x4000, Volatile = 0x8000;
        const int ValidSemantics = NonRelaxed | MakeAvailable | MakeVisible | Volatile;
        const int ValidStorage = 0x40 | 0x100 | 0x800 | 0x1000;   // Buffer | Shared | Image | Output

        if ((semantics & ~ValidSemantics) != 0) throw new GlslParseException($"'{op}' : Invalid semantics value");
        if ((storage & ~ValidStorage) != 0) throw new GlslParseException($"'{op}' : Invalid storage class semantics value");
        int order = semantics & NonRelaxed;
        if (order != 0 && (order & (order - 1)) != 0)
            throw new GlslParseException($"'{op}' : Semantics must not include multiple of gl_SemanticsRelease, gl_SemanticsAcquire, or gl_SemanticsAcquireRelease");
        if (order != 0 && storage == 0)
            throw new GlslParseException($"'{op}' : Storage class semantics must not be zero when used with gl_SemanticsRelease, gl_SemanticsAcquire, or gl_SemanticsAcquireRelease");
        if (storage != 0 && order == 0)
            throw new GlslParseException($"'{op}' : Semantics must be gl_SemanticsRelease, gl_SemanticsAcquire, or gl_SemanticsAcquireRelease when used with non-zero storage class semantics");
        if ((semantics & MakeAvailable) != 0 && (semantics & (Release | AcqRel)) == 0)
            throw new GlslParseException($"'{op}' : gl_SemanticsMakeAvailable requires gl_SemanticsRelease or gl_SemanticsAcquireRelease");
        if ((semantics & MakeVisible) != 0 && (semantics & (Acquire | AcqRel)) == 0)
            throw new GlslParseException($"'{op}' : gl_SemanticsMakeVisible requires gl_SemanticsAcquire or gl_SemanticsAcquireRelease");
        if (isStore && (semantics & Acquire) != 0)
            throw new GlslParseException($"'{op}' : gl_SemanticsAcquire must not be used with (image) atomic store");
    }

    (uint Scope, uint Semantics) AtomicExtendedScope(Expr scopeArg, Expr storageArg, Expr semArg, string op, bool isStore = false)
    {
        ValidateAtomicSemantics(ConstInt(storageArg), ConstInt(semArg), op, isStore);
        uint scope = LowerExpr(scopeArg).Id;
        LowerExpr(storageArg);
        LowerExpr(semArg);
        int combined = ConstInt(storageArg) | ConstInt(semArg);
        MaybeEnableVmm(ConstInt(scopeArg), combined);
        _m.ConstantInt(SpvType(Types.UInt), _vmmPragma ? ScopeQueueFamily : 1);
        _m.ConstantInt(SpvType(Types.UInt), 0);
        return (scope, _m.ConstantInt(SpvType(Types.UInt), unchecked((uint)combined)));
    }

    Value LowerAtomicLoad(Call call)
    {
        var target = ResolveAccess(call.Args[0]);
        var (scope, semantics) = AtomicExtendedScope(call.Args[1], call.Args[2], call.Args[3], call.Name);
        return new Value(Emit(Op.AtomicLoad, SpvType(target.Type), target.Pointer, scope, semantics), target.Type, false);
    }

    Value LowerAtomicStore(Call call)
    {
        var target = ResolveAccess(call.Args[0]);
        var value = LowerExpr(call.Args[1]);
        var (scope, semantics) = AtomicExtendedScope(call.Args[2], call.Args[3], call.Args[4], call.Name, isStore: true);
        _code.Add(InstrPool.Rent(Op.AtomicStore).Add(target.Pointer, scope, semantics, value.Id));
        return new Value(0, Types.Void, false);
    }

    Value LowerAtomic(Call call)
    {
        var target = ResolveAccess(call.Args[0]);
        // Vulkan permits atomics only on Uniform/StorageBuffer/Workgroup/Image/PhysicalStorageBuffer/
        // TaskPayloadWorkgroupEXT storage; a function-local or private target is invalid.
        if (target.Storage is StorageClass.Function or StorageClass.Private)
            throw new GlslParseException($"'{call.Name}' requires a buffer or shared variable, not a local one");
        RejectReadOnlyWrite(target.Root);
        var value = LowerExpr(call.Args[1]);
        var scalar = BaseScalar(target.Type);
        bool unsigned = IsUnsigned(scalar);
        if (Is64Bit(scalar)) _m.AddCapability(Capability.Int64Atomics);
        Op op = IsFloating(scalar) ? FloatAtomicOp(call.Name, scalar) : call.Name switch
        {
            "atomicAdd" => Op.AtomicIAdd,
            "atomicAnd" => Op.AtomicAnd,
            "atomicOr" => Op.AtomicOr,
            "atomicXor" => Op.AtomicXor,
            "atomicMin" => unsigned ? Op.AtomicUMin : Op.AtomicSMin,
            "atomicMax" => unsigned ? Op.AtomicUMax : Op.AtomicSMax,
            "atomicExchange" => Op.AtomicExchange,
            _ => throw new GlslParseException($"atomic '{call.Name}' is not lowered yet"),
        };
        uint scope, semantics;
        if (call.Args.Count >= 5)
        {
            // Extended form atomicAdd(mem, data, scope, storageSemantics, semantics): the scope operand
            // is the int scope arg, the semantics operand the OR of the two int semantics masks as uint.
            // glslang lowers all three int args (materialising their named constants), then seeds a
            // Device/Relaxed default pair as uint before folding the two masks into the uint operand.
            int storageVal = (int)((CScalar)EvalConst(call.Args[3])!).I, semVal = (int)((CScalar)EvalConst(call.Args[4])!).I;
            ValidateAtomicSemantics(storageVal, semVal, call.Name);
            scope = LowerExpr(call.Args[2]).Id;
            LowerExpr(call.Args[3]);
            LowerExpr(call.Args[4]);
            int scopeVal = (int)((CScalar)EvalConst(call.Args[2])!).I;
            int combined = storageVal | semVal;
            MaybeEnableVmm(scopeVal, combined);
            _m.ConstantInt(SpvType(Types.UInt), _vmmPragma ? ScopeQueueFamily : 1);   // discarded default scope
            _m.ConstantInt(SpvType(Types.UInt), 0);   // discarded Relaxed default
            semantics = _m.ConstantInt(SpvType(Types.UInt), unchecked((uint)combined));
        }
        else
        {
            // Under the Vulkan memory model the default atomic scope is QueueFamily, otherwise Device.
            scope = _m.ConstantInt(SpvType(Types.UInt), _vmmPragma ? ScopeQueueFamily : 1);
            semantics = _m.ConstantInt(SpvType(Types.UInt), 0);   // Relaxed
        }
        return new Value(Emit(op, SpvType(target.Type), target.Pointer, scope, semantics, value.Id), target.Type, false);
    }

    Value Derivative(Op op, Value v)
    {
        _m.AddCapability(Capability.DerivativeControl);
        return new Value(Emit(op, SpvType(v.Type), v.Id), v.Type, false);
    }

    Value LowerAtomicCompSwap(Call call)
    {
        var target = ResolveAccess(call.Args[0]);
        var compare = LowerExpr(call.Args[1]);
        var data = LowerExpr(call.Args[2]);
        uint scope, eqSem, unSem;
        if (call.Args.Count >= 8)
        {
            // Extended form: (mem, compare, data, scope, eqStorage, eqSem, unStorage, unSem). The equal
            // and unequal semantics are separate uint ORs; the scope operand is the int scope arg.
            scope = LowerExpr(call.Args[3]).Id;
            LowerExpr(call.Args[4]); LowerExpr(call.Args[5]); LowerExpr(call.Args[6]); LowerExpr(call.Args[7]);
            int eqCombined = ConstInt(call.Args[4]) | ConstInt(call.Args[5]);
            int unCombined = ConstInt(call.Args[6]) | ConstInt(call.Args[7]);
            MaybeEnableVmm(ConstInt(call.Args[3]), eqCombined | unCombined);
            _m.ConstantInt(SpvType(Types.UInt), _vmmPragma ? ScopeQueueFamily : 1);
            _m.ConstantInt(SpvType(Types.UInt), 0);
            eqSem = _m.ConstantInt(SpvType(Types.UInt), unchecked((uint)eqCombined));
            unSem = _m.ConstantInt(SpvType(Types.UInt), unchecked((uint)unCombined));
        }
        else
        {
            scope = _m.ConstantInt(SpvType(Types.UInt), _vmmPragma ? ScopeQueueFamily : 1);   // QueueFamily under VMM, else Device
            eqSem = unSem = _m.ConstantInt(SpvType(Types.UInt), 0);   // Relaxed (used for both equal and unequal)
        }
        // OpAtomicCompareExchange: pointer, scope, equal-semantics, unequal-semantics, value, comparator.
        return new Value(Emit(Op.AtomicCompareExchange, SpvType(target.Type), target.Pointer, scope, eqSem, unSem, data.Id, compare.Id), target.Type, false);
    }

    // The coherent/volatile qualifier of the image variable an image-call argument names (Vulkan memory model only).
    MemQual ImageMem(Expr e) => e switch
    {
        Ident id when ResolveLocalVar(id.Name) is null => ResolveGlobal(id.Name)?.Mem ?? MemQual.None,
        Index ix => ImageMem(ix.Target),
        _ => MemQual.None,
    };

    // Under the Vulkan memory model a coherent image access makes its texel available (store) or visible (load)
    // at queue-family scope and marks it non-private; a volatile one is also VolatileTexel. Null when not needed.
    (uint Mask, uint Scope)? ImageMemOperands(MemQual mem, bool store)
    {
        if (!_vmmPragma || mem is not (MemQual.Coherent or MemQual.Volatile)) return null;
        uint mask = (uint)(ImageOperands.NonPrivateTexel | (store ? ImageOperands.MakeTexelAvailable : ImageOperands.MakeTexelVisible));
        if (mem == MemQual.Volatile) mask |= (uint)ImageOperands.VolatileTexel;
        return (mask, _m.ConstantInt(SpvType(Types.UInt), ScopeQueueFamily));
    }

    Value ImageLoad(Value[] args, MemQual mem = MemQual.None)   // texel type follows the image's sampled type (vec4 / ivec4 / uvec4)
    {
        var image = (ImageType)args[0].Type;
        // Reading a storage image declared without a format layout qualifier needs this capability.
        if (image.Format == ImageFormat.Unknown) _m.AddCapability(Capability.StorageImageReadWithoutFormat);
        var texel = new VectorType(image.Sampled, 4);
        if (image.Multisampled)   // imageLoad(img, coord, sample): the sample index is a Sample image operand
        {
            _m.AddCapability(Capability.StorageImageMultisample);
            var msRead = InstrPool.Rent(Op.ImageRead, SpvType(texel), _m.AllocId()).Add(args[0].Id, args[1].Id);
            if (ImageMemOperands(mem, store: false) is (uint msMask, uint msScope))
                msRead.Add((uint)ImageOperands.Sample | msMask).Add(args[2].Id, msScope);
            else msRead.Add((uint)ImageOperands.Sample).Add(args[2].Id);
            _code.Add(msRead);
            return new Value(msRead.ResultId, texel, false);
        }
        if (ImageMemOperands(mem, store: false) is (uint mask, uint scope))
        {
            var read = InstrPool.Rent(Op.ImageRead, SpvType(texel), _m.AllocId()).Add(args[0].Id, args[1].Id).Add(mask, scope);
            _code.Add(read);
            return new Value(read.ResultId, texel, false);
        }
        return new Value(Emit(Op.ImageRead, SpvType(texel), args[0].Id, args[1].Id), texel, false);
    }

    Value SubpassLoad(Value[] args)   // reads the current fragment's input attachment at coordinate (0,0)
    {
        var result = new VectorType(((ImageType)args[0].Type).Sampled, 4);
        uint z = _m.ConstantInt(_m.TypeInt(32, signed: true), 0);
        uint coord = _m.ConstantComposite(SpvType(new VectorType(Scalar.Int, 2)), z, z);
        // A multisampled attachment takes the sample index as an image operand.
        if (args.Length > 1)
        {
            _code.Add(InstrPool.Rent(Op.ImageRead, SpvType(result), _m.AllocId())
                .Add(args[0].Id, coord).Add((uint)ImageOperands.Sample).Add(args[1].Id));
            return new Value(_code[^1].ResultId, result, false);
        }
        return new Value(Emit(Op.ImageRead, SpvType(result), args[0].Id, coord), result, false);
    }

    Value ImageStore(Value[] args, MemQual mem = MemQual.None)
    {
        var image = (ImageType)args[0].Type;
        // Writing a storage image declared without a format layout qualifier needs this capability.
        if (image.Format == ImageFormat.Unknown) _m.AddCapability(Capability.StorageImageWriteWithoutFormat);
        if (image.Multisampled)   // imageStore(img, coord, sample, texel): texel, then the Sample operand
        {
            _m.AddCapability(Capability.StorageImageMultisample);
            var msWrite = InstrPool.Rent(Op.ImageWrite).Add(args[0].Id, args[1].Id, args[3].Id);
            if (ImageMemOperands(mem, store: true) is (uint msMask, uint msScope))
                msWrite.Add((uint)ImageOperands.Sample | msMask).Add(args[2].Id, msScope);
            else msWrite.Add((uint)ImageOperands.Sample).Add(args[2].Id);
            _code.Add(msWrite);
            return new Value(0, Types.Void, false);
        }
        var write = InstrPool.Rent(Op.ImageWrite).Add(args[0].Id, args[1].Id, args[2].Id);
        if (ImageMemOperands(mem, store: true) is (uint mask, uint scope)) write.Add(mask, scope);
        _code.Add(write);
        return new Value(0, Types.Void, false);
    }

    Value Reinterpret(Value v, GlslType target) => new(Emit(Op.Bitcast, SpvType(target), v.Id), target, false);

    // A compile-time-constant offset uses ConstOffset; a dynamic one uses the Offset operand.
    Value TextureGatherOffset(Call call, Value[] args)
    {
        // Shadow: (sampler, coord, refZ, offset) -> OpImageDrefGather. Else (sampler, coord, offset[, comp]).
        if (((SamplerType)args[0].Type).Shadow)
            return GatherDref(args[0], args[1], args[2], EvalConst(call.Args[3]) is not null ? ImageOperands.ConstOffset : ImageOperands.Offset, args[3]);
        return GatherWithOffsets(args, EvalConst(call.Args[2]) is not null ? ImageOperands.ConstOffset : ImageOperands.Offset);
    }

    // textureGatherOffsets(sampler, coord, offsets[4] [, component]) gathers at four constant offsets.
    Value TextureGatherOffsets(Value[] args)
    {
        if (((SamplerType)args[0].Type).Shadow)
            return GatherDref(args[0], args[1], args[2], ImageOperands.ConstOffsets, args[3]);
        return GatherWithOffsets(args, ImageOperands.ConstOffsets);
    }

    Value GatherWithOffsets(Value[] args, ImageOperands offsetKind)
    {
        // A single CONSTANT offset (ConstOffset) is a base feature; a dynamic Offset or the ConstOffsets
        // form needs the extended-gather capability.
        if (offsetKind is not ImageOperands.ConstOffset) _m.AddCapability(Capability.ImageGatherExtended);
        var result = SampleResult((SamplerType)args[0].Type);
        uint component = args.Length > 3 ? args[3].Id : _m.ConstantInt(SpvType(Types.Int), 0);
        _code.Add(InstrPool.Rent(Op.ImageGather, SpvType(result), _m.AllocId())
            .Add(args[0].Id, args[1].Id, component).Add((uint)offsetKind).Add(args[2].Id));
        return new Value(_code[^1].ResultId, result, false);
    }

    // Shadow gather with an offset: OpImageDrefGather compares against refZ and takes the offset operand.
    Value GatherDref(Value sampler, Value coord, Value refZ, ImageOperands offsetKind, Value offset)
    {
        if (offsetKind is not ImageOperands.ConstOffset) _m.AddCapability(Capability.ImageGatherExtended);
        _code.Add(InstrPool.Rent(Op.ImageDrefGather, SpvType(Vec4), _m.AllocId())
            .Add(sampler.Id, coord.Id, refZ.Id).Add((uint)offsetKind).Add(offset.Id));
        return new Value(_code[^1].ResultId, Vec4, false);
    }

    // textureGather(sampler, coord [, component]): the component defaults to 0. On a shadow sampler
    // the third argument is the depth reference to compare against, not a component.
    Value TextureGather(Value[] args)
    {
        var sampler = (SamplerType)args[0].Type;
        var result = SampleResult(sampler);
        if (sampler.Shadow)
            return new Value(Emit(Op.ImageDrefGather, SpvType(result), args[0].Id, args[1].Id, args[2].Id), result, false);
        uint component = args.Length > 2 ? args[2].Id : _m.ConstantInt(SpvType(Types.Int), 0);
        return new Value(Emit(Op.ImageGather, SpvType(result), args[0].Id, args[1].Id, component), result, false);
    }

    // Image atomics operate on a texel pointer obtained from the image variable, not a loaded value.
    Value LowerImageAtomic(Call call)
    {
        var image = ResolveAccess(call.Args[0]);
        var img = (ImageType)image.Type;
        var coord = LowerExpr(call.Args[1]);
        // glslang evaluates every argument before building the texel pointer, so the data operands'
        // constants come first (the scope/semantics constants then often just reuse them).
        var operands = call.Args.Skip(2).Select(LowerExpr).ToArray();
        var element = new ScalarType(img.Sampled);
        bool unsigned = img.Sampled == Scalar.UInt;
        uint sample = _m.ConstantInt(SpvType(Types.UInt), 0);
        uint texelPtr = Emit(Op.ImageTexelPointer, _m.TypePointer(StorageClass.Image, SpvType(element)), image.Pointer, coord.Id, sample);
        uint scope = _m.ConstantInt(SpvType(Types.UInt), 1);       // Device
        uint semantics = _m.ConstantInt(SpvType(Types.UInt), 0);   // Relaxed

        if (call.Name == "imageAtomicCompSwap")   // (image, coord, compare, data)
            return new Value(Emit(Op.AtomicCompareExchange, SpvType(element), texelPtr, scope, semantics, semantics, operands[1].Id, operands[0].Id), element, false);

        var value = operands[0];
        Op op = IsFloating(img.Sampled) ? FloatAtomicOp(call.Name.Replace("imageAtomic", "atomic"), img.Sampled) : call.Name switch
        {
            "imageAtomicAdd" => Op.AtomicIAdd,
            "imageAtomicAnd" => Op.AtomicAnd,
            "imageAtomicOr" => Op.AtomicOr,
            "imageAtomicXor" => Op.AtomicXor,
            "imageAtomicMin" => unsigned ? Op.AtomicUMin : Op.AtomicSMin,
            "imageAtomicMax" => unsigned ? Op.AtomicUMax : Op.AtomicSMax,
            "imageAtomicExchange" => Op.AtomicExchange,
            _ => throw new GlslParseException($"image atomic '{call.Name}' is not lowered yet"),
        };
        return new Value(Emit(op, SpvType(element), texelPtr, scope, semantics, value.Id), element, false);
    }

    readonly HashSet<uint> _resTypeNamed = [];

    // The *Carry / *Extended ops return a two-member struct of the operand type, which glslang names
    // "ResType". The type de-duplicates, so it is only named the first time it appears.
    uint ResTypeStruct(GlslType component)
    {
        uint id = _m.TypeStruct(SpvType(component), SpvType(component));
        if (_resTypeNamed.Add(id)) _m.AddName(id, "ResType");
        return id;
    }

    uint ResTypeStruct2(GlslType a, GlslType b)
    {
        uint id = _m.TypeStruct(SpvType(a), SpvType(b));
        if (_resTypeNamed.Add(id)) _m.AddName(id, "ResType");
        return id;
    }

    // modf/frexp write their secondary result through a pointer; the *Extended/*Carry ops return a
    // two-member struct whose parts are extracted and stored to the out parameters.
    Value LowerOutParamBuiltin(Call call)
    {
        uint set = _m.ImportExtInstSet("GLSL.std.450");
        switch (call.Name)
        {
            case "modf":
            {
                // glslang uses the ModfStruct form: a {fractional, integer} struct whose integer part is
                // stored to the out parameter and whose fractional part is the return value.
                var x = LowerExpr(call.Args[0]);
                var outArg = ResolveAccess(call.Args[1]);
                uint structType = ResTypeStruct(x.Type);
                uint s = Emit(Op.ExtInst, structType, set, (uint)GLSLstd450.ModfStruct, x.Id);
                Store(outArg.Pointer, Emit(Op.CompositeExtract, SpvType(outArg.Type), s, 1));
                return new Value(Emit(Op.CompositeExtract, SpvType(x.Type), s, 0), x.Type, false);
            }
            case "frexp":
            {
                // glslang uses the FrexpStruct form: a {significand, exponent} struct whose exponent
                // is extracted to the out parameter and whose significand is the return value.
                var x = LowerExpr(call.Args[0]);
                var outArg = ResolveAccess(call.Args[1]);
                uint structType = ResTypeStruct2(x.Type, outArg.Type);
                uint s = Emit(Op.ExtInst, structType, set, (uint)GLSLstd450.FrexpStruct, x.Id);
                Store(outArg.Pointer, Emit(Op.CompositeExtract, SpvType(outArg.Type), s, 1));
                return new Value(Emit(Op.CompositeExtract, SpvType(x.Type), s, 0), x.Type, false);
            }
            case "uaddCarry" or "usubBorrow":
            {
                var x = LowerExpr(call.Args[0]);
                var y = LowerExpr(call.Args[1]);
                var carry = ResolveAccess(call.Args[2]);
                uint structType = ResTypeStruct(x.Type);
                uint s = Emit(call.Name == "uaddCarry" ? Op.IAddCarry : Op.ISubBorrow, structType, x.Id, y.Id);
                Store(carry.Pointer, Emit(Op.CompositeExtract, SpvType(x.Type), s, 1));   // member 1 is the carry/borrow
                return new Value(Emit(Op.CompositeExtract, SpvType(x.Type), s, 0), x.Type, false);
            }
            default:   // umulExtended / imulExtended(x, y, out msb, out lsb) -> void
            {
                var x = LowerExpr(call.Args[0]);
                var y = LowerExpr(call.Args[1]);
                var msb = ResolveAccess(call.Args[2]);
                var lsb = ResolveAccess(call.Args[3]);
                uint structType = ResTypeStruct(x.Type);
                uint s = Emit(call.Name == "umulExtended" ? Op.UMulExtended : Op.SMulExtended, structType, x.Id, y.Id);
                Store(lsb.Pointer, Emit(Op.CompositeExtract, SpvType(x.Type), s, 0));   // member 0 = low bits
                Store(msb.Pointer, Emit(Op.CompositeExtract, SpvType(x.Type), s, 1));   // member 1 = high bits
                return new Value(0, Types.Void, false);
            }
        }
    }

    // interpolateAt*(interpolant, ...): the interpolant is passed as a pointer (its input variable).
    Value LowerInterpolate(Call call)
    {
        _m.AddCapability(Capability.InterpolationFunction);
        var interpolant = ResolveAccess(call.Args[0]);
        var op = call.Name switch
        {
            "interpolateAtCentroid" => GLSLstd450.InterpolateAtCentroid,
            "interpolateAtSample" => GLSLstd450.InterpolateAtSample,
            _ => GLSLstd450.InterpolateAtOffset,
        };
        uint set = _m.ImportExtInstSet("GLSL.std.450");
        uint typeId = SpvType(interpolant.Type);
        // glslang lowers the offset/sample argument BEFORE allocating the ExtInst's result id.
        uint[] extra = [.. call.Args.Skip(1).Select(a => LowerExpr(a).Id)];
        var instr = InstrPool.Rent(Op.ExtInst, typeId, _m.AllocId()).Add(set).Add((uint)op).Add(interpolant.Pointer).Add(extra);
        _code.Add(instr);
        return new Value(instr.ResultId, interpolant.Type, false);
    }

    Value ImageSize(Value[] args)
    {
        _m.AddCapability(Capability.ImageQuery);
        var img = (ImageType)args[0].Type;
        var resultType = IntCoordType(img.Dim, img.Arrayed);
        return new Value(Emit(Op.ImageQuerySize, SpvType(resultType), args[0].Id), resultType, false);
    }

    uint SamplerSpvType(SamplerType s)
    {
        if (s.Dim == Dim.Cube && s.Arrayed) _m.AddCapability(Capability.SampledCubeArray);
        if (s.Dim == Dim.D1) _m.AddCapability(Capability.Sampled1D);
        if (s.Dim == Dim.Rect) _m.AddCapability(Capability.SampledRect);
        if (s.Dim == Dim.Buffer) _m.AddCapability(Capability.SampledBuffer);
        return _m.TypeSampledImage(ImageTypeOf(s));
    }

    uint ImageSpvType(ImageType img)
    {
        if (img.Dim == Dim.SubpassData) _m.AddCapability(Capability.InputAttachment);
        if (img.Dim == Dim.D1) _m.AddCapability(Capability.Image1D);
        if (img.Dim == Dim.Buffer) _m.AddCapability(Capability.ImageBuffer);
        if (img.Dim == Dim.Cube && img.Arrayed) _m.AddCapability(Capability.ImageCubeArray);
        if (img.Multisampled && img.Arrayed) _m.AddCapability(Capability.ImageMSArray);
        return _m.TypeImage(SpvType(new ScalarType(img.Sampled)), img.Dim, 0, img.Arrayed ? 1 : 0, img.Multisampled ? 1 : 0, 2, img.Format);
    }

    uint ImageTypeOf(SamplerType s) =>
        _m.TypeImage(SpvType(new ScalarType(s.Sampled)), s.Dim, s.Shadow ? 1 : 0, s.Arrayed ? 1 : 0, s.Multisampled ? 1 : 0, 1, ImageFormat.Unknown);

    static VectorType SampleResult(SamplerType s) => new(s.Sampled, 4);   // texture()/texelFetch result: vec4 / ivec4 / uvec4

    // The integer coordinate/size type for a sampler or image: base dimensions plus the array layer.
    static GlslType CoordType(SamplerType s) => IntCoordType(s.Dim, s.Arrayed);

    static GlslType IntCoordType(Dim dim, bool arrayed)
    {
        int n = (dim switch { Dim.D1 or Dim.Buffer => 1, Dim.D3 => 3, Dim.Cube => 2, _ => 2 }) + (arrayed ? 1 : 0);
        return n == 1 ? Types.Int : new VectorType(Scalar.Int, n);
    }

    // The conservative-depth mode from a `layout(depth_*) out float gl_FragDepth;` redeclaration.
    static ExecutionMode? DepthConditionOf(Qualifiers q)
    {
        foreach (var item in q.Layout)
            switch (item.Name)
            {
                case "depth_greater": return ExecutionMode.DepthGreater;
                case "depth_less": return ExecutionMode.DepthLess;
                case "depth_unchanged": return ExecutionMode.DepthUnchanged;
            }
        return null;
    }

    // The layout(...) storage-image format qualifiers, spelled as in the GLSL spec.
    static readonly Dictionary<string, ImageFormat> ImageFormats = new()
    {
        ["rgba32f"] = ImageFormat.Rgba32f, ["rgba16f"] = ImageFormat.Rgba16f, ["rg32f"] = ImageFormat.Rg32f,
        ["rg16f"] = ImageFormat.Rg16f, ["r11f_g11f_b10f"] = ImageFormat.R11fG11fB10f, ["r32f"] = ImageFormat.R32f,
        ["r16f"] = ImageFormat.R16f,
        ["rgba16"] = ImageFormat.Rgba16, ["rgb10_a2"] = ImageFormat.Rgb10A2, ["rgba8"] = ImageFormat.Rgba8,
        ["rg16"] = ImageFormat.Rg16, ["rg8"] = ImageFormat.Rg8, ["r16"] = ImageFormat.R16, ["r8"] = ImageFormat.R8,
        ["rgba16_snorm"] = ImageFormat.Rgba16Snorm, ["rgba8_snorm"] = ImageFormat.Rgba8Snorm,
        ["rg16_snorm"] = ImageFormat.Rg16Snorm, ["rg8_snorm"] = ImageFormat.Rg8Snorm,
        ["r16_snorm"] = ImageFormat.R16Snorm, ["r8_snorm"] = ImageFormat.R8Snorm,
        ["rgba32i"] = ImageFormat.Rgba32i, ["rgba16i"] = ImageFormat.Rgba16i, ["rgba8i"] = ImageFormat.Rgba8i,
        ["rg32i"] = ImageFormat.Rg32i, ["rg16i"] = ImageFormat.Rg16i, ["rg8i"] = ImageFormat.Rg8i,
        ["r32i"] = ImageFormat.R32i, ["r16i"] = ImageFormat.R16i, ["r8i"] = ImageFormat.R8i,
        ["rgba32ui"] = ImageFormat.Rgba32ui, ["rgba16ui"] = ImageFormat.Rgba16ui, ["rgb10_a2ui"] = ImageFormat.Rgb10a2ui,
        ["rgba8ui"] = ImageFormat.Rgba8ui, ["rg32ui"] = ImageFormat.Rg32ui, ["rg16ui"] = ImageFormat.Rg16ui,
        ["rg8ui"] = ImageFormat.Rg8ui, ["r32ui"] = ImageFormat.R32ui, ["r16ui"] = ImageFormat.R16ui,
        ["r8ui"] = ImageFormat.R8ui,
    };

    // Only a core subset of formats is available unadorned; the rest need StorageImageExtendedFormats.
    static readonly HashSet<ImageFormat> CoreImageFormats =
    [
        ImageFormat.Unknown,
        ImageFormat.Rgba32f, ImageFormat.Rgba16f, ImageFormat.R32f, ImageFormat.Rgba8, ImageFormat.Rgba8Snorm,
        ImageFormat.Rgba32i, ImageFormat.Rgba16i, ImageFormat.Rgba8i, ImageFormat.R32i,
        ImageFormat.Rgba32ui, ImageFormat.Rgba16ui, ImageFormat.Rgba8ui, ImageFormat.R32ui,
    ];

    ImageFormat ResolveImageFormat(Qualifiers q)
    {
        foreach (var item in q.Layout)
            if (ImageFormats.TryGetValue(item.Name, out var format))
            {
                if (!CoreImageFormats.Contains(format)) _m.AddCapability(Capability.StorageImageExtendedFormats);
                return format;
            }
        return ImageFormat.Unknown;
    }

    // Component-wise: broadcast scalar args to the widest (vector) type, then emit.
    Value Ext(GLSLstd450 op, Value[] args) { var t = Widest(args); return ExtFixed(op, t, Broadcast(args, t)); }
    Value Core(Op op, Value[] args) { var t = Widest(args); var b = Broadcast(args, t); return Relaxed(new Value(Emit(op, SpvType(t), [.. b.Select(a => a.Id)]), t, false), args); }

    Value ExtFixed(GLSLstd450 op, GlslType type, Value[] args)
    {
        uint set = _m.ImportExtInstSet("GLSL.std.450");
        var instr = InstrPool.Rent(Op.ExtInst, SpvType(type), _m.AllocId()).Add(set).Add((uint)op);
        foreach (var a in args) instr.Add(a.Id);
        _code.Add(instr);
        return Relaxed(new Value(instr.ResultId, type, false), args);
    }

    Value[] Broadcast(Value[] args, GlslType type) =>
        type is VectorType vt ? [.. args.Select(a => a.Type is ScalarType ? Splat(a, vt) : a)] : args;

    static GlslType Widest(Value[] args)
    {
        foreach (var a in args) if (a.Type is VectorType) return a.Type;
        return args[0].Type;
    }

    static GLSLstd450 Fsu(GlslType type, GLSLstd450 f, GLSLstd450 s, GLSLstd450 u) => BaseScalar(type) switch
    {
        var k when IsFloating(k) => f,   // float, double, float16
        var k when IsSigned(k) => s,     // int, int64, int16, int8
        _ => u,                          // uint, uint64, uint16, uint8
    };

    // The comma/sequencing operator: evaluate the left for its side effects, yield the right.
    Value Sequence(Binary seq)
    {
        LowerExpr(seq.Left);
        return LowerExpr(seq.Right);
    }

    static string? RootName(Expr e) => e switch { Ident i => i.Name, Member m => RootName(m.Target), Index ix => RootName(ix.Target), _ => null };

    Value LowerAssign(Binary assign)
    {
        // glslang builds the target's access chain first (which is what creates a lazily-declared
        // local), then evaluates the right-hand side, and only then emits the chain and the store.
        if (assign.Left is Member sw && IsSwizzleName(sw.Name) && InferType(sw.Target) is VectorType swVec)
        {
            ValidateSwizzle(swVec, sw.Name, writeMask: true);
            var swTarget = BuildAccess(sw.Target);
            RejectInputWrite(swTarget.Storage);
            RejectReadOnlyWrite(swTarget);
            return LowerSwizzleAssign(sw, swTarget, LowerExpr(assign.Right));
        }
        // glslang builds the target's access chain (creating its index constants), evaluates the
        // right-hand side, and only then emits the OpAccessChain and OpStore.
        var lhs = BuildAccess(assign.Left);
        RejectInputWrite(lhs.Storage);
        RejectReadOnlyWrite(lhs);
        if (IsOpaque(lhs.Type))
            throw new GlslParseException($"'{Types.Name(lhs.Type)}' is an opaque type and cannot be assigned");
        // A block's chain addresses the STORED type, so a bool member is a uint there. The right-hand
        // side still converts to what was declared, and only then to what the block holds.
        var declared = lhs.Layout is null ? lhs.Type : InferType(assign.Left);
        var value = LowerCoerced(assign.Right, declared, "assignment");
        MultiTypeStore(lhs, ToStored(value, lhs.Type));
        return value;
    }

    // v.xy = rhs: store each right-hand component into the matching vector component (per-component
    // access chain), matching how glslang lowers a swizzle on the left of an assignment.
    Value LowerSwizzleAssign(Member m, Chain chain, Value rhs)
    {
        var vecType = (VectorType)chain.Type;
        int[] indices = [.. m.Name.Select(SwizzleIndex)];
        var component = new ScalarType(vecType.Component);

        // Writing every component in order addresses the whole vector, so it is a plain store.
        if (IsIdentitySwizzle(indices, vecType)) { Store(Collapse(chain), rhs.Id); return rhs; }

        // A partial swizzle store (fewer components than the vector) writes each component individually,
        // matching glslang: for each, emit the component access chain (unsigned index) first, then
        // extract the matching right-hand-side component, then store.
        if (indices.Length < vecType.Size)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                // glslang creates the (unsigned) index constant before the component pointer type, and
                // the component is one more index on the target's chain, not a chain from its result.
                uint idxConst = _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)indices[i]);
                uint ptr = Collapse(chain with { Indices = [.. chain.Indices, idxConst], Type = component });
                uint value = indices.Length == 1 ? rhs.Id : Emit(Op.CompositeExtract, SpvType(component), rhs.Id, (uint)i);
                Store(ptr, value);
            }
            return rhs;
        }

        // A full (possibly reordered) swizzle store loads the vector, shuffles the rhs into place, stores.
        uint whole = Collapse(chain);
        uint loaded = Emit(Op.Load, SpvType(vecType), whole);
        var pick = new uint[vecType.Size];
        for (int j = 0; j < indices.Length; j++) pick[indices[j]] = (uint)(vecType.Size + j);
        var shuffle = InstrPool.Rent(Op.VectorShuffle, SpvType(vecType), _m.AllocId()).Add(loaded, rhs.Id);
        foreach (var idx in pick) shuffle.Add(idx);
        _code.Add(shuffle);
        Store(whole, shuffle.ResultId);
        return rhs;
    }

    Value LowerCompoundAssign(Binary bin)
    {
        if (bin.Left is Member sw && IsSwizzleName(sw.Name) && InferType(sw.Target) is VectorType vecType)
        {
            ValidateSwizzle(vecType, sw.Name, writeMask: true);
            // glslang builds the swizzle target's access chain once (loading any dynamic index like an
            // array subscript), evaluates the right-hand side, then load-modify-stores through that same
            // chain: the base index is not reloaded for the write-back, and the RHS is computed before
            // the current value is loaded.
            var baseChain = BuildAccess(sw.Target);
            RejectInputWrite(baseChain.Storage);
            RejectReadOnlyWrite(baseChain);
            var rhsVal = LowerExpr(bin.Right);
            int[] indices = [.. sw.Name.Select(SwizzleIndex)];
            var component = new ScalarType(vecType.Component);

            // `v.xyz` on a vec3 selects the whole vector in order: glslang drops the swizzle entirely,
            // so this is an ordinary load-modify-store with no shuffling at all.
            if (IsIdentitySwizzle(indices, vecType))
            {
                var whole = ApplyBinary(bin.Op[..^1], Load(vecType, Collapse(baseChain), baseChain.Layout), rhsVal, bin);
                Store(Collapse(baseChain), whole.Id);
                return whole;
            }

            // A single component stays in the access chain, so it loads the scalar directly rather than
            // the whole vector; a wider swizzle has to load the vector and shuffle out the components.
            uint loaded = 0;
            Value curr;
            if (indices.Length == 1)
            {
                uint idxConst = _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)indices[0]);
                curr = Load(component, Collapse(baseChain with { Indices = [.. baseChain.Indices, idxConst], Type = component }));
            }
            else
            {
                // The swizzle's own type is the operation's result type, which glslang has in hand
                // before it loads the vector to shuffle.
                var swType = new VectorType(vecType.Component, indices.Length);
                uint swTypeId = SpvType(swType);
                loaded = Emit(Op.Load, SpvType(vecType), Collapse(baseChain));
                var shuffle = InstrPool.Rent(Op.VectorShuffle, swTypeId, _m.AllocId()).Add(loaded, loaded);
                foreach (var k in indices) shuffle.Add((uint)k);
                _code.Add(shuffle);
                curr = new Value(shuffle.ResultId, swType, false);
            }

            var updated = ApplyBinary(bin.Op[..^1], curr, rhsVal, bin);

            if (indices.Length < vecType.Size)
                for (int k = 0; k < indices.Length; k++)
                {
                    uint idxConst = _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)indices[k]);
                    uint ptr = Collapse(baseChain with { Indices = [.. baseChain.Indices, idxConst], Type = component });
                    uint val = indices.Length == 1 ? updated.Id : Emit(Op.CompositeExtract, SpvType(component), updated.Id, (uint)k);
                    Store(ptr, val);
                }
            else
            {
                var pick = new uint[vecType.Size];
                for (int j = 0; j < indices.Length; j++) pick[indices[j]] = (uint)(vecType.Size + j);
                var back = InstrPool.Rent(Op.VectorShuffle, SpvType(vecType), _m.AllocId()).Add(loaded, updated.Id);
                foreach (var idx in pick) back.Add(idx);
                _code.Add(back);
                Store(Collapse(baseChain), back.ResultId);
            }
            return updated;
        }
        // glslang builds the target's access chain (loading any dynamic index) before evaluating the
        // right-hand side, but emits the OpAccessChain itself only at the load and store. So for an
        // indexed target like `a[i] += a[j]` the right side is computed between the index load and the
        // access chain, and the chain is collapsed separately for the load and the store.
        var chain = BuildAccess(bin.Left);
        RejectInputWrite(chain.Storage);
        RejectReadOnlyWrite(chain);
        var rhs = LowerExpr(bin.Right);
        // A compound assignment L op= R must yield L's type: a scalar target cannot take a vector or matrix
        // right-hand side (that would produce a vector/matrix, which is not assignable back to the scalar).
        if (chain.Type is ScalarType && rhs.Type is VectorType or MatrixType)
            throw new GlslParseException($"cannot assign the result of '{bin.Op}' with '{Types.Name(rhs.Type)}' to '{Types.Name(chain.Type)}'");
        // glslang evaluates the right side, INCLUDING its implicit conversion, before loading the current
        // value, so `f += i` converts i to float ahead of the load. (Shifts keep the amount's own type.)
        if (chain.Type is ScalarType && bin.Op is not ("<<=" or ">>=")) rhs = Coerce(rhs, chain.Type);
        var current = Load(chain.Type, Collapse(chain), chain.Layout);
        var result = ApplyBinary(bin.Op[..^1], current, rhs, bin);
        Store(Collapse(chain), result.Id);
        return result;
    }

    // GLSL '&&' and '||' short-circuit: the right operand must not be evaluated (its side effects
    // must not happen, and it may guard an out-of-bounds access) when the left decides the result.
    // Lower to a selection: evaluate the right side in its own block, then OpPhi the two edges.
    // glslang short-circuits &&/|| only when the right operand is non-trivial (may have side effects);
    // a trivial right operand (symbol/constant/simple comparison/index) is handled as a plain binary op.
    // glslang counts a symbol as a trivial leaf only for a local, a global, a const, a uniform, or a
    // function in/inout parameter. An `out` parameter and a shader in/out varying are NOT trivial, so
    // an `||` whose right side reads one still short circuits. Neither is an anonymous block's member:
    // by its bare name it is still an index into the block, not a symbol.
    bool IsTrivialLeaf(Expr e) => e switch
    {
        Ident id => !_outParams.Contains(id.Name) && !IsVarying(id.Name) && !_blockMembers.ContainsKey(id.Name),
        // Anything that folds is already a constant in glslang's AST by the time it asks, so it counts
        // as a leaf however it was written (`MAX - 1` is just as much a constant as `3`).
        _ => EvalConst(e) is not null,
    };

    // Checked WITHOUT materializing the global: triviality is asked while blocks are being laid out, and
    // building the variable there would allocate its id too early.
    bool IsVarying(string name) => _varyingNames.Contains(name) || Builtins.ContainsKey(name);

    bool IsTrivial(Expr e) => e switch
    {
        // A non-scalar operand cannot short circuit, so glslang treats it as trivial outright.
        _ when InferType(e) is not ScalarType => true,
        // A bare block member is not a leaf but IS an index into the block, which is a trivial
        // operation as long as the block itself is a trivial symbol to read.
        Ident id when _blockMembers.TryGetValue(id.Name, out var bm) => bm.TrivialSymbol,
        Ident or Literal => IsTrivialLeaf(e),
        Unary { Op: "!" } u => IsTrivialLeaf(u.Operand),
        Binary b when b.Op is "==" or "!=" or "<" or ">" or "<=" or ">=" or "^^" => IsTrivialLeaf(b.Left) && IsTrivialLeaf(b.Right),
        Index ix => IsTrivialLeaf(ix.Target) && IsTrivialLeaf(ix.Idx) && EvalConst(ix.Idx) is not null,
        Member m => IsTrivialLeaf(m.Target),
        Call { Name: "any" or "all" } c => c.Args.Count == 1 && IsTrivialLeaf(c.Args[0]),
        _ => false,
    };

    Value LowerShortCircuit(Binary b)
    {
        if (InferType(b.Left) is not ScalarType { Kind: Scalar.Bool } || InferType(b.Right) is not ScalarType { Kind: Scalar.Bool })
            throw new GlslParseException($"operator '{b.Op}' requires boolean operands");
        bool isAnd = b.Op == "&&";
        if (IsTrivial(b.Right))
        {
            uint l = LowerExpr(b.Left).Id, r = LowerExpr(b.Right).Id;
            return new Value(Emit(isAnd ? Op.LogicalAnd : Op.LogicalOr, SpvType(Types.Bool), l, r), Types.Bool, false);
        }
        uint boolType = SpvType(Types.Bool);   // glslang's createShortCircuit makes the bool type first
        var left = LowerExpr(b.Left);
        uint leftBlock = _currentBlock;
        // Both branch to the "then" edge to evaluate the right operand; `&&` does so when the left is
        // true, `||` when it is false. glslang models `||` by branching on an explicit OpLogicalNot of
        // the left rather than by swapping the branch targets.
        uint cond = isAnd ? left.Id : Emit(Op.LogicalNot, boolType, left.Id);
        uint rhsBlock = _m.AllocId(), merge = _m.AllocId();

        SelectionMerge(merge);
        BranchConditional(cond, rhsBlock, merge);

        Label(rhsBlock);
        var right = LowerExpr(b.Right);
        uint rhsEnd = _currentBlock;
        Branch(merge);

        Label(merge);
        // From the left edge the result is the left value itself (false for &&, true for ||).
        uint result = _m.AllocId();
        _code.Add(InstrPool.Rent(Op.Phi, SpvType(Types.Bool), result).Add(left.Id, leftBlock, right.Id, rhsEnd));
        return new Value(result, Types.Bool, false);
    }

    // GLSL implicit conversions only widen: int -> uint/float, uint -> float, half -> float.
    static bool ImplicitAllowed(Scalar from, Scalar to) => (from, to) switch
    {
        (Scalar.Int, Scalar.UInt) or (Scalar.Int, Scalar.Float) or (Scalar.UInt, Scalar.Float) => true,
        (Scalar.Half, Scalar.Float) => true,
        // Anything numeric widens to double.
        (Scalar.Int or Scalar.UInt or Scalar.Float or Scalar.Half, Scalar.Double) => true,
        // An integer widens to a wider integer, and any integer widens to a float.
        (Scalar.Int, Scalar.Int64 or Scalar.UInt64) => true,
        (Scalar.UInt, Scalar.UInt64 or Scalar.Int64) => true,
        (Scalar.Int64, Scalar.UInt64) => true,
        (Scalar.Int64 or Scalar.UInt64, Scalar.Float) => true,
        _ => false,
    };

    static Scalar Wider(Scalar a, Scalar b) =>
        a == Scalar.Double || b == Scalar.Double ? Scalar.Double :
        a == Scalar.Float || b == Scalar.Float ? Scalar.Float :
        a == Scalar.Half || b == Scalar.Half ? Scalar.Half :
        a == Scalar.UInt64 || b == Scalar.UInt64 ? Scalar.UInt64 :
        a == Scalar.Int64 || b == Scalar.Int64 ? Scalar.Int64 :
        a == Scalar.UInt || b == Scalar.UInt ? Scalar.UInt : a;

    // Inserts an implicit widening conversion so a value matches an expected type, where GLSL allows
    // it (e.g. `float x = 0;`). Returns the value unchanged when no legal implicit conversion applies.
    Value Coerce(Value v, GlslType target)
    {
        if (v.Type.Equals(target)) return v;
        if (v.Type is ScalarType s && target is ScalarType t && ImplicitAllowed(s.Kind, t.Kind))
            return ConvertScalar(t.Kind, v);
        if (v.Type is VectorType vv && target is VectorType tv && vv.Size == tv.Size && ImplicitAllowed(vv.Component, tv.Component))
            return NumericConvert(v, tv, vv.Component, tv.Component);
        return v;
    }

    // Like Coerce, but for contexts (assignment, initialization, return, call arguments) where the
    // value MUST match the target: a mismatch with no legal implicit conversion is an error, not a
    // silently mis-typed store.
    // Lowers an expression to a target type. glslang's front end folds an implicit conversion applied to
    // a constant into the constant itself, so `uint i = 0` is a uint constant rather than a converted
    // int one; only a non-constant needs a real conversion op.
    // A nested brace initializer has no type name of its own: GLSL 4.20 gives it the type of the member or
    // element it initializes. Resolve the whole initializer tree against the declared type before lowering,
    // so everything downstream sees ordinary constructor calls.
    Expr ResolveInitializer(Expr e, GlslType target)
    {
        if (e is not Call c) return e;
        var args = c.Args;
        if (InitMemberTypes(target, args.Count) is { } members)
            args = [.. args.Select((a, i) => ResolveInitializer(a, members[i]))];
        // A brace-produced array constructor carries no dimensions (only the explicit `T[N](..)` syntax
        // does), so take the declared type's: without them a `float[2][2]` initializer reads as `float[2]`.
        bool braceArray = c.IsArray && c.ArrayDims is null;
        if (c.Name.Length > 0 && !(braceArray && target is ArrayType or RuntimeArrayType))
            return ReferenceEquals(args, c.Args) ? c : c with { Args = args };
        string name = CtorName(target) ?? throw new GlslParseException($"'{Types.Name(target)}' cannot be initialized from a brace initializer");
        return target is ArrayType or RuntimeArrayType
            ? new Call(name, args, IsArray: true, ArrayDims: ArrayDimsOf(target, args.Count))
            : new Call(name, args);
    }

    // The dimension list naming an array type, outermost first, as constructor syntax expects it.
    static IReadOnlyList<Expr?> ArrayDimsOf(GlslType t, int outerCount)
    {
        var dims = new List<Expr?>();
        for (GlslType cur = t; ; )
        {
            if (cur is ArrayType a) { dims.Add(new Literal(TokenKind.IntConst, a.Length.ToString())); cur = a.Element; }
            else if (cur is RuntimeArrayType r) { dims.Add(new Literal(TokenKind.IntConst, outerCount.ToString())); cur = r.Element; }
            else break;
            outerCount = 0;
        }
        return dims;
    }

    // The types an aggregate's brace items initialize, or null when the shape does not line up.
    static IReadOnlyList<GlslType>? InitMemberTypes(GlslType target, int count) => target switch
    {
        StructType st when st.Fields.Count == count => [.. st.Fields.Select(f => f.Type)],
        ArrayType a when a.Length == count => [.. Enumerable.Repeat(a.Element, count)],
        RuntimeArrayType r => [.. Enumerable.Repeat(r.Element, count)],
        MatrixType m when m.Columns == count => [.. Enumerable.Repeat<GlslType>(new VectorType(m.Component, m.Rows), count)],
        VectorType v when v.Size == count => [.. Enumerable.Repeat<GlslType>(new ScalarType(v.Component), count)],
        _ => null,
    };

    // The name a type is constructed by, or null for one with no constructor syntax.
    static string? CtorName(GlslType t) => t switch
    {
        StructType st => st.Name,
        ScalarType or VectorType or MatrixType => Types.Name(t),
        ArrayType a => CtorName(a.Element),
        RuntimeArrayType r => CtorName(r.Element),
        _ => null,
    };

    Value LowerCoerced(Expr e, GlslType target, string context)
    {
        e = ResolveInitializer(e, target);
        if (!UsesSpecConstant(e) && EvalConst(e) is { } cv && CanImplicitlyConvert(CValType(cv), target)
            && FoldConversion(cv, target) is { } folded)
            return Materialize(folded);
        return CoerceOrThrow(LowerExpr(e), target, context);
    }

    static GlslType CValType(CVal v) => v switch
    {
        CScalar s => new ScalarType(s.Kind),
        CAgg a => a.Type,
        _ => Types.Void,
    };

    static CVal? FoldConversion(CVal v, GlslType target) => (v, target) switch
    {
        (CScalar s, ScalarType st) => ConvertConst(s, st.Kind),
        (CAgg { Type: VectorType sv, Parts: var parts }, VectorType tv) when sv.Size == tv.Size =>
            new CAgg(tv, [.. parts.Select(p => (CVal)ConvertConst((CScalar)p, tv.Component))]),
        _ => null,
    };

    Value CoerceOrThrow(Value v, GlslType target, string context)
    {
        var c = Coerce(v, target);
        if (!c.Type.Equals(target))
            throw new GlslParseException($"cannot convert '{Types.Name(v.Type)}' to '{Types.Name(target)}' for {context}");
        return c;
    }

    // glslang types the AST before lowering, so an implicit conversion of a constant operand is folded
    // into the constant rather than emitted: `someUint < 4` compares against a uint 4, never a converted
    // int one. Shifts keep each operand's own type, so they never promote.
    Value LowerBinary(Binary b)
    {
        if (b.Op is "^^" or "<<" or ">>") return ApplyBinary(b.Op, LowerExpr(b.Left), LowerExpr(b.Right));

        // An implicit conversion is a node wrapped around the operand it applies to, so the left one
        // converts as it is evaluated, before the right side runs, rather than after both are in hand.
        // A constant left operand folds its conversion into the constant (glslang types the AST before
        // lowering) rather than emitting a runtime one, so it is done up front before any int constant is
        // materialized and left unused.
        GlslType? leftPromoted = PromotedType(InferType(b.Left), InferType(b.Right));
        Value l;
        if (leftPromoted is ScalarType lpt && !UsesSpecConstant(b.Left) && EvalConst(b.Left) is { } lcv
            && CValType(lcv) is ScalarType && FoldConversion(lcv, lpt) is { } lfolded)
            l = Materialize(lfolded);
        else
        {
            l = LowerExpr(b.Left);
            if (leftPromoted is { } promoted) l = Coerce(l, promoted);
        }

        if ((l.Type is VectorType lv ? new ScalarType(lv.Component) : l.Type as ScalarType) is { } target
            && !UsesSpecConstant(b.Right) && EvalConst(b.Right) is { } cv
            && CValType(cv) is ScalarType cs && cs.Kind != target.Kind
            && CanImplicitlyConvert(cs, target) && FoldConversion(cv, target) is { } folded)
            return ApplyBinary(b.Op, l, Materialize(folded), b);
        return ApplyBinary(b.Op, l, LowerExpr(b.Right), b);
    }

    // The type an operand takes when promoted against the other, or null when it already has it.
    static GlslType? PromotedType(GlslType self, GlslType other) => (self, other) switch
    {
        (ScalarType a, VectorType v) when a.Kind != v.Component => new ScalarType(v.Component),
        (ScalarType a, ScalarType b) when a.Kind != b.Kind => new ScalarType(Wider(a.Kind, b.Kind)),
        _ => null,
    };

    // Promotes the operands of a binary operation to a common type per GLSL's implicit rules.
    (Value L, Value R) Promote(Value l, Value r)
    {
        if (l.Type is VectorType lvec && r.Type is ScalarType) return (l, Coerce(r, new ScalarType(lvec.Component)));
        if (r.Type is VectorType rvec && l.Type is ScalarType) return (Coerce(l, new ScalarType(rvec.Component)), r);
        if (l.Type is ScalarType ls && r.Type is ScalarType rs && ls.Kind != rs.Kind)
        {
            var target = new ScalarType(Wider(ls.Kind, rs.Kind));
            return (Coerce(l, target), Coerce(r, target));
        }
        return (l, r);
    }

    // GLSL gives an operation the highest precision of its operands, so the result is relaxed only when
    // every operand that carries a precision is relaxed (a literal/constant carries none and adapts).
    static bool RelaxedResult(params Value[] operands) =>
        operands.Any(v => v.Relaxed) && operands.All(v => v.Relaxed || v.IsConstant);

    // Propagates operand precision to a computed result: when the operands are relaxed the result id is
    // decorated RelaxedPrecision (a bool result carries no precision). Mirrors ApplyBinary's relaxing.
    Value Relaxed(Value result, params Value[] operands) =>
        RelaxedResult(operands) && BaseScalar(result.Type) is not Scalar.Bool
            ? result with { Id = Relax(result.Id), Relaxed = true }
            : result;

    // Whether glslang decorates this AST node's operation NoContraction (PreciseAnalysis), given its result.
    bool NoContraction(object? node, GlslType result) =>
        node is not null && _preciseOps.TryGetValue(node, out bool skipInt) && !(skipInt && BaseScalar(result) == Scalar.Int);

    Value ApplyBinary(string op, Value l, Value r, Expr? node = null)
    {
        // Logical ops keep bool operands; shifts keep each operand's own type (glslang does not promote
        // the shift amount to the base type); everything else may need promotion to a common type.
        if (op is not ("&&" or "||" or "^^" or "<<" or ">>"))
            (l, r) = Promote(l, r);
        var result = BinaryResult(op, l, r);
        // A `precise` computation must not be re-associated or fused (e.g. into a multiply-add).
        if (NoContraction(node, result.Type))
            _m.Decorate(result.Id, Decoration.NoContraction);
        // nonuniformEXT is a type qualifier, so an operation on a non-uniform operand is non-uniform too
        // (glslang decorates the result right after NoContraction).
        if (_nonUniformIds.Contains(l.Id) || _nonUniformIds.Contains(r.Id))
        {
            _m.Decorate(result.Id, Decoration.NonUniform);
            _nonUniformIds.Add(result.Id);
        }
        // The operation takes its operands' precision. A bool result carries none.
        return RelaxedResult(l, r) && BaseScalar(result.Type) is not Scalar.Bool
            ? result with { Id = Relax(result.Id), Relaxed = true }
            : result;
    }

    Value BinaryResult(string op, Value l, Value r)
    {
        if (op is "+" or "-" or "*" or "/" or "<" or ">" or "<=" or ">=" && (BaseScalar(l.Type) == Scalar.Bool || BaseScalar(r.Type) == Scalar.Bool))
            throw new GlslParseException($"operator '{op}' does not operate on boolean operands");
        if (op is "&" or "|" or "^" or "<<" or ">>" or "%" && (!IsIntegral(BaseScalar(l.Type)) || !IsIntegral(BaseScalar(r.Type))))
            throw new GlslParseException($"operator '{op}' requires integer operands, not '{Types.Name(l.Type)}' and '{Types.Name(r.Type)}'");
        // '^^' is a logical exclusive-or on bools: it never short-circuits, and is exactly an inequality.
        if (op == "^^")
        {
            if (BaseScalar(l.Type) != Scalar.Bool || BaseScalar(r.Type) != Scalar.Bool)
                throw new GlslParseException($"operator '{op}' requires boolean operands");
            return new Value(Emit(Op.LogicalNotEqual, SpvType(Types.Bool), l.Id, r.Id), Types.Bool, false);
        }
        if (IsComparison(op)) return CompareOperator(op, l, r);
        if (op is "&" or "|" or "^")
        {
            (l, r) = MatchComponents(l, r);
            return new Value(Emit(op switch { "&" => Op.BitwiseAnd, "|" => Op.BitwiseOr, _ => Op.BitwiseXor }, SpvType(l.Type), l.Id, r.Id), l.Type, false);
        }
        if (op is "<<" or ">>") return Shift(op, l, r);
        return Arithmetic(op, l, r);
    }

    // GLSL's component-wise bitwise and shift operators accept a scalar against a vector; SPIR-V requires
    // both operands to share the component count, so the scalar is smeared to a vector of its own component
    // type (glslang emits an OpCompositeConstruct even for a constant scalar).
    (Value, Value) MatchComponents(Value l, Value r)
    {
        if (l.Type is VectorType lv && r.Type is ScalarType) r = Splat(r, new VectorType(BaseScalar(r.Type), lv.Size));
        else if (r.Type is VectorType rv && l.Type is ScalarType) l = Splat(l, new VectorType(BaseScalar(l.Type), rv.Size));
        return (l, r);
    }

    Value Arithmetic(string op, Value l, Value r)
    {
        if (l.Type is not (ScalarType or VectorType or MatrixType) || r.Type is not (ScalarType or VectorType or MatrixType))
            throw new GlslParseException($"operator '{op}' cannot be applied to '{Types.Name(l.Type)}' and '{Types.Name(r.Type)}'");
        if (op == "*")
        {
            // matCxR * vec(C) -> vec(R); matA(Ca,Ra) * matB(Cb,Rb) -> mat(Cb,Ra); vec(R) * matCxR -> vec(C).
            if (l.Type is MatrixType lm && r.Type is VectorType rvv) { if (rvv.Size != lm.Columns) throw MulMismatch(l.Type, r.Type); var res = new VectorType(lm.Component, lm.Rows); return new Value(Emit(Op.MatrixTimesVector, SpvType(res), l.Id, r.Id), res, false); }
            if (l.Type is MatrixType lmm && r.Type is MatrixType rmm) { if (rmm.Rows != lmm.Columns) throw MulMismatch(l.Type, r.Type); var res = new MatrixType(rmm.Columns, lmm.Rows, lmm.Component); return new Value(Emit(Op.MatrixTimesMatrix, SpvType(res), l.Id, r.Id), res, false); }
            if (l.Type is MatrixType && r.Type is ScalarType) return new Value(Emit(Op.MatrixTimesScalar, SpvType(l.Type), l.Id, r.Id), l.Type, false);
            if (l.Type is VectorType lvm && r.Type is MatrixType rmv) { if (lvm.Size != rmv.Rows) throw MulMismatch(l.Type, r.Type); var res = new VectorType(rmv.Component, rmv.Columns); return new Value(Emit(Op.VectorTimesMatrix, SpvType(res), l.Id, r.Id), res, false); }
            if (l.Type is ScalarType && r.Type is MatrixType) return new Value(Emit(Op.MatrixTimesScalar, SpvType(r.Type), r.Id, l.Id), r.Type, false);
        }

        // Matrix +/- is not a single opcode: add/subtract corresponding columns, then rebuild.
        if (l.Type is MatrixType lmat && r.Type is MatrixType && op is "+" or "-")
        {
            uint colType = _m.TypeVector(_m.TypeFloat(32), lmat.Rows);
            uint[] columns = [.. Enumerable.Range(0, lmat.Columns).Select(c =>
            {
                uint lc = Emit(Op.CompositeExtract, colType, l.Id, (uint)c);
                uint rc = Emit(Op.CompositeExtract, colType, r.Id, (uint)c);
                return Emit(op == "+" ? Op.FAdd : Op.FSub, colType, lc, rc);
            })];
            return new Value(Emit(Op.CompositeConstruct, SpvType(l.Type), columns), l.Type, false);
        }

        // Matrix (+ | - | /) scalar is component-wise (matrix * scalar is handled above as MatrixTimesScalar).
        if (l.Type is MatrixType ms && r.Type is ScalarType && op is "+" or "-" or "/")
        {
            var column = new VectorType(Scalar.Float, ms.Rows);
            uint colType = SpvType(column);
            uint splat = Splat(r, column).Id;
            uint[] columns = [.. Enumerable.Range(0, ms.Columns).Select(c =>
                Emit(ArithOp(op, column), colType, Emit(Op.CompositeExtract, colType, l.Id, (uint)c), splat))];
            return new Value(Emit(Op.CompositeConstruct, SpvType(l.Type), columns), l.Type, false);
        }

        if (l.Type.Equals(r.Type))
            return new Value(Emit(ArithOp(op, l.Type), SpvType(l.Type), l.Id, r.Id), l.Type, false);

        // OpVectorTimesScalar is float-only; integer vectors multiply component-wise against a splat.
        if (l.Type is VectorType lv && r.Type is ScalarType)
            return op == "*" && IsFloating(lv.Component)
                ? new Value(Emit(Op.VectorTimesScalar, SpvType(lv), l.Id, r.Id), lv, false)
                : new Value(Emit(ArithOp(op, lv), SpvType(lv), l.Id, Splat(r, lv).Id), lv, false);

        if (l.Type is ScalarType && r.Type is VectorType rv)
            return op == "*" && IsFloating(rv.Component)
                ? new Value(Emit(Op.VectorTimesScalar, SpvType(rv), r.Id, l.Id), rv, false)
                : new Value(Emit(ArithOp(op, rv), SpvType(rv), Splat(l, rv).Id, r.Id), rv, false);

        throw new GlslParseException($"operator '{op}' cannot be applied to '{Types.Name(l.Type)}' and '{Types.Name(r.Type)}'");
    }

    // The comparison OPERATORS (==, !=, <, ...) yield a single bool. On vectors GLSL allows only
    // == and != (relational operators are scalar-only; vectors use lessThan/greaterThan/... which
    // are handled by Componentwise below), and they compare the whole vector: all/any components.
    Value CompareOperator(string op, Value l, Value r)
    {
        if (!l.Type.Equals(r.Type))
            throw new GlslParseException($"cannot compare '{Types.Name(l.Type)}' and '{Types.Name(r.Type)}'");

        if (l.Type is VectorType v)
        {
            if (op is not ("==" or "!="))
                throw new GlslParseException($"operator '{op}' is not defined on vectors in GLSL (use lessThan/greaterThan/...)");
            uint componentwise = Emit(CompareOp(op, v.Component), SpvType(new VectorType(Scalar.Bool, v.Size)), l.Id, r.Id);
            return new Value(Emit(op == "==" ? Op.All : Op.Any, SpvType(Types.Bool), componentwise), Types.Bool, false);
        }
        // ==/!= on structs, matrices and arrays compare member/column/element-wise and fold the results:
        // == is the AND of every part being equal, != the OR of every part differing.
        if (l.Type is StructType or MatrixType or ArrayType)
            return AggregateCompare(op, l.Id, r.Id, l.Type);
        return new Value(Emit(CompareOp(op, BaseScalar(l.Type)), SpvType(Types.Bool), l.Id, r.Id), Types.Bool, false);
    }

    // Recursively compares two aggregates: each scalar/vector part is compared, then the part results are
    // folded with LogicalAnd (==) or LogicalOr (!=). Mirrors glslang's createBinaryOperation for aggregates.
    Value AggregateCompare(string op, uint lId, uint rId, GlslType type)
    {
        bool eq = op == "==";
        SpvType(Types.Bool);   // glslang creates the result bool type before extracting the parts
        if (type is ScalarType st)
            return new Value(Emit(CompareOp(op, st.Kind), SpvType(Types.Bool), lId, rId), Types.Bool, false);
        if (type is VectorType v)
        {
            uint cw = Emit(CompareOp(op, v.Component), SpvType(new VectorType(Scalar.Bool, v.Size)), lId, rId);
            return new Value(Emit(eq ? Op.All : Op.Any, SpvType(Types.Bool), cw), Types.Bool, false);
        }
        GlslType[] parts = type switch
        {
            StructType s => [.. s.Fields.Select(f => f.Type)],
            MatrixType m => [.. Enumerable.Repeat((GlslType)new VectorType(m.Component, m.Rows), m.Columns)],
            ArrayType a => [.. Enumerable.Repeat(a.Element, a.Length)],
            _ => throw new GlslParseException($"'{op}' is not defined on {type}"),
        };
        uint acc = 0;
        for (uint i = 0; i < parts.Length; i++)
        {
            uint le = Emit(Op.CompositeExtract, SpvType(parts[i]), lId, i);
            uint re = Emit(Op.CompositeExtract, SpvType(parts[i]), rId, i);
            uint part = AggregateCompare(op, le, re, parts[i]).Id;
            acc = i == 0 ? part : Emit(eq ? Op.LogicalAnd : Op.LogicalOr, SpvType(Types.Bool), acc, part);
        }
        return new Value(acc, Types.Bool, false);
    }

    // The component-wise comparison FUNCTIONS (lessThan, equal, ...) yield a bvec.
    Value Componentwise(string op, Value l, Value r)
    {
        if (l.Type is not VectorType v || !l.Type.Equals(r.Type))
            throw new GlslParseException($"'{op}' expects two vectors of the same type");
        var result = new VectorType(Scalar.Bool, v.Size);
        return new Value(Emit(CompareOp(op, v.Component), SpvType(result), l.Id, r.Id), result, false);
    }

    Value Shift(string op, Value l, Value r)
    {
        // A vector shifted by a scalar amount smears the amount to a vector matching the base's width
        // (SPIR-V requires equal component counts); the amount keeps its own component type.
        if (l.Type is VectorType lv && r.Type is ScalarType) r = Splat(r, new VectorType(BaseScalar(r.Type), lv.Size));
        Op shift = op == "<<" ? Op.ShiftLeftLogical : IsSigned(BaseScalar(l.Type)) ? Op.ShiftRightArithmetic : Op.ShiftRightLogical;
        return new Value(Emit(shift, SpvType(l.Type), l.Id, r.Id), l.Type, false);
    }

    Value LowerUnary(Unary u)
    {
        if (u.Op is "++" or "--") return LowerIncDec(u);
        if (u.Op is "+" or "-" && u.Operand is Literal lit) return LowerSignedLiteral(u.Op, lit);
        var v = LowerExpr(u.Operand);
        static bool IsIntegral(Scalar s) => s is Scalar.Int or Scalar.UInt or Scalar.Int64 or Scalar.UInt64 or Scalar.UInt8 or Scalar.Int8 or Scalar.Int16 or Scalar.UInt16;
        GlslParseException Wrong() => new($"wrong operand type for unary '{u.Op}': '{Types.Name(v.Type)}'");
        return u.Op switch
        {
            "+" or "-" when BaseScalar(v.Type) == Scalar.Bool => throw Wrong(),
            "+" => v,
            "-" when v.Type is MatrixType mt => NegateMatrix(v, mt),
            "-" => Negate(u, v),
            "!" => v.Type is ScalarType { Kind: Scalar.Bool }
                ? new Value(Emit(Op.LogicalNot, SpvType(Types.Bool), v.Id), Types.Bool, false) : throw Wrong(),
            "~" => IsIntegral(BaseScalar(v.Type))
                ? new Value(Emit(Op.Not, SpvType(v.Type), v.Id), v.Type, false) : throw Wrong(),
            _ => throw new GlslParseException($"unary '{u.Op}' is not lowered yet"),
        };
    }

    Value Negate(Unary u, Value v)
    {
        uint id = Emit(IsFloating(BaseScalar(v.Type)) ? Op.FNegate : Op.SNegate, SpvType(v.Type), v.Id);
        if (NoContraction(u, v.Type)) _m.Decorate(id, Decoration.NoContraction);
        return new Value(id, v.Type, false);
    }

    // SPIR-V negates scalars and vectors only: glslang's createUnaryMatrixOperation negates column by column
    // (extract, negate per column) and rebuilds the matrix.
    Value NegateMatrix(Value v, MatrixType mt)
    {
        var column = new VectorType(mt.Component, mt.Rows);
        var cols = new uint[mt.Columns];
        for (int c = 0; c < mt.Columns; c++)
        {
            uint part = Emit(Op.CompositeExtract, SpvType(column), v.Id, (uint)c);
            cols[c] = Emit(Op.FNegate, SpvType(column), part);
        }
        return new Value(Emit(Op.CompositeConstruct, SpvType(mt), cols), mt, false);
    }

    Value LowerIncDec(Unary u)
    {
        var (pointer, type) = ResolveLValue(u.Operand);
        var current = Load(type, pointer);
        // glslang adds a SIGNED one to an integer of either signedness and smears it to a vector of the
        // ONE's own type, relying on OpIAdd ignoring signedness rather than matching component types.
        var oneScalar = IsFloating(BaseScalar(type)) ? BaseScalar(type)
                      : Is64Bit(BaseScalar(type)) ? Scalar.Int64 : Scalar.Int;
        uint one = IncrementOne(BaseScalar(type));
        if (type is VectorType vt)
            one = Emit(Op.CompositeConstruct, SpvType(new VectorType(oneScalar, vt.Size)), [.. Enumerable.Repeat(one, vt.Size)]);
        uint id = Emit(ArithOp(u.Op == "++" ? "+" : "-", type), SpvType(type), current.Id, one);
        if (NoContraction(u, type)) _m.Decorate(id, Decoration.NoContraction);
        if (current.Relaxed) Relax(id);
        var updated = new Value(id, type, false, current.Relaxed);
        Store(pointer, updated.Id);
        return u.Prefix ? updated : current;
    }

    uint IncrementOne(Scalar kind) => kind switch
    {
        Scalar.Float => _m.ConstantFloat(SpvType(Types.Float), 1f),
        Scalar.Double => _m.ConstantDouble(SpvType(Types.Double), 1.0),
        Scalar.Half => _m.ConstantFloat(SpvType(Types.Half), 1f),
        Scalar.Int64 or Scalar.UInt64 => _m.ConstantInt64(SpvType(Types.Int64), 1),
        _ => _m.ConstantInt(SpvType(Types.Int), 1),
    };

    // A signed literal like -1.0 folds to a single negated constant, matching glslang; this
    // keeps const initializers (and their array/vector aggregates) genuinely constant.
    Value LowerSignedLiteral(string op, Literal lit)
    {
        if (op == "+") return LowerLiteral(lit);
        return lit.Kind switch
        {
            TokenKind.FloatConst => new Value(_m.ConstantFloat(SpvType(Types.Float), -ParseFloat(lit.Text)), Types.Float, true),
            TokenKind.DoubleConst => new Value(_m.ConstantDouble(SpvType(Types.Double), -ParseDouble(lit.Text)), Types.Double, true),
            TokenKind.HalfConst => new Value(_m.ConstantHalf(SpvType(Types.Half), -ParseFloat(lit.Text)), Types.Half, true),
            TokenKind.IntConst => new Value(_m.ConstantInt(SpvType(Types.Int), unchecked((uint)-(int)ParseIntLiteral(lit.Text))), Types.Int, true),
            TokenKind.UintConst => new Value(_m.ConstantInt(SpvType(Types.UInt), unchecked((uint)-(int)ParseIntLiteral(lit.Text))), Types.UInt, true),
            TokenKind.Int64Const => new Value(_m.ConstantInt64(SpvType(Types.Int64), unchecked((ulong)-(long)ParseInt64Literal(lit.Text))), Types.Int64, true),
            TokenKind.Uint64Const => new Value(_m.ConstantInt64(SpvType(Types.UInt64), unchecked((ulong)-(long)ParseInt64Literal(lit.Text))), Types.UInt64, true),
            _ => throw new GlslParseException($"unary '-' on literal '{lit.Text}' is not lowered yet"),
        };
    }

    // GLSL '?:' evaluates only the selected branch, so it lowers to a real selection with an OpPhi,
    // not OpSelect (which would evaluate both arms). The condition is always a scalar bool.
    Value LowerConditional(Conditional t) => LowerConditional(t, keepTemp: false, out _);

    // keepTemp: when the ternary branches, hand back its Function temporary as an access chain instead of
    // loading it. glslang's result IS that l-value, so a call argument copies it only in its own step 2.
    Value LowerConditional(Conditional t, bool keepTemp, out Chain? tempChain)
    {
        tempChain = null;
        if (InferType(t.Cond) is not ScalarType { Kind: Scalar.Bool })
            throw new GlslParseException($"a ternary condition must be a boolean expression, not '{Types.Name(InferType(t.Cond))}'");
        // A constant condition picks its arm at compile time (glslang folds this in the AST), so a fully
        // constant ternary collapses to a single constant rather than an OpSelect or a branch.
        if (EvalConst(t) is { } folded) return Materialize(folded);

        // The two arms must share a type; GLSL applies its usual implicit conversion to unify them
        // (e.g. `c ? 1 : 2.0` -> both float), so both edges into the OpPhi carry the same type.
        var common = CommonType(InferType(t.Then), InferType(t.Else));

        // glslang evaluates both arms and uses OpSelect when the result is a scalar/vector and both
        // arms are a symbol or a constant (a variable reference or a constant-foldable expression, e.g.
        // a literal vec3 constructor); otherwise it branches with an OpPhi. A member of an anonymous
        // block reads as a bare name but is an index into the block, not a symbol, so it forces a branch.
        bool ConstOrSymbol(Expr e) =>
            (e is Ident sym && !_blockMembers.ContainsKey(sym.Name)) || EvalConst(e) is not null;
        // SPIR-V 1.4 lets OpSelect take any composite and a scalar condition for vectors, so glslang selects
        // matrices, arrays and structs too and stops smearing the condition (isOpSelectable/handleAsOpSelect).
        bool selectable = common is ScalarType or VectorType
            || version >= SpirVVersion.V1_4 && common is MatrixType or ArrayType or StructType;
        if (selectable && ConstOrSymbol(t.Then) && ConstOrSymbol(t.Else))
        {
            uint condId = LowerExpr(t.Cond).Id;
            // A constant arm folds to the common type (glslang folds it), so `c ? 1 : 2.0` selects between
            // two float constants rather than converting the int at runtime. glslang does not decorate the
            // OpSelect result of a ternary with RelaxedPrecision, so neither do we.
            uint thenVal = LowerCoerced(t.Then, common, "ternary").Id;
            uint elseVal = LowerCoerced(t.Else, common, "ternary").Id;
            if (common is VectorType cv && version < SpirVVersion.V1_4)
                condId = Splat(new Value(condId, Types.Bool, false), new VectorType(Scalar.Bool, cv.Size)).Id;
            return new Value(Emit(Op.Select, SpvType(common), condId, thenVal, elseVal), common, false);
        }

        // Otherwise only one side may be evaluated, so glslang emits control flow around a Function-local
        // temporary (created after the condition): each arm stores into it and the result is a load, and
        // it deliberately leaves the temp for later SSA passes rather than emitting an OpPhi itself.
        var cond = LowerExpr(t.Cond);
        uint tempPtr = _m.TypePointer(StorageClass.Function, SpvType(common));   // glslang allocates the pointer type before the temp's id
        uint temp = _m.AllocId();
        _vars.Add(InstrPool.Rent(Op.Variable, tempPtr, temp).AddEnum(StorageClass.Function));

        uint thenB = _m.AllocId(), merge = _m.AllocId();
        SelectionMerge(merge);
        int branchAt = _code.Count;   // the OpBranchConditional belongs here, once the else block exists

        Label(thenB);
        MultiTypeStore(new Chain(temp, [], common, StorageClass.Function), LowerCoerced(t.Then, common, "ternary"));   // glslang multiTypeStore: a block-member arm copies logically
        Branch(merge);

        uint elseB = _m.AllocId();
        Label(elseB);
        MultiTypeStore(new Chain(temp, [], common, StorageClass.Function), LowerCoerced(t.Else, common, "ternary"));   // glslang multiTypeStore: a block-member arm copies logically
        Branch(merge);

        _code.Insert(branchAt, InstrPool.Rent(Op.BranchConditional).Add(cond.Id, thenB, elseB));
        Label(merge);
        if (keepTemp) { tempChain = new Chain(temp, [], common, StorageClass.Function); return default; }
        return Load(common, temp);
    }

    // The common type two values are promoted to under GLSL's implicit conversions.
    static GlslType CommonType(GlslType a, GlslType b) =>
        a.Equals(b) ? a
        : a is ScalarType sa && b is ScalarType sb ? new ScalarType(Wider(sa.Kind, sb.Kind))
        : a is VectorType va && b is VectorType vb && va.Size == vb.Size ? new VectorType(Wider(va.Component, vb.Component), va.Size)
        : a;

    uint LowerLValue(Expr target) => ResolveAccess(target).Pointer;

    // A constant index into a constant array folds to the element constant (glslang's front-end does
    // this in the AST); a dynamic index, or indexing a real variable, loads through an access chain.
    Value LowerIndex(Index ix)
    {
        CheckConstIndexBounds(ix);
        if (EvalConst(ix) is { } c) return Materialize(c);
        var a = ResolveAccess(ix);
        return FromStored(LoadFrom(a), InferType(ix));
    }

    // A constant index into a fixed-size aggregate must be within bounds (glslang errors at compile time).
    void CheckConstIndexBounds(Index ix)
    {
        if (EvalConst(ix.Idx) is not CScalar { Kind: Scalar.Int or Scalar.UInt } idx) return;
        var target = InferType(ix.Target);
        int? bound = target switch
        {
            ArrayType a when a.SpecLength is null && a.Length > 0 => a.Length,
            VectorType v => v.Size,
            MatrixType m => m.Columns,
            _ => null,
        };
        if (bound is int n && (idx.I < 0 || idx.I >= n))
            throw new GlslParseException($"index {idx.I} is out of bounds for '{Types.Name(target)}' with {n} element{(n == 1 ? "" : "s")}");
    }

    // arr.length(): a fixed array folds to its constant size; a storage-buffer runtime array uses
    // OpArrayLength on the enclosing block (its result is uint, which GLSL's int length() bitcasts).
    Value LowerMethod(Method meth)
    {
        if (meth.Name != "length" || meth.Args.Count != 0)
            throw new GlslParseException($"method '.{meth.Name}()' is not lowered yet");

        switch (InferType(meth.Target))
        {
            case ArrayType arr:
                return new Value(_m.ConstantInt(SpvType(Types.Int), (uint)arr.Length), Types.Int, true);
            case RuntimeArrayType when meth.Target is Member m:
                var block = ResolveAccess(m.Target);
                if (block.Type is not StructType st) throw new GlslParseException("runtime-array length requires a buffer-block member");
                return ArrayLength(block.Pointer, IndexOfField(st, m.Name));
            // The same member reached by its bare name, from an anonymous buffer block.
            case RuntimeArrayType when meth.Target is Ident bare && _blockMembers.TryGetValue(bare.Name, out var bm):
                return ArrayLength(bm.Var(), bm.Index);
            default:
                throw new GlslParseException("'.length()' is only valid on arrays");
        }
    }

    // OpArrayLength yields a uint, which GLSL's int-returning length() bitcasts.
    Value ArrayLength(uint blockPointer, int member)
    {
        uint length = Emit(Op.ArrayLength, SpvType(Types.UInt), blockPointer, (uint)member);
        return new Value(Emit(Op.Bitcast, SpvType(Types.Int), length), Types.Int, false);
    }

    readonly record struct Access(uint Pointer, GlslType Type, StorageClass Storage, BlockLayout? Layout = null, bool Physical = false, MemQual Mem = MemQual.None, int PhysAlign = 0, int PhysOffset = 0, uint Root = 0, int MemberIdx = -1);

    // A not-yet-emitted access: the root pointer plus the sequence of index ids. glslang builds this
    // structure (creating the index constants) while visiting the l-value, then emits ONE collapsed
    // OpAccessChain at load/store time - crucially, for an assignment, after the right-hand side.
    // DeferredComp is a trailing vector-component index (`v.x`) that glslang materializes as a constant
    // only when the chain is collapsed/loaded, not when it is built; -1 means there is none.
    // Swizzle mirrors glslang's access chain: a multi-component swizzle of an l-value is not code, it
    // rides along until the load, which reads the whole vector and shuffles.
    // Layout is the block ruleset the chain is rooted in (null outside a block); it selects which of a
    // struct's two SPIR-V types the pointee resolves to.
    readonly record struct Chain(uint Base, uint[] Indices, GlslType Type, StorageClass Storage, int DeferredComp = -1, int[]? Swizzle = null, BlockLayout? Layout = null, bool Physical = false, MemQual Mem = MemQual.None, int PhysAlign = 0, int PhysOffset = 0);

    // You cannot access-chain into an OpConstantComposite, so a dynamic index into a const array copies
    // it into a Function-local "indexable" temp. glslang does that where the chain is LOADED, not where
    // it was built, and makes a fresh temp for every such access.
    Value IndexableLoad(uint constId, Value index, ArrayType type)
    {
        uint temp = IndexableTemp(constId, _m.TypePointer(StorageClass.Function, SpvType(type)));
        return Load(type.Element, Collapse(new Chain(temp, [index.Id], type.Element, StorageClass.Function)));
    }

    // The Function-local copy of a const array, for dynamic indexing. SPIR-V 1.4 uses an OpVariable
    // initializer and marks it NonWritable; before that glslang stores the constant separately.
    uint IndexableTemp(uint constId, uint ptrType)
    {
        uint temp = _m.AllocId();
        _m.AddName(temp, "indexable");
        if (version >= SpirVVersion.V1_4)
        {
            _m.Decorate(temp, Decoration.NonWritable);
            _vars.Add(InstrPool.Rent(Op.Variable, ptrType, temp).AddEnum(StorageClass.Function).Add(constId));
        }
        else
        {
            _vars.Add(InstrPool.Rent(Op.Variable, ptrType, temp).AddEnum(StorageClass.Function));
            Store(temp, constId);
        }
        return temp;
    }

    Value LoadChain(Chain c)
    {
        var loaded = Load(c.Type, Collapse(c), c.Layout, c.Mem);
        // A value read from a relaxed symbol is itself relaxed (the access chain stays undecorated).
        if (!loaded.Relaxed && _relaxedIds.Contains(c.Base) && BaseScalar(c.Type) is not Scalar.Bool)
            loaded = loaded with { Id = Relax(loaded.Id), Relaxed = true };
        return c.Swizzle is null ? loaded : Shuffle(loaded, c.Swizzle);
    }

    // Writes back through a saved l-value chain. A swizzle cannot be stored through as one pointer, so
    // each selected component gets its own access chain, exactly like a partial swizzle assignment.
    void StoreThroughChain(Chain target, Value value)
    {
        RejectInputWrite(target.Storage);
        RejectReadOnlyWrite(target);
        if (target.Swizzle is null) { Store(Collapse(target), value.Id, target.Mem); return; }
        var component = new ScalarType(((VectorType)target.Type).Component);
        for (int i = 0; i < target.Swizzle.Length; i++)
        {
            uint idx = _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)target.Swizzle[i]);
            uint ptr = Collapse(target with { Indices = [.. target.Indices, idx], Type = component, Swizzle = null });
            Store(ptr, Emit(Op.CompositeExtract, SpvType(component), value.Id, (uint)i), target.Mem);
        }
    }

    Value Shuffle(Value src, int[] select)
    {
        var component = ((VectorType)src.Type).Component;
        // A single component rides in the chain as an index, so its unsigned constant is created even
        // though the extract that ends up reading it takes a literal.
        if (select.Length == 1)
        {
            var scalar = new ScalarType(component);
            _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)select[0]);
            return new Value(Emit(Op.CompositeExtract, SpvType(scalar), src.Id, (uint)select[0]), scalar, false);
        }
        var result = new VectorType(component, select.Length);
        var sh = InstrPool.Rent(Op.VectorShuffle, SpvType(result), _m.AllocId()).Add(src.Id, src.Id);
        foreach (int i in select) sh.Add((uint)i);
        _code.Add(sh);
        return new Value(sh.ResultId, result, false);
    }

    // Emits the single collapsed OpAccessChain (or just returns the base when there are no indices).
    uint Collapse(Chain c)
    {
        uint[] indices = c.DeferredComp >= 0
            ? [.. c.Indices, _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)c.DeferredComp)]
            : c.Indices;
        if (indices.Length == 0) return c.Base;
        uint ptrType = _m.TypePointer(c.Storage, SpvType(c.Type, c.Layout));
        uint result = Emit(Op.AccessChain, ptrType, [c.Base, .. indices]);
        if (indices.Any(_nonUniformIds.Contains)) { _m.Decorate(result, Decoration.NonUniform); _nonUniformIds.Add(result); }
        return result;
    }

    Access ResolveAccess(Expr e) { var c = BuildAccess(e); return new Access(Collapse(c), c.Type, c.Storage, c.Layout, c.Physical, c.Mem, c.PhysAlign, c.PhysOffset, c.Base, MemberIndexOfChain(c)); }

    // The Aligned memory operand for a buffer_reference access: the low bit of the reference alignment
    // combined with the accessed member's byte offset (glslang takes the LSB of alignment | offset).
    static uint PhysAligned(int align, int offset) { int a = align | offset; return (uint)(a & -a); }

    // An unsized descriptor array (`sampler2D t[]`, `buffer B {...} b[]`) is a runtime descriptor array.
    void DeclareRuntimeDescriptorArray()
    {
        _m.AddCapability(Capability.RuntimeDescriptorArray);
        AddIncorporatedExtension("SPV_EXT_descriptor_indexing", SpirVVersion.V1_5);
    }

    // The capability for non-uniformly indexing a descriptor array, keyed by the indexed resource type.
    void AddNonUniformIndexCap(GlslType element, StorageClass storage) => _m.AddCapability(element switch
    {
        ImageType => Capability.StorageImageArrayNonUniformIndexing,
        SamplerType or TextureType => Capability.SampledImageArrayNonUniformIndexing,
        // Keyed by the GLSL qualifier, as glslang does: below 1.3 an SSBO array is Uniform-class too.
        StructType st => storage == StorageClass.Uniform && !_bufferBlockStructs.Contains(st)
            ? Capability.UniformBufferArrayNonUniformIndexing : Capability.StorageBufferArrayNonUniformIndexing,
        _ => Capability.ShaderNonUniform,
    });

    // SPIR-V memory-access operand bits (OpLoad/OpStore).
    const uint Volatile = 0x1, MakePointerAvailable = 0x8, MakePointerVisible = 0x10, NonPrivatePointer = 0x20;

    // Under the Vulkan memory model a coherent/volatile access carries a NonPrivatePointer +
    // MakePointerAvailable (store) or MakePointerVisible (load) operand, scoped to the queue family.
    (uint Mask, uint Scope)? VmmOperands(MemQual mem, bool store)
    {
        if (!_vmmPragma || mem == MemQual.None) return null;
        uint mask = NonPrivatePointer | (store ? MakePointerAvailable : MakePointerVisible);
        if (mem == MemQual.Volatile) mask |= Volatile;
        uint scope = mem == MemQual.Shared ? ScopeWorkgroup : ScopeQueueFamily;
        return (mask, _m.ConstantInt(SpvType(Types.UInt), scope));
    }

    // A read through a PhysicalStorageBuffer (buffer_reference) pointer carries an Aligned memory
    // operand with the type's std430 alignment, exactly like the matching store.
    Value LoadFrom(Access a)
    {
        RejectWriteOnlyRead(a.Root, a.MemberIdx);
        return LoadFromCore(a);
    }

    Value LoadFromCore(Access a) => a.Physical
        ? new Value(Emit(Op.Load, SpvType(a.Type, a.Layout), a.Pointer, 0x2, PhysAligned(a.PhysAlign, a.PhysOffset)), a.Type, false, false, a.Layout)
        : Load(a.Type, a.Pointer, a.Layout, a.Mem);

    uint EnsureGlPerVertex() { if (_glPerVertex == 0) CreateGlPerVertex(); return _glPerVertex; }
    // A constant index outside a sized array/vector/matrix is a source error. Left unchecked it produces
    // an out-of-bounds access chain that later passes can trip over, so diagnose it here.
    // The index of a builtin per-vertex/per-primitive array (gl_in/gl_out/gl_MeshVerticesEXT/...) must
    // be an INTEGER expression, and a compile-time-constant one must be in range: both would otherwise
    // emit an OpAccessChain spirv-val rejects (negative constant index / non-integer index). These arrays
    // are lowered through bespoke chain cases, so the general index checks must be applied here.
    uint BuiltinArrayIndex(Expr indexExpr, string arrayName, int? bound)
    {
        if (InferType(indexExpr) is { } t && t is not ScalarType { Kind: Scalar.Int or Scalar.UInt })
            throw new GlslParseException($"can only index '{arrayName}' with an integer expression, not '{Types.Name(t)}'", indexExpr.Line);
        if (bound is int n && EvalConst(indexExpr) is CScalar { Kind: Scalar.Int or Scalar.UInt } c && (c.I < 0 || c.I >= n))
            throw new GlslParseException($"vertex index {c.I} is out of range for '{arrayName}' (size {n})", indexExpr.Line);
        return LowerExpr(indexExpr).Id;
    }

    void RejectOutOfRangeIndex(GlslType container, Expr indexExpr)
    {
        // The index must be an INTEGER expression: a float/bool index would emit an invalid
        // OpAccessChain (spirv-val: "Indexes passed to OpAccessChain must be of type integer").
        if (InferType(indexExpr) is { } it && it is not ScalarType { Kind: Scalar.Int or Scalar.UInt })
            throw new GlslParseException($"can only index '{Types.Name(container)}' with an integer expression, not '{Types.Name(it)}'", indexExpr.Line);
        if (EvalConst(indexExpr) is not CScalar c) return;
        long i = c.I;
        int? bound = container switch
        {
            // A specialization-sized array has no compile-time length to check against.
            ArrayType { SpecLength: not null } => null,
            ArrayType a => a.Length,
            VectorType v => v.Size,
            MatrixType m => m.Columns,
            _ => null,
        };
        if (bound is null) return;
        if (i < 0 || i >= bound)
            throw new GlslParseException($"index {i} is out of range for '{Types.Name(container)}' (size {bound})");
    }

    uint MemberIndexConst(int index)
    {
        uint id = _m.ConstantInt(_m.TypeInt(32, signed: true), (uint)index);
        _memberIndexOf[id] = index;
        return id;
    }
    // Reverse of MemberIndexConst: the member index a constant id stands for, used to decide whether a
    // chain addresses a member that was declared readonly.
    readonly Dictionary<uint, int> _memberIndexOf = [];

    // Walks a chain of identifiers, array indices, and struct-field members into a (base, indices)
    // pair without emitting the OpAccessChain. Swizzles are not pointer chains and are handled by load.
    Chain BuildAccess(Expr e)
    {
        switch (e)
        {
            // gl_in[i].<member> / gl_out[i].<member>: a member of the per-vertex array element.
            case Member { Target: Index { Target: Ident { Name: "gl_in" or "gl_out" } arr, Idx: var vtx }, Name: var pvm }
                    when MeshVertexMember(pvm) is (int pvi, GlslType pvt):
            {
                bool input = arr.Name == "gl_in";
                var storage = input ? StorageClass.Input : StorageClass.Output;
                uint variable = input
                    ? GlPerVertexArray(ref _glIn, storage, _inVertexCount, "gl_in")
                    : GlPerVertexArray(ref _glOut, storage, _outVertexCount, "gl_out");
                if (pvi == 1) NeedPointSizeCap();
                return new Chain(variable,
                    [BuiltinArrayIndex(vtx, arr.Name, input ? _inVertexCount : _outVertexCount), MemberIndexConst(pvi)], pvt, storage);
            }
            // gl_MeshVerticesEXT[v].<member> writes a per-output-vertex value (gl_Position, gl_PointSize, ...).
            case Member { Target: Index { Target: Ident { Name: "gl_MeshVerticesEXT" }, Idx: var mvtx }, Name: var mm }
                    when MeshVertexMember(mm) is (int mi, GlslType mt):
                return new Chain(GlMeshVertices(), [BuiltinArrayIndex(mvtx, "gl_MeshVerticesEXT", _meshMaxVertices), MemberIndexConst(mi)], mt, StorageClass.Output);
            // gl_MeshPrimitivesEXT[p].<member> writes a per-primitive builtin (gl_PrimitiveID/gl_Layer/...).
            case Member { Target: Index { Target: Ident { Name: "gl_MeshPrimitivesEXT" }, Idx: var mprim }, Name: var pm }
                    when MeshPrimitiveMember(pm) is (int pmi, GlslType pmt):
                return new Chain(GlMeshPrimitives(), [BuiltinArrayIndex(mprim, "gl_MeshPrimitivesEXT", _meshMaxPrimitives), MemberIndexConst(pmi)], pmt, StorageClass.Output);
            // GL_NV_mesh_shader: gl_MeshVerticesNV[v].<member>, gl_PrimitiveIndicesNV[i], gl_PrimitiveCountNV.
            case Member { Target: Index { Target: Ident { Name: "gl_MeshVerticesNV" }, Idx: var nvtx }, Name: var nm }
                    when MeshVertexMember(nm) is (int ni, GlslType nt):
                return new Chain(GlMeshVerticesNV(), [BuiltinArrayIndex(nvtx, "gl_MeshVerticesNV", _meshMaxVertices), MemberIndexConst(ni)], nt, StorageClass.Output);
            case Index { Target: Ident { Name: "gl_PrimitiveIndicesNV" }, Idx: var pidx }:
                return new Chain(GlPrimitiveIndicesNV(), [BuiltinArrayIndex(pidx, "gl_PrimitiveIndicesNV", null)], Types.UInt, StorageClass.Output);
            case Ident { Name: "gl_PrimitiveCountNV" }:
                return new Chain(GlPrimitiveCountNV(), [], Types.UInt, StorageClass.Output);
            // gl_Primitive{Triangle|Line|Point}IndicesEXT[p] is the per-primitive index output.
            case Index { Target: Ident { Name: "gl_PrimitiveTriangleIndicesEXT" or "gl_PrimitiveLineIndicesEXT" or "gl_PrimitivePointIndicesEXT" }, Idx: var prim }:
            {
                var (var2, element) = GlMeshIndices();
                return new Chain(var2, [BuiltinArrayIndex(prim, "gl_PrimitiveIndicesEXT", null)], element, StorageClass.Output);
            }
            case Ident { Name: "gl_TessLevelOuter" }: { var a = TessLevelAccess(ref _glTessOuter, BuiltIn.TessLevelOuter, 4); return new Chain(a.Pointer, [], a.Type, a.Storage); }
            case Ident { Name: "gl_TessLevelInner" }: { var a = TessLevelAccess(ref _glTessInner, BuiltIn.TessLevelInner, 2); return new Chain(a.Pointer, [], a.Type, a.Storage); }
            case Ident { Name: "gl_Position" }:
            {
                uint pv = EnsureGlPerVertex();
                var (mi, mt) = PerVertexOutMember("gl_Position");
                return new Chain(pv, [MemberIndexConst(mi)], mt, StorageClass.Output);
            }
            case Ident { Name: "gl_PointSize" }:
            {
                NeedPointSizeCap();
                uint pv = EnsureGlPerVertex();
                var (mi, mt) = PerVertexOutMember("gl_PointSize");
                return new Chain(pv, [MemberIndexConst(mi)], mt, StorageClass.Output);
            }
            case Ident { Name: "gl_ClipDistance" }:
                _m.AddCapability(Capability.ClipDistance);
                // In a fragment shader gl_ClipDistance is a read-only Input array, not a gl_PerVertex
                // output member (emitting the gl_PerVertex Position block in a fragment shader is invalid).
                if (stage == ShaderStage.Fragment)
                    return new Chain(ClipCullInput(ref _glClipFrag, BuiltIn.ClipDistance), [], ClipCullType, StorageClass.Input);
                { uint pv = EnsureGlPerVertex(); var (mi, mt) = PerVertexOutMember("gl_ClipDistance"); return new Chain(pv, [MemberIndexConst(mi)], mt, StorageClass.Output); }
            case Ident { Name: "gl_CullDistance" }:
                _m.AddCapability(Capability.CullDistance);
                if (stage == ShaderStage.Fragment)
                    return new Chain(ClipCullInput(ref _glCullFrag, BuiltIn.CullDistance), [], ClipCullType, StorageClass.Input);
                { uint pv = EnsureGlPerVertex(); var (mi, mt) = PerVertexOutMember("gl_CullDistance"); return new Chain(pv, [MemberIndexConst(mi)], mt, StorageClass.Output); }
            case Ident id when Builtins.TryGetValue(id.Name, out var b):
                var (bt, bv) = BuiltinVar(id.Name);
                return new Chain(bv, [], bt, BuiltinIsOutput(id.Name, b.Output) ? StorageClass.Output : StorageClass.Input);
            case Ident id when ResolveLocalVar(id.Name) is { } local:
                return new Chain(local.VarId, [], local.Type, StorageClass.Function);
            case Ident id when ResolveGlobal(id.Name) is { } g:
                return new Chain(g.VarId, [], g.Type, g.Storage, Layout: BlockRuleset(g.Type), Mem: g.Mem);
            case Ident id when _blockMembers.TryGetValue(id.Name, out var bm):
                return new Chain(bm.Var(), [MemberIndexConst(bm.Index)], BlockStored(bm.Type), bm.Storage, Layout: bm.Layout, Mem: bm.Mem);
            // A dynamic index into a const array: glslang copies the constant into a Function-local
            // "indexable" temp (you cannot access-chain into an OpConstantComposite) and indexes that.
            case Index { Target: Ident cid } ix when _constValues.TryGetValue(cid.Name, out var cv)
                    && cv is CAgg { Type: ArrayType arrT } && EvalConst(ix.Idx) is null:
            {
                // glslang order: the constant, then the index load, then the temp's pointer/variable, then
                // the store. A fresh "indexable" temp is created for EACH such access (glslang does not
                // cache it), so two `arr[i]` uses produce two temps.
                uint constId = Materialize(cv).Id;
                uint index = LowerExpr(ix.Idx).Id;
                uint temp = IndexableTemp(constId, _m.TypePointer(StorageClass.Function, SpvType(arrT)));
                return new Chain(temp, [index], arrT.Element, StorageClass.Function);
            }
            case Index ix:
            {
                var baseC = BuildAccess(ix.Target);
                // Bounds/type checks BEFORE the deferred-comp early return below: `v[-1] = 1.0` used to
                // slip through as deferred component -1 and store through the whole-vector pointer.
                RejectOutOfRangeIndex(baseC.Type, ix.Idx);
                // A constant index into a vector is a one-component swizzle in glslang, not a real
                // index: it becomes an UNSIGNED constant, created only when the chain is collapsed.
                if (baseC.Type is VectorType civ && EvalConst(ix.Idx) is CScalar { Kind: Scalar.Int or Scalar.UInt } ci)
                    return baseC with { Type = new ScalarType(civ.Component), DeferredComp = (int)ci.I };
                GlslType element = baseC.Type switch
                {
                    ArrayType arr => arr.Element,
                    RuntimeArrayType rarr => rarr.Element,
                    MatrixType mt => new VectorType(mt.Component, mt.Rows),   // matrix[i] selects column i
                    VectorType vt => new ScalarType(vt.Component),            // vector[i] / matrix[i][j] selects a component
                    _ => throw new GlslParseException($"cannot index into '{Types.Name(baseC.Type)}'"),
                };
                uint idx = LowerExpr(ix.Idx).Id;
                if (_nonUniformIds.Contains(idx)) AddNonUniformIndexCap(element, baseC.Storage);
                // A constant array index through a buffer_reference adds its byte offset to the Aligned
                // operand computation; a dynamic index contributes nothing (as glslang pushes zero).
                int idxOff = baseC.Physical && EvalConst(ix.Idx) is CScalar cs ? (int)cs.I * Stride(element, BlockLayout.Std430) : 0;
                return new Chain(baseC.Base, [.. baseC.Indices, idx], element, baseC.Storage, Layout: baseC.Layout, Physical: baseC.Physical, Mem: baseC.Mem, PhysAlign: baseC.PhysAlign, PhysOffset: baseC.PhysOffset + idxOff);
            }
            // `ref.field` dereferences a buffer_reference: load the 64-bit pointer, then access-chain
            // through it in PhysicalStorageBuffer storage.
            case Member m when InferType(m.Target) is BufferReferenceType brt:
            {
                // A reference read out of another reference (`node.next.v`) is itself a physical load: Aligned.
                var refChain = BuildAccess(m.Target);
                uint ptr = refChain.Physical
                    ? Emit(Op.Load, SpvType(new BufferReferenceType(brt.Name)), Collapse(refChain), 0x2, PhysAligned(refChain.PhysAlign, refChain.PhysOffset))
                    : Load(new BufferReferenceType(brt.Name), Collapse(refChain)).Id;
                BufferRefDefine(brt.Name);
                var rst = _bufferRefs[brt.Name];
                int ri = IndexOfField(rst, m.Name);
                return new Chain(ptr, [MemberIndexConst(ri)], rst.Fields[ri].Type, StorageClass.PhysicalStorageBuffer, Physical: true,
                    PhysAlign: _bufferRefAlign.GetValueOrDefault(brt.Name, 16), PhysOffset: FieldOffset(rst, ri, BlockLayout.Std430));
            }
            case Member m:
            {
                var baseC = BuildAccess(m.Target);
                // A single-component swizzle (`v.x`) selects a vector component; glslang defers the
                // index constant until the chain is collapsed, so record it rather than emitting it now.
                if (baseC.Type is VectorType bvec && IsSwizzleName(m.Name) && m.Name.Length == 1)
                    return new Chain(baseC.Base, baseC.Indices, new ScalarType(bvec.Component), baseC.Storage, SwizzleIndex(m.Name[0]), Layout: baseC.Layout, Mem: baseC.Mem);
                if (baseC.Type is not StructType st)
                    throw new GlslParseException($"type '{Types.Name(baseC.Type)}' has no member '.{m.Name}'");
                int index = IndexOfField(st, m.Name);
                // An interface block's bool member is stored as uint, so the chain addresses the stored type.
                var fieldType = st.Fields[index].Type;
                int memOff = baseC.Physical ? baseC.PhysOffset + FieldOffset(st, index, BlockLayout.Std430) : 0;
                return new Chain(baseC.Base, [.. baseC.Indices, MemberIndexConst(index)],
                    baseC.Layout is not null ? BlockStored(fieldType) : fieldType, baseC.Storage, Layout: baseC.Layout, Physical: baseC.Physical, Mem: baseC.Mem, PhysAlign: baseC.PhysAlign, PhysOffset: memOff);
            }
            default:
                // An identifier nothing can resolve is undeclared (glslang: "'o' : undeclared identifier"),
                // not a read-only l-value - the misleading message pointed const-assignment diagnostics
                // at the wrong cause.
                if (e is Ident { Name: var n } && !NameResolvable(n))
                    throw new GlslParseException($"unresolved identifier '{Clip(n)}'", e.Line);
                throw new GlslParseException(e is Ident ident
                    ? $"cannot assign to '{ident.Name}'; it is not an l-value (it may be const or read-only)"
                    : "cannot assign to this expression; an l-value is required");
        }
    }

    uint _glMeshVertices, _glMeshIndices;

    // gl_MeshVerticesEXT: an Output array[max_vertices] of the gl_MeshPerVertexEXT block (identical in
    // shape to gl_PerVertex, only differently named).
    uint GlMeshVertices()
    {
        if (_glMeshVertices == 0)
        {
            uint block = PerVertexBlockType("gl_MeshPerVertexEXT");
            uint arr = _m.TypeArray(block, _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)_meshMaxVertices));
            uint ptr = _m.TypePointer(StorageClass.Output, arr);
            _glMeshVertices = _m.GlobalVariable(ptr, StorageClass.Output);
            _m.AddName(_glMeshVertices, "gl_MeshVerticesEXT");
            _interface.Add(_glMeshVertices);
        }
        return _glMeshVertices;
    }

    uint _glMeshPrimitives;

    // gl_MeshPrimitivesEXT: an Output array[max_primitives] of the gl_MeshPerPrimitiveEXT block.
    uint GlMeshPrimitives()
    {
        if (_glMeshPrimitives == 0)
        {
            uint block = MeshPerPrimitiveBlockType();
            uint arr = _m.TypeArray(block, _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)_meshMaxPrimitives));
            uint ptr = _m.TypePointer(StorageClass.Output, arr);
            _glMeshPrimitives = _m.GlobalVariable(ptr, StorageClass.Output);
            _m.AddName(_glMeshPrimitives, "gl_MeshPrimitivesEXT");
            _interface.Add(_glMeshPrimitives);
        }
        return _glMeshPrimitives;
    }

    // The gl_MeshPerPrimitiveEXT block: { int gl_PrimitiveID; int gl_Layer; int gl_ViewportIndex;
    // bool gl_CullPrimitiveEXT; }, each member both a BuiltIn and PerPrimitiveEXT (glslang builds the bool
    // type just before the struct).
    uint MeshPerPrimitiveBlockType()
    {
        uint i = SpvType(Types.Int);
        uint b = _m.TypeBool();
        uint s = _m.TypeStructUnique(i, i, i, b);
        _m.AddName(s, "gl_MeshPerPrimitiveEXT");
        _m.AddMemberName(s, 0, "gl_PrimitiveID");
        _m.AddMemberName(s, 1, "gl_Layer");
        _m.AddMemberName(s, 2, "gl_ViewportIndex");
        _m.AddMemberName(s, 3, "gl_CullPrimitiveEXT");
        _m.MemberDecorate(s, 0, Decoration.BuiltIn, (uint)BuiltIn.PrimitiveId);
        _m.MemberDecorate(s, 0, Decoration.PerPrimitiveEXT);
        _m.MemberDecorate(s, 1, Decoration.BuiltIn, (uint)BuiltIn.Layer);
        _m.MemberDecorate(s, 1, Decoration.PerPrimitiveEXT);
        _m.MemberDecorate(s, 2, Decoration.BuiltIn, (uint)BuiltIn.ViewportIndex);
        _m.MemberDecorate(s, 2, Decoration.PerPrimitiveEXT);
        _m.MemberDecorate(s, 3, Decoration.BuiltIn, (uint)BuiltIn.CullPrimitiveEXT);
        _m.MemberDecorate(s, 3, Decoration.PerPrimitiveEXT);
        _m.Decorate(s, Decoration.Block);
        return s;
    }

    uint _glMeshVerticesNV, _glPrimIndicesNV, _glPrimCountNV;

    // GL_NV_mesh_shader gl_MeshVerticesNV: Output array[max_vertices] of gl_MeshPerVertexNV.
    uint GlMeshVerticesNV()
    {
        if (_glMeshVerticesNV == 0)
        {
            uint block = MeshPerVertexNVType();
            uint arr = _m.TypeArray(block, _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)_meshMaxVertices));
            uint ptr = _m.TypePointer(StorageClass.Output, arr);
            _glMeshVerticesNV = _m.GlobalVariable(ptr, StorageClass.Output);
            _m.AddName(_glMeshVerticesNV, "gl_MeshVerticesNV");
            _interface.Add(_glMeshVerticesNV);
        }
        return _glMeshVerticesNV;
    }

    // gl_PrimitiveIndicesNV: a flat Output uint array of max_primitives * <vertices per primitive>.
    uint GlPrimitiveIndicesNV()
    {
        if (_glPrimIndicesNV == 0)
        {
            int perPrim = _meshPrimitive switch { "points" => 1, "lines" => 2, _ => 3 };
            uint arr = _m.TypeArray(SpvType(Types.UInt), _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)(_meshMaxPrimitives * perPrim)));
            uint ptr = _m.TypePointer(StorageClass.Output, arr);
            _glPrimIndicesNV = _m.GlobalVariable(ptr, StorageClass.Output);
            _m.AddName(_glPrimIndicesNV, "gl_PrimitiveIndicesNV");
            _m.Decorate(_glPrimIndicesNV, Decoration.BuiltIn, (uint)BuiltIn.PrimitiveIndicesNV);
            _interface.Add(_glPrimIndicesNV);
        }
        return _glPrimIndicesNV;
    }

    // gl_PrimitiveCountNV: a single Output uint the shader writes with the emitted primitive count.
    uint GlPrimitiveCountNV()
    {
        if (_glPrimCountNV == 0)
        {
            uint ptr = _m.TypePointer(StorageClass.Output, SpvType(Types.UInt));
            _glPrimCountNV = _m.GlobalVariable(ptr, StorageClass.Output);
            _m.AddName(_glPrimCountNV, "gl_PrimitiveCountNV");
            _m.Decorate(_glPrimCountNV, Decoration.BuiltIn, (uint)BuiltIn.PrimitiveCountNV);
            _interface.Add(_glPrimCountNV);
        }
        return _glPrimCountNV;
    }

    // The gl_MeshPerVertexNV block: gl_Position/PointSize/Clip/CullDistance plus the PerViewNV variants,
    // the per-view arrays sized to the fixed gl_MaxMeshViewCountNV (4).
    uint MeshPerVertexNVType()
    {
        uint f = _m.TypeFloat(32);
        uint vec4 = SpvType(Vec4);
        uint u = _m.TypeInt(32, signed: false);
        uint arr1 = _m.TypeArray(f, _m.ConstantInt(u, 1));
        uint c4 = _m.ConstantInt(u, 4);
        uint vec4x4 = _m.TypeArray(vec4, c4);
        uint arr1x4 = _m.TypeArray(arr1, c4);
        uint s = _m.TypeStructUnique(vec4, f, arr1, arr1, vec4x4, arr1x4, arr1x4);
        _m.AddName(s, "gl_MeshPerVertexNV");
        _m.AddMemberName(s, 0, "gl_Position");
        _m.AddMemberName(s, 1, "gl_PointSize");
        _m.AddMemberName(s, 2, "gl_ClipDistance");
        _m.AddMemberName(s, 3, "gl_CullDistance");
        _m.AddMemberName(s, 4, "gl_PositionPerViewNV");
        _m.AddMemberName(s, 5, "gl_ClipDistancePerViewNV");
        _m.AddMemberName(s, 6, "gl_CullDistancePerViewNV");
        _m.MemberDecorate(s, 0, Decoration.BuiltIn, (uint)BuiltIn.Position);
        _m.MemberDecorate(s, 1, Decoration.BuiltIn, (uint)BuiltIn.PointSize);
        _m.MemberDecorate(s, 2, Decoration.BuiltIn, (uint)BuiltIn.ClipDistance);
        _m.MemberDecorate(s, 3, Decoration.BuiltIn, (uint)BuiltIn.CullDistance);
        _m.MemberDecorate(s, 4, Decoration.PerViewNV);
        _m.MemberDecorate(s, 4, Decoration.BuiltIn, (uint)BuiltIn.PositionPerViewNV);
        _m.MemberDecorate(s, 5, Decoration.PerViewNV);
        _m.MemberDecorate(s, 5, Decoration.BuiltIn, (uint)BuiltIn.ClipDistancePerViewNV);
        _m.MemberDecorate(s, 6, Decoration.PerViewNV);
        _m.MemberDecorate(s, 6, Decoration.BuiltIn, (uint)BuiltIn.CullDistancePerViewNV);
        _m.Decorate(s, Decoration.Block);
        return s;
    }

    // The per-primitive index output: uvec3[max_primitives] for triangles, uvec2 for lines, uint for
    // points, decorated with the matching PrimitiveIndicesEXT built-in.
    (uint Var, GlslType Element) GlMeshIndices()
    {
        var (element, builtIn) = _meshPrimitive switch
        {
            "lines" => ((GlslType)new VectorType(Scalar.UInt, 2), BuiltIn.PrimitiveLineIndicesEXT),
            "points" => (Types.UInt, BuiltIn.PrimitivePointIndicesEXT),
            _ => (new VectorType(Scalar.UInt, 3), BuiltIn.PrimitiveTriangleIndicesEXT),
        };
        if (_glMeshIndices == 0)
        {
            uint arr = _m.TypeArray(SpvType(element), _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)_meshMaxPrimitives));
            uint ptr = _m.TypePointer(StorageClass.Output, arr);
            _glMeshIndices = _m.GlobalVariable(ptr, StorageClass.Output);
            _m.AddName(_glMeshIndices, _meshPrimitive switch { "lines" => "gl_PrimitiveLineIndicesEXT", "points" => "gl_PrimitivePointIndicesEXT", _ => "gl_PrimitiveTriangleIndicesEXT" });
            _m.Decorate(_glMeshIndices, Decoration.BuiltIn, (uint)builtIn);
            _interface.Add(_glMeshIndices);
        }
        return (_glMeshIndices, element);
    }

    // The gl_MeshPerVertexEXT member a name selects: its index in the block and its type.
    static (int Index, GlslType Type)? MeshVertexMember(string name) => name switch
    {
        "gl_Position" => (0, Vec4),
        "gl_PointSize" => (1, Types.Float),
        "gl_ClipDistance" => (2, ClipCullType),
        "gl_CullDistance" => (3, ClipCullType),
        _ => null,
    };

    // The gl_MeshPerPrimitiveEXT member a name selects: index in the block and type.
    static (int Index, GlslType Type)? MeshPrimitiveMember(string name) => name switch
    {
        "gl_PrimitiveID" => (0, Types.Int),
        "gl_Layer" => (1, Types.Int),
        "gl_ViewportIndex" => (2, Types.Int),
        "gl_CullPrimitiveEXT" => (3, Types.Bool),
        _ => null,
    };

    uint GlPerVertexArray(ref uint cache, StorageClass storage, int count, string name)
    {
        if (cache == 0)
        {
            var redecl = name == "gl_in" ? _perVertexIn : _perVertexOut;
            uint arr = _m.TypeArray(PerVertexBlockType("gl_PerVertex", redecl), _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)count));
            uint ptr = _m.TypePointer(storage, arr);
            cache = _m.GlobalVariable(ptr, storage);
            _m.AddName(cache, name);
            _interface.Add(cache);
        }
        return cache;
    }

    // The gl_PerVertex block struct that a gl_in[]/gl_out[] array element resolves to: a redeclaration's
    // exact member list when present, else the default four members. Each is a fresh (unique) struct so
    // gl_in and gl_out stay distinct even with the same shape.
    uint PerVertexBlockType(string blockName = "gl_PerVertex", List<(string Name, GlslType Type)>? members = null)
    {
        if (members is { } m)
        {
            uint sr = _m.TypeStructUnique([.. m.Select(x => SpvType(x.Type))]);
            _m.AddName(sr, blockName);
            for (int i = 0; i < m.Count; i++)
            {
                _m.AddMemberName(sr, i, m[i].Name);
                _m.MemberDecorate(sr, i, Decoration.BuiltIn, (uint)PerVertexBuiltIn(m[i].Name));
                if (m[i].Name == "gl_ClipDistance") _m.AddCapability(Capability.ClipDistance);
                if (m[i].Name == "gl_CullDistance") _m.AddCapability(Capability.CullDistance);
            }
            _m.Decorate(sr, Decoration.Block);
            return sr;
        }
        // glslang builds the member types in member order: the vec4 (gl_Position) before the float[1]
        // clip/cull arrays, so create Vec4 first to match its id allocation.
        uint fl = _m.TypeFloat(32);
        uint vec4 = SpvType(Vec4);
        uint arr1 = _m.TypeArray(fl, _m.ConstantInt(_m.TypeInt(32, signed: false), 1));
        uint s = _m.TypeStructUnique(vec4, fl, arr1, arr1);
        // glslang names the per-vertex block and its members, just like the output gl_PerVertex.
        _m.AddName(s, blockName);
        _m.AddMemberName(s, 0, "gl_Position");
        _m.AddMemberName(s, 1, "gl_PointSize");
        _m.AddMemberName(s, 2, "gl_ClipDistance");
        _m.AddMemberName(s, 3, "gl_CullDistance");
        _m.MemberDecorate(s, 0, Decoration.BuiltIn, (uint)BuiltIn.Position);
        _m.MemberDecorate(s, 1, Decoration.BuiltIn, (uint)BuiltIn.PointSize);
        _m.MemberDecorate(s, 2, Decoration.BuiltIn, (uint)BuiltIn.ClipDistance);
        _m.MemberDecorate(s, 3, Decoration.BuiltIn, (uint)BuiltIn.CullDistance);
        _m.Decorate(s, Decoration.Block);
        return s;
    }

    // The (name, type) members of a gl_PerVertex redeclaration, in declared order.
    List<(string Name, GlslType Type)> PerVertexMembers(InterfaceBlock block) =>
        [.. block.Members.SelectMany(f => f.Declarators.Select(d => (d.Name, ApplyArrayDims(ResolveTypeRef(f.Type), d.Array))))];

    // gl_TessLevelOuter/Inner are written by the tessellation control shader (Output) and read by
    // the tessellation evaluation shader (Input). Using Output storage in a TES is invalid SPIR-V
    // (VUID-TessLevelOuter-04392).
    Access TessLevelAccess(ref uint cache, BuiltIn kind, int size)
    {
        var storage = stage == ShaderStage.TessEvaluation ? StorageClass.Input : StorageClass.Output;
        var type = new ArrayType(Types.Float, size);
        if (cache == 0)
        {
            uint ptr = _m.TypePointer(storage, SpvType(type));
            cache = _m.GlobalVariable(ptr, storage);
            _m.AddName(cache, kind == BuiltIn.TessLevelInner ? "gl_TessLevelInner" : "gl_TessLevelOuter");
            _m.Decorate(cache, Decoration.Patch);
            _m.Decorate(cache, Decoration.BuiltIn, (uint)kind);
            _interface.Add(cache);
        }
        return new Access(cache, type, storage);
    }

    // A standalone Input array carrying gl_ClipDistance/gl_CullDistance in a fragment shader.
    uint ClipCullInput(ref uint cache, BuiltIn kind)
    {
        if (cache == 0)
        {
            uint ptr = _m.TypePointer(StorageClass.Input, SpvType(ClipCullType));
            cache = _m.GlobalVariable(ptr, StorageClass.Input);
            _m.Decorate(cache, Decoration.BuiltIn, (uint)kind);
            _interface.Add(cache);
        }
        return cache;
    }

    static int IndexOfField(StructType st, string name)
    {
        for (int i = 0; i < st.Fields.Count; i++) if (st.Fields[i].Name == name) return i;
        throw new GlslParseException($"struct '{st.Name}' has no field '{name}'");
    }

    // gl_Layer, gl_ViewportIndex and gl_PrimitiveID are outputs when a geometry shader produces them
    // (routing a primitive to a layer/viewport, or overriding the primitive id) and inputs in every
    // other stage. gl_PrimitiveIDIn (the geometry input) stays Input via its own dictionary entry.
    bool BuiltinIsOutput(string name, bool defaultOutput) => name switch
    {
        "gl_PrimitiveID" => stage == ShaderStage.Geometry,
        // GL_ARB_shader_viewport_layer_array lets the pre-rasterization stages write them too.
        "gl_Layer" or "gl_ViewportIndex" => stage is ShaderStage.Geometry or ShaderStage.Vertex or ShaderStage.TessEvaluation,
        _ => defaultOutput,
    };

    // gl_SubgroupSize and gl_SubgroupInvocationID are mediump (RelaxedPrecision); the others are not.
    static readonly HashSet<string> RelaxedBuiltins = ["gl_SubgroupSize", "gl_SubgroupInvocationID"];

    // gl_-prefixed names a user may legitimately redeclare (built-ins). Anything else with a gl_ prefix is
    // a reserved-identifier error. Covers the Builtins table, the gl_PerVertex members, and the aggregates
    // (tessellation levels, mesh output arrays, gl_in/gl_out) that are not plain scalar built-ins.
    static readonly HashSet<string> ExtraGlBuiltins =
    [
        "gl_PerVertex", "gl_in", "gl_out", "gl_TessLevelOuter", "gl_TessLevelInner", "gl_WorkGroupSize",
        "gl_MeshVerticesEXT", "gl_MeshPrimitivesEXT", "gl_PrimitiveTriangleIndicesEXT",
        "gl_PrimitiveLineIndicesEXT", "gl_PrimitivePointIndicesEXT", "gl_ClipDistance", "gl_CullDistance",
    ];
    static bool IsGlBuiltinName(string name) => Builtins.ContainsKey(name) || PerVertexType(name) is not null || ExtraGlBuiltins.Contains(name);

    // Any gl_-prefixed variable, function, or parameter a user declares is reserved unless it redeclares
    // a built-in. Struct type and member names are exempt: glslang accepts those.
    static void RejectReservedName(string name)
    {
        if (name.StartsWith("gl_", StringComparison.Ordinal) && !IsGlBuiltinName(name))
            throw new GlslParseException($"'{name}' : identifiers starting with 'gl_' are reserved");
    }

    // The SPIR-V version that folded a built-in's extension into core (GlslangToSpv's addIncorporatedExtension
    // calls): multiview, device group and draw parameters joined core in 1.3. The rest are still extensions.
    static SpirVVersion BuiltinExtensionCoreVersion(string ext) => ext switch
    {
        "SPV_KHR_multiview" or "SPV_KHR_device_group" or "SPV_KHR_shader_draw_parameters" => SpirVVersion.V1_3,
        _ => (SpirVVersion)uint.MaxValue,
    };

    (GlslType Type, uint VarId) BuiltinVar(string name)
    {
        // gl_HitTEXT is a pure alias of gl_RayTmaxEXT (same RayTmaxKHR built-in); glslang emits one variable
        // named gl_RayTmaxEXT for both, so canonicalize before caching/naming.
        if (name == "gl_HitTEXT") name = "gl_RayTmaxEXT";
        if (name == "gl_HitTNV") name = "gl_RayTmaxNV";
        if (_builtinVars.TryGetValue(name, out var existing)) return existing;
        if (stage != ShaderStage.Fragment && name is "gl_FragCoord" or "gl_FragDepth" or "gl_FrontFacing" or "gl_PointCoord" or "gl_SampleID" or "gl_SampleMaskIn" or "gl_HelperInvocation")
            throw new GlslParseException($"'{name}' is only available in a fragment shader");
        if (stage != ShaderStage.Vertex && name is "gl_VertexIndex" or "gl_InstanceIndex")
            throw new GlslParseException($"'{name}' is only available in a vertex shader");
        if (stage != ShaderStage.TessEvaluation && name is "gl_TessCoord")
            throw new GlslParseException($"'{name}' is only available in a tessellation evaluation shader");
        if (stage is not (ShaderStage.Compute or ShaderStage.Mesh or ShaderStage.Task)
            && name is "gl_WorkGroupID" or "gl_NumWorkGroups" or "gl_LocalInvocationID" or "gl_GlobalInvocationID" or "gl_LocalInvocationIndex")
            throw new GlslParseException($"'{name}' is only available in a compute, task, or mesh shader");
        var (kind, type, defaultOutput, cap, ext) = Builtins[name];
        bool output = BuiltinIsOutput(name, defaultOutput);
        // gl_PrimitiveID/gl_PrimitiveIDIn pull in the Geometry capability ONLY in a fragment shader
        // (GlslangToSpv EbvPrimitiveId). Every other stage that exposes them already declares a capability
        // that covers them, and a ray-tracing stage must not gain a spurious Geometry.
        if (name is "gl_Layer" or "gl_ViewportIndex" && stage is ShaderStage.Vertex or ShaderStage.TessEvaluation)
        {
            // Written from a vertex/tessellation stage (GlslangToSpv EbvLayer/EbvViewportIndex): SPIR-V 1.5 split
            // SPV_EXT_shader_viewport_index_layer's single ShaderViewportIndexLayerEXT capability into the core
            // ShaderLayer and ShaderViewportIndex. The Geometry/MultiViewport capabilities are not used here.
            if (version < SpirVVersion.V1_5)
            {
                _m.AddExtension("SPV_EXT_shader_viewport_index_layer");
                _m.AddCapability(Capability.ShaderViewportIndexLayerEXT);
            }
            else _m.AddCapability(name == "gl_Layer" ? Capability.ShaderLayer : Capability.ShaderViewportIndex);
        }
        else if (cap is Capability c
            && !(c == Capability.Geometry && name is "gl_PrimitiveID" or "gl_PrimitiveIDIn" && stage != ShaderStage.Fragment))
            _m.AddCapability(c);
        if (ext is not null) AddIncorporatedExtension(ext, BuiltinExtensionCoreVersion(ext));
        var storage = output ? StorageClass.Output : StorageClass.Input;
        uint ptr = _m.TypePointer(storage, SpvType(type));
        uint var = _m.GlobalVariable(ptr, storage);
        _m.AddName(var, name);   // glslang names built-in variables with their gl_ source name
        // A few subgroup built-ins are mediump per the KHR spec, so they and their loads are Relaxed.
        if (RelaxedBuiltins.Contains(name)) Relax(var);
        // An integer input to a fragment shader cannot be interpolated and must be Flat (VUID-04744).
        if (!output && stage == ShaderStage.Fragment && BaseScalar(type) is Scalar.Int or Scalar.UInt)
            _m.Decorate(var, Decoration.Flat);
        _m.Decorate(var, Decoration.BuiltIn, (uint)kind);
        // In an intersection shader reportIntersectionEXT can update the ray's tMax, so gl_RayTmaxEXT is mutable
        // and glslang marks the RayTmaxKHR built-in Volatile + Coherent (the older NV path does not).
        if (stage == ShaderStage.Intersection && kind == BuiltIn.RayTmaxKHR && !_nvRayTracing)
        {
            _m.Decorate(var, Decoration.Volatile);
            _m.Decorate(var, Decoration.Coherent);
        }
        _interface.Add(var);
        return _builtinVars[name] = (type, var);
    }

    // Buffer variables declared `readonly`; storing through one is a source error, not something to emit.
    readonly HashSet<uint> _readOnlyVars = [];

    (uint Pointer, GlslType Type) ResolveLValue(Expr target)
    {
        var a = ResolveAccess(target);
        RejectInputWrite(a.Storage);
        RejectReadOnlyWrite(a.Root);
        return (a.Pointer, a.Type);
    }

    // A shader input is read-only; writing through it (assign, compound assign, ++/--) is an error.
    static void RejectInputWrite(StorageClass storage)
    {
        if (storage == StorageClass.Input)
            throw new GlslParseException("cannot write to a shader input variable");
    }

    // A buffer (or block member) declared `readonly` cannot be stored through.
    void RejectReadOnlyWrite(uint rootVar)
    {
        if (_readOnlyVars.Contains(rootVar))
            throw new GlslParseException("cannot write to a readonly buffer");
    }

    // As above, but also catches a single member declared readonly inside an otherwise writable block.
    void RejectReadOnlyWrite(Chain c)
    {
        RejectReadOnlyWrite(c.Base);
        if (c.Indices.Length > 0 && _memberIndexOf.TryGetValue(c.Indices[0], out int mi)
            && _readOnlyMembers.Contains((c.Base, mi)))
            throw new GlslParseException("cannot write to a readonly buffer member");
    }
    readonly HashSet<(uint Var, int Member)> _readOnlyMembers = [];
    readonly HashSet<uint> _writeOnlyVars = [];
    readonly HashSet<(uint Var, int Member)> _writeOnlyMembers = [];

    // The global variable an expression ultimately names, or null if it is not a simple resource access.
    uint? RootVariableOf(Expr e) => e switch
    {
        Ident id => ResolveGlobal(id.Name)?.VarId,
        Index ix => RootVariableOf(ix.Target),
        Member m => RootVariableOf(m.Target),
        _ => null,
    };

    int MemberIndexOfChain(Chain c) =>
        c.Indices.Length > 0 && _memberIndexOf.TryGetValue(c.Indices[0], out int mi) ? mi : -1;

    // A `writeonly` resource cannot be read from.
    void RejectWriteOnlyRead(uint rootVar, int memberIdx)
    {
        if (_writeOnlyVars.Contains(rootVar) || (memberIdx >= 0 && _writeOnlyMembers.Contains((rootVar, memberIdx))))
            throw new GlslParseException("cannot read from a writeonly resource");
    }

    Value LowerLoad(Ident id)
    {
        if (ShadingRateFlags.TryGetValue(id.Name, out int flag)) return Materialize(new CScalar(Scalar.Int, 0, flag));
        if (RayFlags.TryGetValue(id.Name, out int rf)) return Materialize(new CScalar(Scalar.UInt, 0, rf));
        if (_valueParams.TryGetValue(id.Name, out var vp)) return vp;   // shadows any same-named global
        if (_constants.TryGetValue(id.Name, out var c)) return c;
        // A const materializes its OpConstant lazily on first use (glslang folds the declaration and
        // emits the constant only when referenced), caching the result thereafter. A const array reaches
        // here only when used as a whole value; indexing it folds or goes through an "indexable" temp.
        if (_constValues.TryGetValue(id.Name, out var cv))
            return _constants[id.Name] = Materialize(cv);
        if (_pendingSpecConstants.TryGetValue(id.Name, out var sc))
            return MaterializeSpecConstant(id.Name, sc);
        if (_pendingSpecExprs.ContainsKey(id.Name)) return SpecConstantExpr(id);
        // A specializable local size leaves gl_WorkGroupSize unfoldable: it reads the spec composite.
        if (id.Name == "gl_WorkGroupSize" && stage == ShaderStage.Compute) return WorkGroupSizeConstant();
        if (ResolveLocalVar(id.Name) is { } local) return Load(local.Type, local.VarId);
        if (ResolveGlobal(id.Name) is { } g) return Load(g.Type, g.VarId);
        if (_blockMembers.TryGetValue(id.Name, out var member))
        {
            var a = ResolveAccess(id);
            RejectWriteOnlyRead(a.Root, a.MemberIdx);
            return FromStored(Load(a.Type, a.Pointer, a.Layout), member.Type);
        }
        if (Builtins.ContainsKey(id.Name)) { var (t, v) = BuiltinVar(id.Name); return Load(t, v); }
        // gl_Position and the other gl_PerVertex members are readable, through their access chain.
        if (PerVertexType(id.Name) is not null) { var a = ResolveAccess(id); return Load(a.Type, a.Pointer); }
        throw new GlslParseException($"unresolved identifier '{id.Name}'");
    }

    Value LowerLiteral(Literal lit) => lit.Kind switch
    {
        TokenKind.FloatConst => new Value(_m.ConstantFloat(SpvType(Types.Float), ParseFloat(lit.Text)), Types.Float, true),
        TokenKind.DoubleConst => new Value(_m.ConstantDouble(SpvType(Types.Double), ParseDouble(lit.Text)), Types.Double, true),
        TokenKind.HalfConst => new Value(_m.ConstantHalf(SpvType(Types.Half), ParseFloat(lit.Text)), Types.Half, true),
        TokenKind.Int64Const => new Value(_m.ConstantInt64(SpvType(Types.Int64), ParseInt64Literal(lit.Text)), Types.Int64, true),
        TokenKind.Uint64Const => new Value(_m.ConstantInt64(SpvType(Types.UInt64), ParseInt64Literal(lit.Text)), Types.UInt64, true),
        TokenKind.IntConst => new Value(_m.ConstantInt(SpvType(Types.Int), ParseIntLiteral(lit.Text)), Types.Int, true),
        TokenKind.UintConst => new Value(_m.ConstantInt(SpvType(Types.UInt), ParseIntLiteral(lit.Text)), Types.UInt, true),
        TokenKind.BoolConst => new Value(_m.ConstantBool(SpvType(Types.Bool), lit.Text == "true"), Types.Bool, true),
        _ => throw new GlslParseException($"literal '{lit.Text}' is not lowered yet"),
    };

    Value LowerConstructor(Call call)
    {
        if (call.IsArray) return LowerArrayConstructor(call);

        // A constructor with all-constant arguments folds to an OpConstantComposite, exactly like
        // glslang's front-end constant folding (e.g. a literal mat3, ivec2, vec4, etc.).
        if (Resolve(call.Name) is MatrixType or VectorType or ScalarType && EvalConst(call) is CVal folded)
            return Materialize(folded);

        var type = Resolve(call.Name);
        // A buffer reference back to its raw address: uvec2 reinterprets it, uint64_t converts it.
        if (_bufferRefs.Count > 0 && call.Args.Count == 1
            && type is VectorType { Component: Scalar.UInt, Size: 2 } or ScalarType { Kind: Scalar.UInt64 }
            && InferType(call.Args[0]) is BufferReferenceType)
        {
            var reference = LowerExpr(call.Args[0]);
            return new Value(Emit(type is ScalarType ? Op.ConvertPtrToU : Op.Bitcast, SpvType(type), reference.Id), type, false);
        }
        // glslang evaluates AND converts each argument of a multi-argument vector constructor in turn,
        // so convert each right after it is lowered (rather than lowering all then converting all). It also
        // reuses the previous argument's loaded id when this argument and the one before it are the same
        // variable symbol with no conversion, instead of loading it again (translateArguments' "Reuse the id").
        Scalar? comp = ConstituentScalar(type, call.Args.Count);
        var args = new Value[call.Args.Count];
        for (int i = 0; i < call.Args.Count; i++)
            args[i] = i > 0 && call.Args[i] is Ident cur && call.Args[i - 1] is Ident prev
                          && cur.Name == prev.Name && (comp is null || SymbolScalar(cur.Name) == comp)
                ? args[i - 1]
                : comp is { } c ? LowerConstituent(call.Args[i], c) : LowerExpr(call.Args[i]);

        var built = LowerConstructorBody(type, args, call);
        // Under a mediump default a constructor of that type takes its operands' (relaxed) precision.
        return RelaxByDefault(built.Type) && !built.IsConstant && !built.Relaxed && RelaxedResult(args)
            ? built with { Id = Relax(built.Id), Relaxed = true } : built;
    }

    Value LowerConstructorBody(GlslType type, Value[] args, Call call)
    {
        if (type is ScalarType scalar && args.Length == 1)
            return ConvertScalar(scalar.Kind, args[0]);

        if (type is SamplerType combined && args.Length == 2)   // sampler2D(texture, sampler)
            return new Value(Emit(Op.SampledImage, SpvType(combined), args[0].Id, args[1].Id), combined, false);

        if (type is VectorType vec)
        {
            // vecN(largerVec) truncates, but glslang has no special case for it: it falls through to the
            // component-by-component path below, which extracts only as many components as fit.

            // vecN(otherVecN): component-wise numeric conversion (e.g. vec2(ivec2), vec4(bvec4)).
            if (args.Length == 1 && args[0].Type is VectorType sv && sv.Size == vec.Size && sv.Component != vec.Component)
            {
                if (sv.Component == Scalar.Bool) return SelectFromBool(vec, args[0]);
                if (vec.Component == Scalar.Bool) return BoolVectorFrom(vec, sv, args[0]);
                return NumericConvert(args[0], vec, sv.Component, vec.Component);
            }

            // glslang elides a same-type vector copy: vecN(vecN) of an equal-size same-component vector returns
            // the source unchanged (createConstructor's "2 vectors of equal size" special case).
            if (args.Length == 1 && args[0].Type is VectorType scopy && scopy.Size == vec.Size && scopy.Component == vec.Component)
                return args[0];

            // A single scalar smears and a single larger vector/matrix truncates; otherwise the arguments'
            // components must add up to exactly the vector's size.
            int Components(Value v) => v.Type switch { VectorType vt => vt.Size, MatrixType mt => mt.Columns * mt.Rows, _ => 1 };
            int provided = args.Sum(Components);
            bool singleScalar = args.Length == 1 && args[0].Type is ScalarType;
            bool truncating = args.Length == 1 && args[0].Type is VectorType or MatrixType && provided >= vec.Size;
            if (!singleScalar && !truncating && provided != vec.Size)
                throw new GlslParseException($"a '{Types.Name(type)}' constructor takes {vec.Size} components, but {provided} were provided");

            // convert constituents to the component type (e.g. vec2(int, int) -> floats, vec4(ivec3, 1) -> vec3 + float)
            args = [.. args.Select(a => ConvertConstituent(a, vec.Component))];

            uint typeId = SpvType(type);

            // A single scalar smears across all components (vec3(x) -> {x, x, x}).
            if (args.Length == 1 && args[0].Type is ScalarType)
            {
                uint[] smear = [.. Enumerable.Repeat(args[0].Id, vec.Size)];
                if (args[0].IsConstant) return new Value(_m.ConstantComposite(typeId, smear), type, true);
                if (_specConstIds.Contains(args[0].Id)) return TrackSpec(_m.SpecConstantComposite(typeId, smear), type);
                // A scalar-smear constructor takes the target type's (default) precision, not the operand's,
                // so it is not relaxed here; the mediump-default case is handled by LowerConstructor.
                return new Value(Emit(Op.CompositeConstruct, typeId, smear), type, false);
            }

            // All-constant scalar constituents fold to a constant composite.
            if (args.All(a => a.IsConstant && a.Type is ScalarType))
                return new Value(_m.ConstantComposite(typeId, [.. args.Select(a => a.Id)]), type, true);

            // A composite of specialization constants stays specializable as an OpSpecConstantComposite.
            if (args.All(a => a.Type is ScalarType && (a.IsConstant || _specConstIds.Contains(a.Id))) && args.Any(a => _specConstIds.Contains(a.Id)))
                return TrackSpec(_m.SpecConstantComposite(typeId, [.. args.Select(a => a.Id)]), type);

            // Otherwise flatten every argument into scalar components, exactly like glslang's
            // createConstructor: each vector argument is decomposed with OpCompositeExtract and scalars
            // are used directly, then a single OpCompositeConstruct builds the result from the scalars.
            uint scalarType = SpvType(new ScalarType(vec.Component));
            var flat = new List<uint>();
            foreach (var a in args)
            {
                if (a.Type is VectorType av)
                    for (uint c = 0; c < av.Size && flat.Count < vec.Size; c++)
                    {
                        uint ex = Emit(Op.CompositeExtract, scalarType, a.Id, c);
                        if (a.Relaxed) Relax(ex);   // a mediump component's extract is itself mediump
                        flat.Add(ex);
                    }
                else
                    flat.Add(a.Id);
            }
            return Relaxed(new Value(Emit(Op.CompositeConstruct, typeId, [.. flat]), type, false), args);
        }

        if (type is MatrixType mat)
        {
            // A matrix's components are its own scalar (float, or double for a dmat); convert any scalar
            // argument that differs (e.g. mat3(1.0, 0, 0, ...)).
            args = [.. args.Select(a => a.Type is ScalarType s && s.Kind != mat.Component ? ConvertScalar(mat.Component, a) : a)];
            uint compType = SpvType(new ScalarType(mat.Component));
            uint colType = _m.TypeVector(compType, mat.Rows);
            uint matTypeId = SpvType(type);   // glslang creates the matrix type before its column constituents
            uint CompConst(double v) => Materialize(new CScalar(mat.Component, v, 0)).Id;
            uint[] columns;
            if (args.Length == 1 && args[0].Type is MatrixType src && src.Columns >= mat.Columns && src.Rows >= mat.Rows)   // mat3(mat4): truncate each column
                columns = [.. Enumerable.Range(0, mat.Columns).Select(c =>
                {
                    uint col = Emit(Op.CompositeExtract, _m.TypeVector(SpvType(new ScalarType(src.Component)), src.Rows), args[0].Id, (uint)c);
                    // Columns of equal height need no component selection, and glslang emits no shuffle.
                    if (src.Rows == mat.Rows) return col;
                    var shuffle = InstrPool.Rent(Op.VectorShuffle, colType, _m.AllocId()).Add(col, col);
                    for (uint r = 0; r < mat.Rows; r++) shuffle.Add(r);
                    _code.Add(shuffle);
                    return shuffle.ResultId;
                })];
            else if (args.Length == 1 && args[0].Type is MatrixType from)   // mat4(mat3): copy overlap, identity-fill the rest
            {
                uint one = CompConst(1), zero = CompConst(0);
                // glslang's two-step: fill the whole component array first (extracting only the overlap,
                // column-major), and only then build the column vectors from it.
                uint[,] ids = new uint[mat.Columns, mat.Rows];
                for (int c = 0; c < mat.Columns; c++)
                    for (int r = 0; r < mat.Rows; r++)
                        ids[c, r] = c < from.Columns && r < from.Rows
                            ? Emit(Op.CompositeExtract, compType, args[0].Id, (uint)c, (uint)r)
                            : c == r ? one : zero;
                columns = [.. Enumerable.Range(0, mat.Columns).Select(c =>
                    Emit(Op.CompositeConstruct, colType, [.. Enumerable.Range(0, mat.Rows).Select(r => ids[c, r])]))];
            }
            // glslang builds a matrix straight from column vectors only when they are the SAME repeated
            // vector; distinct columns go through the general path below, like any other components.
            else if (args.Length == mat.Columns && args.All(a => a.Type is VectorType && a.Id == args[0].Id))
                columns = [.. args.Select(a => a.Id)];
            else if (args.Length == 1 && args[0].Type is ScalarType)
            {
                // Same identity seed: the 1.0 is allocated first (glslang) though the diagonal ends up
                // being the scalar argument, leaving the 1.0 constant emitted but unreferenced.
                CompConst(1);
                uint diag = args[0].Id, zero = CompConst(0);
                columns = [.. Enumerable.Range(0, mat.Columns).Select(c =>
                    Emit(Op.CompositeConstruct, colType, [.. Enumerable.Range(0, mat.Rows).Select(r => r == c ? diag : zero)]))];
            }
            else
            {
                // glslang's general two-step: seed an identity (the 1.0 then the 0.0), then overwrite it
                // column-major with the supplied components - decomposing every vector argument with
                // OpCompositeExtract - and rebuild the column vectors from those scalars.
                CompConst(1);
                CompConst(0);
                var flat = new List<uint>();
                foreach (var a in args)
                {
                    if (a.Type is VectorType av)
                        for (uint c = 0; c < av.Size; c++) flat.Add(Emit(Op.CompositeExtract, compType, a.Id, c));
                    else flat.Add(a.Id);
                }
                if (flat.Count < mat.Columns * mat.Rows)
                    throw new GlslParseException($"a {mat.Columns}x{mat.Rows} matrix constructor needs {mat.Columns * mat.Rows} components, got {flat.Count}");
                columns = [.. Enumerable.Range(0, mat.Columns).Select(c =>
                    Emit(Op.CompositeConstruct, colType, [.. flat.Skip(c * mat.Rows).Take(mat.Rows)]))];
            }
            return new Value(Emit(Op.CompositeConstruct, matTypeId, columns), type, false);
        }

        throw new GlslParseException($"constructor for '{Types.Name(type)}' is not lowered yet");
    }

    // `Ref(addr)`: a buffer reference built from a raw address. GL_EXT_buffer_reference_uvec2 reinterprets a
    // uvec2 (OpBitcast); GL_EXT_buffer_reference2 converts a uint64_t (OpConvertUToPtr). A reference of the
    // same type passes through.
    Value LowerReferenceConstructor(Call call)
    {
        if (call.Args.Count != 1) throw new GlslParseException($"'{call.Name}' : a buffer reference constructor takes one argument");
        var target = new BufferReferenceType(call.Name);
        var arg = LowerExpr(call.Args[0]);
        if (arg.Type is BufferReferenceType r && r.Name == call.Name) return arg;
        BufferRefDefine(call.Name);
        return arg.Type switch
        {
            VectorType { Component: Scalar.UInt, Size: 2 } => new Value(Emit(Op.Bitcast, SpvType(target), arg.Id), target, false),
            ScalarType { Kind: Scalar.UInt64 } => new Value(Emit(Op.ConvertUToPtr, SpvType(target), arg.Id), target, false),
            _ => throw new GlslParseException($"'{call.Name}' : cannot construct a buffer reference from '{Types.Name(arg.Type)}'"),
        };
    }

    Value LowerStructConstructor(Call call)
    {
        var st = _structs[call.Name];
        // A struct constructor takes exactly one argument per member; anything else builds a composite
        // whose constituent count does not match its type.
        if (call.Args.Count != st.Fields.Count)
            throw new GlslParseException($"constructor for '{call.Name}' expects {st.Fields.Count} argument(s), but got {call.Args.Count}");
        var args = call.Args.Select(LowerExpr).ToArray();
        uint typeId = SpvType(st);
        uint[] comps = [.. args.Select(a => a.Id)];
        return args.All(a => a.IsConstant)
            ? new Value(_m.ConstantComposite(typeId, comps), st, true)
            : new Value(Emit(Op.CompositeConstruct, typeId, comps), st, false);
    }

    // The type of an array constructor. An unsized `T[](a, b)` takes its length from the argument count.
    ArrayType ArrayCtorType(Call c, GlslType baseType)
    {
        var element = c.ArrayDims is { Count: > 1 } d ? ApplyArrayDims(baseType, [.. d.Skip(1)]) : baseType;
        Expr? outer = c.ArraySize ?? (c.ArrayDims is { Count: > 0 } dd ? dd[0] : null);
        return new ArrayType(element, outer is not null && EvalConst(outer) is CScalar n ? (int)n.I : c.Args.Count);
    }

    // The element type of an array constructor: for float[A][B](...) the inner dims become the element.
    GlslType ArrayCtorElement(Call c) => c.ArrayDims is { Count: > 1 } d ? ApplyArrayDims(Resolve(c.Name), [.. d.Skip(1)]) : Resolve(c.Name);

    Value LowerArrayConstructor(Call call)
    {
        var element = ArrayCtorElement(call);
        // Constituents convert to the element type: float[](1, 2, 3) turns the int literals into floats.
        var args = call.Args.Select(a => ConvertToElement(LowerExpr(a), element)).ToArray();
        int length = call.ArraySize is Literal lit ? (int)ParseIntLiteral(lit.Text) : args.Length;
        var type = new ArrayType(element, length);
        uint typeId = SpvType(type);
        uint[] components = [.. args.Select(a => a.Id)];

        return args.All(a => a.IsConstant)
            ? new Value(_m.ConstantComposite(typeId, components), type, true)
            : new Value(Emit(Op.CompositeConstruct, typeId, components), type, false);
    }

    // Converts a value to a target aggregate-element type (scalar or vector component conversion).
    Value ConvertToElement(Value arg, GlslType element)
    {
        if (arg.Type.Equals(element)) return arg;
        if (element is ScalarType es && arg.Type is ScalarType) return ConvertScalar(es.Kind, arg);
        if (element is VectorType ev && arg.Type is VectorType av && av.Size == ev.Size && av.Component != ev.Component)
            return ConvertVectorComponent(arg, av, ev.Component);
        return arg;
    }

    // Converts a constructor constituent (scalar or vector) to the target component type, so a
    // vector built from mismatched pieces (e.g. vec4(ivec3, 1)) converts each piece component-wise.
    // The component every constituent of a constructor converts to, or null when the constructor takes
    // its argument whole (a single-argument conversion, a sampler, a matrix from a matrix).
    static Scalar? ConstituentScalar(GlslType? type, int argCount) => type switch
    {
        VectorType v when argCount > 1 => v.Component,
        MatrixType m when argCount > 1 => m.Component,
        _ => null,
    };

    // The scalar component of a plain local/global variable, for the constructor argument-reuse guard: reuse
    // only applies when no conversion is needed (the component already matches the constructor's).
    Scalar? SymbolScalar(string name) => (ResolveLocalVar(name)?.Type ?? ResolveGlobal(name)?.Type) switch
    {
        ScalarType s => s.Kind,
        VectorType v => v.Component,
        _ => null,
    };

    // glslang's front end converts each constructor argument as it parses, so a constant argument is
    // already the component type rather than a converted one.
    Value LowerConstituent(Expr arg, Scalar component)
    {
        if (!UsesSpecConstant(arg) && EvalConst(arg) is { } cv
            && FoldConversion(cv, CValType(cv) is VectorType v ? new VectorType(component, v.Size) : new ScalarType(component)) is { } folded)
            return Materialize(folded);
        return ConvertConstituent(LowerExpr(arg), component);
    }

    Value ConvertConstituent(Value a, Scalar target) => a.Type switch
    {
        ScalarType s when s.Kind != target => ConvertScalar(target, a),
        VectorType v when v.Component != target => ConvertVectorComponent(a, v, target),
        _ => a,
    };

    Value ConvertVectorComponent(Value a, VectorType from, Scalar target)
    {
        var result = new VectorType(target, from.Size);
        return from.Component == Scalar.Bool
            ? SelectFromBool(result, a)
            : NumericConvert(a, result, from.Component, target);
    }

    // vecN(bvecN): each true component becomes 1, each false 0 (OpSelect over constant composites).
    // glslang creates the 0 then the 1 constant, then their constant-composite vectors, and selects
    // (cond ? oneVec : zeroVec).
    // bvecN(vecN): each component compares against zero, mirroring the scalar bool() conversion (an
    // integer source compares against an UNSIGNED zero, which only has to match in width).
    Value BoolVectorFrom(VectorType target, VectorType source, Value arg)
    {
        bool sourceFloat = IsFloating(source.Component);
        // glslang creates the bvec result type before smearing the zero it compares against, and creates
        // the scalar zero constant before the vector type it broadcasts into.
        uint resultType = SpvType(target);
        var zeroComp = new CScalar(sourceFloat ? source.Component : Scalar.UInt, 0, 0);
        Materialize(zeroComp);
        var zeroType = new VectorType(zeroComp.Kind, target.Size);
        uint zeroVec = Materialize(new CAgg(zeroType, [.. Enumerable.Repeat((CVal)zeroComp, target.Size)])).Id;
        return new Value(Emit(sourceFloat ? Op.FUnordNotEqual : Op.INotEqual, resultType, arg.Id, zeroVec), target, false);
    }

    Value SelectFromBool(VectorType vec, Value cond)
    {
        uint elementType = SpvType(new ScalarType(vec.Component));
        uint zero = Materialize(new CScalar(vec.Component, 0, 0)).Id;
        uint one = Materialize(new CScalar(vec.Component, 1, 1)).Id;
        uint zeroVec = _m.ConstantComposite(SpvType(vec), [.. Enumerable.Repeat(zero, vec.Size)]);
        uint oneVec = _m.ConstantComposite(SpvType(vec), [.. Enumerable.Repeat(one, vec.Size)]);
        return new Value(Emit(Op.Select, SpvType(vec), cond.Id, oneVec, zeroVec), vec, false);
    }

    // The transpose keeps the source matrix's component type (a dmat transposes to a dmat, not a mat).
    static MatrixType Transposed(GlslType t) => t is MatrixType m ? new MatrixType(m.Rows, m.Columns, m.Component) : throw new GlslParseException("transpose expects a matrix");

    Value ConvertScalar(Scalar target, Value arg)
    {
        var source = BaseScalar(arg.Type);
        if (source == target) return arg;
        uint typeId = SpvType(new ScalarType(target));

        if (IsIntegral(source) && IsIntegral(target))
            return NumericConvert(arg, new ScalarType(target), source, target);

        if (ConvertOp(source, target) is Op op)
            return Relaxed(new Value(Emit(op, typeId, arg.Id), new ScalarType(target), false), arg);

        if (target == Scalar.Bool)
        {
            // glslang compares an integer against an UNSIGNED zero (OpINotEqual needs matching width,
            // not signedness), so `bool(int)` uses a uint 0 too.
            var (zero, cmp) = IsFloating(source)
                ? (Materialize(new CScalar(source, 0, 0)).Id, Op.FUnordNotEqual)
                : (_m.ConstantInt(SpvType(Types.UInt), 0), Op.INotEqual);
            return new Value(Emit(cmp, SpvType(Types.Bool), arg.Id, zero), Types.Bool, false);
        }

        if (source == Scalar.Bool)
        {
            // `float(b)` selects between one and zero; glslang creates the 0 constant before the 1.
            uint zero = Materialize(new CScalar(target, 0, 0)).Id;
            uint one = Materialize(new CScalar(target, 1, 1)).Id;
            return new Value(Emit(Op.Select, typeId, arg.Id, one, zero), new ScalarType(target), false);
        }

        throw new GlslParseException($"conversion {source} -> {target} is not lowered yet");
    }

    // A numeric conversion between scalars or same-size vectors. glslang changes integer width with
    // SConvert/UConvert chosen by the SOURCE signedness, then a Bitcast only if the final signedness
    // still differs; a same-width integer sign change is a lone Bitcast. Non-integer pairs are one op.
    Value NumericConvert(Value arg, GlslType target, Scalar srcComp, Scalar dstComp)
    {
        uint dstType = SpvType(target);
        if (IsIntegral(srcComp) && IsIntegral(dstComp))
        {
            if (IntWidth(srcComp) == IntWidth(dstComp))
                return Relaxed(new Value(Emit(Op.Bitcast, dstType, arg.Id), target, false), arg);
            Scalar midComp = IsSigned(srcComp) ? SignedOfWidth(IntWidth(dstComp)) : UnsignedOfWidth(IntWidth(dstComp));
            uint mid = Emit(IsSigned(srcComp) ? Op.SConvert : Op.UConvert, SpvType(WithComponent(target, midComp)), arg.Id);
            if (arg.Relaxed) Relax(mid);
            return midComp == dstComp ? Relaxed(new Value(mid, target, false), arg)
                                      : Relaxed(new Value(Emit(Op.Bitcast, dstType, mid), target, false), arg);
        }
        return Relaxed(new Value(Emit(ConvertOp(srcComp, dstComp)!.Value, dstType, arg.Id), target, false), arg);
    }

    static GlslType WithComponent(GlslType t, Scalar c) => t is VectorType v ? new VectorType(c, v.Size) : new ScalarType(c);
    static int IntWidth(Scalar s) => s switch
    {
        Scalar.Int8 or Scalar.UInt8 => 8,
        Scalar.Int16 or Scalar.UInt16 => 16,
        Scalar.Int64 or Scalar.UInt64 => 64,
        _ => 32,
    };
    static Scalar SignedOfWidth(int w) => w switch { 8 => Scalar.Int8, 16 => Scalar.Int16, 64 => Scalar.Int64, _ => Scalar.Int };
    static Scalar UnsignedOfWidth(int w) => w switch { 8 => Scalar.UInt8, 16 => Scalar.UInt16, 64 => Scalar.UInt64, _ => Scalar.UInt };

    uint HalfType() => _m.TypeFloat(16);   // Float16 comes from Module.PostProcessNarrowTypes, as in glslang
    uint DoubleType() { _m.AddCapability(Capability.Float64); return _m.TypeFloat(64); }
    uint Int64Type(bool signed) { _m.AddCapability(Capability.Int64); return _m.TypeInt(64, signed); }
    uint Int8Type(bool signed) => _m.TypeInt(8, signed);   // so do Int8 ...
    uint Int16Type(bool signed) => _m.TypeInt(16, signed);   // ... and Int16

    // Every floating-point kind; they share the F* opcodes and differ only in width.
    static bool IsFloating(Scalar s) => s is Scalar.Float or Scalar.Half or Scalar.Double;
    static bool IsSigned(Scalar s) => s is Scalar.Int or Scalar.Int64 or Scalar.Int8 or Scalar.Int16;
    static bool IsUnsigned(Scalar s) => s is Scalar.UInt or Scalar.UInt64 or Scalar.UInt8 or Scalar.UInt16;
    static bool IsIntegral(Scalar s) => IsSigned(s) || IsUnsigned(s);
    static bool Is64Bit(Scalar s) => s is Scalar.Int64 or Scalar.UInt64;

    // The numeric-conversion opcode between two scalar kinds (also valid component-wise on vectors).
    static Op? ConvertOp(Scalar source, Scalar target) => (source, target) switch
    {
        _ when IsSigned(source) && IsFloating(target) => Op.ConvertSToF,
        _ when IsUnsigned(source) && IsFloating(target) => Op.ConvertUToF,
        _ when IsFloating(source) && IsSigned(target) => Op.ConvertFToS,
        _ when IsFloating(source) && IsUnsigned(target) => Op.ConvertFToU,
        // Any float-to-float width change (half/float/double) is an OpFConvert.
        var (a, b) when a != b && IsFloating(a) && IsFloating(b) => Op.FConvert,
        // Integer width changes sign-extend or zero-extend; a same-width sign change is a bitcast.
        _ when IsIntegral(source) && IsIntegral(target) && IntWidth(source) != IntWidth(target) =>
            IsSigned(source) ? Op.SConvert : Op.UConvert,
        _ when IsIntegral(source) && IsIntegral(target) && source != target => Op.Bitcast,
        _ => null,
    };

    // Whether an expression denotes a memory location (has an access chain), so a single-component
    // swizzle on it can be an OpAccessChain+OpLoad rather than a load-then-extract.
    bool IsAddressable(Expr e) => e switch
    {
        Ident id => LocalExists(id.Name) || GlobalExists(id.Name) || _blockMembers.ContainsKey(id.Name)
                    || Builtins.ContainsKey(id.Name)
                    || id.Name is "gl_Position" or "gl_PointSize" or "gl_ClipDistance" or "gl_CullDistance"
                              or "gl_TessLevelOuter" or "gl_TessLevelInner" or "gl_in" or "gl_out",
        Index ix => IsAddressable(ix.Target),
        // Only a member of a VECTOR is a swizzle; a struct field merely spelled from swizzle letters
        // ("arr", "st") is an ordinary field and stays addressable at any length.
        Member m => IsSwizzleName(m.Name) && InferType(m.Target) is VectorType
            ? m.Name.Length == 1 && IsAddressable(m.Target)
            : IsAddressable(m.Target),
        _ => false,
    };

    // `v.xyz` on a vec3 selects every component in order, so it addresses the whole vector: glslang
    // simplifies such a swizzle away, making the read just `v` and the write a plain store.
    static bool IsIdentitySwizzle(int[] indices, GlslType source) =>
        source is VectorType v && indices.Length == v.Size && !indices.Where((c, i) => c != i).Any();

    Value LowerSwizzle(Member m)
    {
        if (InferType(m.Target) is VectorType targetVec) ValidateSwizzle(targetVec, m.Name, writeMask: false);
        // A scalar swizzles like a 1-component vector: ".x" reads the scalar (glslang accepts it);
        // anything longer, or selecting another component, is a source error (glslang: "vector
        // swizzle selection out of range") -- indexing a scalar's single component would crash.
        // Only fires on confident scalar inference: an unknown target type lowers as before.
        else if (InferType(m.Target) is ScalarType && (m.Name.Length > 1 || SwizzleIndex(m.Name[0]) != 0))
            throw new GlslParseException($"'{m.Name}' : vector swizzle selection out of range", m.Line);
        int[] indices = [.. m.Name.Select(SwizzleIndex)];
        // glslang composes chained swizzles into ONE selection rather than shuffling twice: reading
        // `v.xyz.zyx` selects v's components 2,1,0 directly.
        while (m.Target is Member inner && IsSwizzleName(inner.Name) && InferType(inner.Target) is VectorType vt)
        {
            // Each link of a chained swizzle names components of ITS OWN target, so validate it here:
            // the outer link was checked against the intermediate type, which hides an over-long inner one.
            ValidateSwizzle(vt, inner.Name, writeMask: false);
            SpvType(new VectorType(vt.Component, inner.Name.Length));   // glslang types the intermediate swizzle
            int[] outer = [.. inner.Name.Select(SwizzleIndex)];
            indices = [.. indices.Select(i => outer[i])];
            m = m with { Target = inner.Target };
        }
        // A swizzle selecting every component in order is a no-op, and glslang drops it entirely.
        if (IsIdentitySwizzle(indices, InferType(m.Target))) return LowerExpr(m.Target);
        // A single-component swizzle of an addressable vector (`v.x`) is a component access chain then a
        // load, matching glslang (which indexes with an unsigned constant); on an r-value it falls back
        // to OpCompositeExtract below.
        if (indices.Length == 1 && IsAddressable(m.Target) && BuildAccess(m.Target) is { Type: VectorType bv } baseC)
        {
            // glslang resolves the swizzle base's DECLARED type as part of the swizzle, so a block's
            // bool-vector member creates the bvec type here even though the load uses a uint component.
            if (baseC.Layout is not null && InferType(m.Target) is VectorType { Component: Scalar.Bool } db)
                SpvType(db);
            var elem = new ScalarType(bv.Component);
            uint idx = _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)indices[0]);
            var loaded = Load(elem, Collapse(baseC with { Indices = [.. baseC.Indices, idx], Type = elem }), baseC.Layout);
            // A component read from a relaxed symbol is itself relaxed.
            if (!loaded.Relaxed && _relaxedIds.Contains(baseC.Base) && bv.Component is not Scalar.Bool)
                loaded = loaded with { Id = Relax(loaded.Id), Relaxed = true };
            // A component of a block's bool-vector member loads as uint and converts back to bool.
            return FromStored(loaded, InferType(m));
        }

        // A multi-component swizzle is a VectorShuffle. For an addressable source glslang materializes
        // it (access chain), THEN creates the result vector type, THEN loads - so do the same.
        if (indices.Length > 1 && InferType(m.Target) is VectorType ivt && IsAddressable(m.Target)
            && BuildAccess(m.Target) is { Type: VectorType cvt } chain)
        {
            // The shuffle works in the SOURCE's component type (a block bool vector is stored as uint),
            // then the result converts back to the declared component.
            var resultType = new VectorType(cvt.Component, indices.Length);
            uint resultTypeId = SpvType(resultType);
            uint srcId = Load(chain.Type, Collapse(chain), chain.Layout).Id;
            var sh = InstrPool.Rent(Op.VectorShuffle, resultTypeId, _m.AllocId()).Add(srcId, srcId);
            foreach (int i in indices) sh.Add((uint)i);
            _code.Add(sh);
            return FromStored(new Value(sh.ResultId, resultType, false), new VectorType(ivt.Component, indices.Length));
        }

        var src = LowerExpr(m.Target);
        if (src.Type is not VectorType vec)
            throw new GlslParseException($"type '{Types.Name(src.Type)}' has no member '.{m.Name}'");

        if (indices.Length == 1)
        {
            // glslang turns a single-component swizzle into an access-chain index, materializing the
            // unsigned component constant, and only then notices the index is constant and extracts with
            // a literal instead - so the constant is created either way, even though nothing reads it.
            _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)indices[0]);
            return new Value(Emit(Op.CompositeExtract, SpvType(new ScalarType(vec.Component)), src.Id, (uint)indices[0]), new ScalarType(vec.Component), false);
        }

        var resultType2 = new VectorType(vec.Component, indices.Length);
        var shuffle = InstrPool.Rent(Op.VectorShuffle, SpvType(resultType2), _m.AllocId()).Add(src.Id, src.Id);
        foreach (int i in indices) shuffle.Add((uint)i);
        _code.Add(shuffle);
        return new Value(shuffle.ResultId, resultType2, false);
    }

    // glslang's smearScalar always emits a runtime OpCompositeConstruct (never a folded constant),
    // even for a constant scalar - so promoting a scalar builtin/operator argument to a vector matches.
    Value Splat(Value scalar, VectorType vt) =>
        new(Emit(Op.CompositeConstruct, SpvType(vt), [.. Enumerable.Repeat(scalar.Id, vt.Size)]), vt, false);

    // ---- gl_Position (the gl_PerVertex output block) ----

    void CreateGlPerVertex()
    {
        // The gl_PerVertex output block (gl_Position/gl_PointSize/gl_Clip/CullDistance) only exists in
        // the vertex-processing stages. Referencing it in a fragment or compute shader is invalid GLSL.
        if (stage is ShaderStage.Fragment or ShaderStage.Compute)
            throw new GlslParseException($"gl_Position/gl_PointSize are not available in a {stage} shader");

        uint tStruct = _perVertexOut is { } members ? RedeclaredPerVertexStruct(members) : DefaultPerVertexStruct();

        uint ptr = _m.TypePointer(StorageClass.Output, tStruct);
        _glPerVertex = _m.GlobalVariable(ptr, StorageClass.Output);
        _m.AddName(_glPerVertex, "");
        if (_usesStreams) _m.Decorate(_glPerVertex, Decoration.Stream, 0);
        // With transform feedback active the gl_PerVertex output block belongs to xfb_buffer 0.
        if (_xfbMode)
        {
            _m.Decorate(_glPerVertex, Decoration.XfbBuffer, 0);
            _m.Decorate(_glPerVertex, Decoration.XfbStride, (uint)_xfbStrides.GetValueOrDefault(0));
        }
        _interface.Add(_glPerVertex);
    }

    uint DefaultPerVertexStruct()
    {
        uint tFloat = _m.TypeFloat(32);
        uint tVec4 = SpvType(Vec4);
        uint length1 = _m.ConstantInt(_m.TypeInt(32, signed: false), 1);
        uint tArray = _m.TypeArray(tFloat, length1);
        uint tStruct = _m.TypeStruct(tVec4, tFloat, tArray, tArray);
        _m.AddName(tStruct, "gl_PerVertex");
        _m.AddMemberName(tStruct, 0, "gl_Position");
        _m.AddMemberName(tStruct, 1, "gl_PointSize");
        _m.AddMemberName(tStruct, 2, "gl_ClipDistance");
        _m.AddMemberName(tStruct, 3, "gl_CullDistance");
        // `invariant gl_Position;` decorates the Position member, ahead of its BuiltIn decoration.
        if (_invariantPosition) _m.MemberDecorate(tStruct, 0, Decoration.Invariant);
        _m.MemberDecorate(tStruct, 0, Decoration.BuiltIn, (uint)BuiltIn.Position);
        _m.MemberDecorate(tStruct, 1, Decoration.BuiltIn, (uint)BuiltIn.PointSize);
        _m.MemberDecorate(tStruct, 2, Decoration.BuiltIn, (uint)BuiltIn.ClipDistance);
        _m.MemberDecorate(tStruct, 3, Decoration.BuiltIn, (uint)BuiltIn.CullDistance);
        _m.Decorate(tStruct, Decoration.Block);
        return tStruct;
    }

    // The redeclared output gl_PerVertex block: members in declared order, gl_Clip/CullDistance sized as
    // declared. Member types are created in order so the id allocation matches glslang; the ClipDistance/
    // CullDistance capabilities come from the declaration, in member order.
    uint RedeclaredPerVertexStruct(List<(string Name, GlslType Type)> members)
    {
        uint[] memberTypes = [.. members.Select(m => SpvType(m.Type))];
        uint tStruct = _m.TypeStruct(memberTypes);
        _m.AddName(tStruct, "gl_PerVertex");
        for (int i = 0; i < members.Count; i++)
        {
            _m.AddMemberName(tStruct, i, members[i].Name);
            if (members[i].Name == "gl_Position" && _invariantPosition) _m.MemberDecorate(tStruct, i, Decoration.Invariant);
            _m.MemberDecorate(tStruct, i, Decoration.BuiltIn, (uint)PerVertexBuiltIn(members[i].Name));
            if (members[i].Name == "gl_ClipDistance") _m.AddCapability(Capability.ClipDistance);
            if (members[i].Name == "gl_CullDistance") _m.AddCapability(Capability.CullDistance);
        }
        _m.Decorate(tStruct, Decoration.Block);
        return tStruct;
    }

    // The output gl_PerVertex member a name selects: its index and type, honoring a redeclaration.
    (int Index, GlslType Type) PerVertexOutMember(string name)
    {
        if (_perVertexOut is { } m)
        {
            int i = m.FindIndex(x => x.Name == name);
            if (i >= 0) return (i, m[i].Type);
        }
        return name switch
        {
            "gl_Position" => (0, Vec4),
            "gl_PointSize" => (1, Types.Float),
            "gl_ClipDistance" => (2, ClipCullType),
            _ => (3, ClipCullType),
        };
    }

    // ---- emission & type mapping ----

    // Fixed-arity overloads win overload resolution over the params one, so the common 0..4 operand emits
    // (nearly every instruction) do not allocate an operand array.
    uint Emit(Op op, uint typeId)
    {
        uint id = _m.AllocId();
        _code.Add(InstrPool.Rent(op, typeId, id));
        return id;
    }

    uint Emit(Op op, uint typeId, uint a)
    {
        uint id = _m.AllocId();
        _code.Add(InstrPool.Rent(op, typeId, id).Add(a));
        return id;
    }

    uint Emit(Op op, uint typeId, uint a, uint b)
    {
        uint id = _m.AllocId();
        _code.Add(InstrPool.Rent(op, typeId, id).Add(a, b));
        return id;
    }

    uint Emit(Op op, uint typeId, uint a, uint b, uint c)
    {
        uint id = _m.AllocId();
        _code.Add(InstrPool.Rent(op, typeId, id).Add(a, b, c));
        return id;
    }

    uint Emit(Op op, uint typeId, uint a, uint b, uint c, uint d)
    {
        uint id = _m.AllocId();
        _code.Add(InstrPool.Rent(op, typeId, id).Add(a, b, c, d));
        return id;
    }

    uint Emit(Op op, uint typeId, params uint[] operands)
    {
        uint id = _m.AllocId();
        _code.Add(InstrPool.Rent(op, typeId, id).Add(operands));
        return id;
    }

    void Store(uint pointer, uint value, MemQual mem = MemQual.None)
    {
        var inst = InstrPool.Rent(Op.Store).Add(pointer, value);
        if (VmmOperands(mem, store: true) is (uint mask, uint scope)) inst.Add(mask, scope);
        _code.Add(inst);
    }

    // Stores an aggregate whose SPIR-V type may differ from the target's even though both are the same
    // GLSL type: a struct inside a block carries the block's Offsets, its plain counterpart does not.
    // Such a copy has to go member by member (glslang's multiTypeStore); anything else is a plain store.
    void MultiTypeStore(Chain target, Value value)
    {
        BlockLayout? source = value.Layout;
        if (SpvType(target.Type, target.Layout) == SpvType(value.Type, source))
        {
            // A store through a PhysicalStorageBuffer (buffer_reference) pointer carries an Aligned
            // memory operand with the reference's buffer_reference_align (default 16).
            if (target.Physical)
                _code.Add(InstrPool.Rent(Op.Store).Add(Collapse(target), value.Id, 0x2, PhysAligned(target.PhysAlign, target.PhysOffset)));
            else
                Store(Collapse(target), value.Id, target.Mem);
            return;
        }
        // SPIR-V 1.4's OpCopyLogical converts between two aggregates that differ only in decorations, so
        // glslang uses it instead of the member-wise copy - unless exactly one side holds a bool, which a
        // block stores as uint and no logical copy can bridge. The target chain comes first, then the copy.
        if (version >= SpirVVersion.V1_4 && target.Type is StructType or ArrayType
            && (target.Layout is null && ContainsBool(target.Type)) == (source is null && ContainsBool(value.Type)))
        {
            uint pointer = Collapse(target);
            uint copy = Emit(Op.CopyLogical, SpvType(target.Type, target.Layout), value.Id);
            if (target.Physical)
                _code.Add(InstrPool.Rent(Op.Store).Add(pointer, copy, 0x2, PhysAligned(target.PhysAlign, target.PhysOffset)));
            else
                Store(pointer, copy, target.Mem);
            return;
        }
        // A scalar/vector leaf whose SPIR-V types differ across the two universes only does so because
        // one stores a bool as uint; convert to what the target holds, THEN build the target chain
        // (glslang emits the conversion before the store's access chain).
        if (target.Type is not (StructType or ArrayType))
        {
            uint converted = ToStored(FromStored(value, DeclaredScalar(value.Type)), target.Type).Id;
            Store(Collapse(target), converted, target.Mem);
            return;
        }
        // glslang collapses the target to a single pointer once, then indexes each member from THAT
        // pointer rather than re-chaining from the original base. The two universes correspond field for
        // field: the target keeps its declared type; the source, if it is a block, stores bools as uint.
        var at = target with { Base = Collapse(target), Indices = [] };
        var members = at.Type switch
        {
            ArrayType arr => [.. Enumerable.Repeat(arr.Element, arr.Length)],
            StructType s => s.Fields.Select(f => f.Type).ToList(),
            _ => new List<GlslType>(),
        };
        for (int i = 0; i < members.Count; i++)
        {
            var stored = source is not null ? BlockStored(members[i]) : members[i];
            uint part = Emit(Op.CompositeExtract, SpvType(stored, source), value.Id, (uint)i);
            // A block target holds its bools as uint too, so the leaf converts into what it stores.
            MultiTypeStore(at with { Indices = [MemberIndexConst(i)], Type = target.Layout is not null ? BlockStored(members[i]) : members[i] },
                           new Value(part, stored, false, Layout: source));
        }
    }

    // A stored scalar/vector back to its declared (bool) shape, for the FromStored step of a copy leaf.
    static GlslType DeclaredScalar(GlslType stored) => stored switch
    {
        ScalarType { Kind: Scalar.UInt } => Types.Bool,
        VectorType { Component: Scalar.UInt } v => new VectorType(Scalar.Bool, v.Size),
        _ => stored,
    };

    // A mediump/lowp declaration is a RelaxedPrecision decoration on the id (glslang emits these even in
    // a desktop profile, where precision otherwise carries no semantics).
    uint Relax(uint id)
    {
        if (_relaxedIds.Add(id)) _m.Decorate(id, Decoration.RelaxedPrecision);
        return id;
    }

    static bool IsRelaxed(Qualifiers q) => q.Has("mediump") || q.Has("lowp");

    // Loading through a relaxed pointer yields a relaxed value, so the result is decorated too.
    Value Load(GlslType type, uint pointer, BlockLayout? layout = null, MemQual mem = MemQual.None)
    {
        uint typeId = SpvType(type, layout);
        uint id = _m.AllocId();
        var inst = InstrPool.Rent(Op.Load, typeId, id).Add(pointer);
        if (VmmOperands(mem, store: false) is (uint mask, uint scope)) inst.Add(mask, scope);
        _code.Add(inst);
        if (_nonUniformIds.Contains(pointer)) { _m.Decorate(id, Decoration.NonUniform); _nonUniformIds.Add(id); }
        bool relaxed = _relaxedIds.Contains(pointer);
        if (relaxed) Relax(id);
        return new Value(id, type, false, relaxed, layout);
    }

    uint SpvType(GlslType type) => SpvType(type, null);

    // Layout names the block ruleset a type is being viewed under (null outside any block). It only
    // changes aggregates: those carry the block's Offsets/ArrayStride and so are distinct types.
    uint SpvType(GlslType type, BlockLayout? blockLayout) => blockLayout is not BlockLayout layout ? SpvTypePlain(type) : type switch
    {
        StructType st when _blockStructIds.TryGetValue(st, out var id) => id,
        StructType st when _layoutStructIds.TryGetValue((st, layout, false), out var id) => id,
        StructType st when _layoutStructIds.TryGetValue((st, layout, true), out var id) => id,
        ArrayType a when _layoutArrayIds.TryGetValue((BlockStored(a.Element), a.Length, layout), out var id) => id,
        // Every laid-out aggregate is built once, when its block struct is, so anything unknown here was
        // reached by a path that cannot name the block's copy. Emitting the unlaid-out type would
        // silently produce a mismatched access chain, so refuse instead.
        StructType or ArrayType or RuntimeArrayType =>
            throw new GlslParseException($"'{type}' is not addressable as a whole block member yet"),
        _ => SpvTypePlain(type),
    };

    uint SpvTypePlain(GlslType type) => type switch
    {
        ScalarType { Kind: Scalar.Void } => _m.TypeVoid(),
        ScalarType { Kind: Scalar.Bool } => _m.TypeBool(),
        ScalarType { Kind: Scalar.Int } => _m.TypeInt(32, signed: true),
        ScalarType { Kind: Scalar.UInt } => _m.TypeInt(32, signed: false),
        ScalarType { Kind: Scalar.Float } => _m.TypeFloat(32),
        ScalarType { Kind: Scalar.Half } => HalfType(),
        ScalarType { Kind: Scalar.Double } => DoubleType(),
        ScalarType { Kind: Scalar.Int64 } => Int64Type(signed: true),
        ScalarType { Kind: Scalar.UInt64 } => Int64Type(signed: false),
        ScalarType { Kind: Scalar.Int8 } => Int8Type(signed: true),
        ScalarType { Kind: Scalar.UInt8 } => Int8Type(signed: false),
        ScalarType { Kind: Scalar.Int16 } => Int16Type(signed: true),
        ScalarType { Kind: Scalar.UInt16 } => Int16Type(signed: false),
        VectorType v => _m.TypeVector(SpvType(new ScalarType(v.Component)), v.Size),
        MatrixType m => _m.TypeMatrix(SpvType(new VectorType(m.Component, m.Rows)), m.Columns),
        ArrayType { SpecLength: { } spec } a => _m.TypeArray(SpvType(a.Element), SpecConstantExpr(spec).Id),
        ArrayType a => _m.TypeArray(SpvType(a.Element), _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)a.Length)),
        RuntimeArrayType r => _m.TypeRuntimeArray(SpvType(r.Element)),
        StructType st when _blockStructIds.TryGetValue(st, out var id) => id,
        StructType st when _plainStructIds.TryGetValue(st, out var id) => id,
        // Unique, not deduped: two distinct GLSL structs with identical members are still distinct types.
        StructType st => _plainStructIds[st] = NameStruct(st, _m.TypeStructUnique([.. st.Fields.Select(f => SpvType(f.Type))])),
        CoopMatType cm => CoopMatSpvType(cm),
        SamplerType s => SamplerSpvType(s),
        TextureType t => _m.TypeImage(_m.TypeFloat(32), t.Dim, t.Shadow ? 1 : 0, t.Arrayed ? 1 : 0, 0, 1, ImageFormat.Unknown),
        ImageType img => ImageSpvType(img),
        PureSamplerType => _m.TypeSampler(),
        AccelStructType => AccelStructSpvType(),
        RayQueryType => RayQuerySpvType(),
        BufferReferenceType r => BufferRefPtr(r.Name),
        _ => throw new GlslParseException($"type '{type}' has no SPIR-V mapping yet"),
    };

    // OpTypeCooperativeMatrixKHR takes the component type plus scope/rows/cols/use as constant <id>s;
    // glslang creates those uint constants (rows and cols share one when equal) before the type.
    uint CoopMatSpvType(CoopMatType cm)
    {
        _m.AddCapability(Capability.CooperativeMatrixKHR);
        _m.AddExtension("SPV_KHR_cooperative_matrix");
        EnableVulkanMemoryModel();   // cooperative matrices require the Vulkan memory model
        uint comp = SpvType(new ScalarType(cm.Component));
        uint u = _m.TypeInt(32, signed: false);
        uint scope = _m.ConstantInt(u, cm.Scope);
        uint rows = _m.ConstantInt(u, cm.Rows);
        uint cols = _m.ConstantInt(u, cm.Cols);
        uint use = _m.ConstantInt(u, cm.Use);
        return _m.TypeCooperativeMatrixKHR(comp, scope, rows, cols, use);
    }

    static Op ArithOp(string op, GlslType type)
    {
        var scalar = BaseScalar(type);
        bool f = IsFloating(scalar);
        bool s = IsSigned(scalar);
        return op switch
        {
            "+" => f ? Op.FAdd : Op.IAdd,
            "-" => f ? Op.FSub : Op.ISub,
            "*" => f ? Op.FMul : Op.IMul,
            "/" => f ? Op.FDiv : s ? Op.SDiv : Op.UDiv,
            "%" => f ? Op.FMod : s ? Op.SMod : Op.UMod,
            _ => throw new GlslParseException($"operator '{op}' is not lowered yet"),
        };
    }

    static Op CompareOp(string op, Scalar scalar)
    {
        bool f = IsFloating(scalar);
        bool s = IsSigned(scalar);
        bool b = scalar == Scalar.Bool;
        return op switch
        {
            "<" => f ? Op.FOrdLessThan : s ? Op.SLessThan : Op.ULessThan,
            ">" => f ? Op.FOrdGreaterThan : s ? Op.SGreaterThan : Op.UGreaterThan,
            "<=" => f ? Op.FOrdLessThanEqual : s ? Op.SLessThanEqual : Op.ULessThanEqual,
            ">=" => f ? Op.FOrdGreaterThanEqual : s ? Op.SGreaterThanEqual : Op.UGreaterThanEqual,
            "==" => f ? Op.FOrdEqual : b ? Op.LogicalEqual : Op.IEqual,
            "!=" => f ? Op.FUnordNotEqual : b ? Op.LogicalNotEqual : Op.INotEqual,
            _ => throw new GlslParseException($"comparison '{op}' is not lowered yet"),
        };
    }

    // The result type of an `a * b` where one side may be a matrix, mirroring Arithmetic's opcode choice.
    static GlslType InferMul(GlslType l, GlslType r) => (l, r) switch
    {
        (MatrixType lm, VectorType) => new VectorType(lm.Component, lm.Rows),
        (VectorType, MatrixType rm) => new VectorType(rm.Component, rm.Columns),
        (MatrixType lm, MatrixType rm) => new MatrixType(rm.Columns, lm.Rows, lm.Component),
        (MatrixType, ScalarType) => l,
        (ScalarType, MatrixType) => r,
        (VectorType v, _) => v,
        _ => r,
    };

    static bool IsComparison(string op) => op is "<" or ">" or "<=" or ">=" or "==" or "!=";
    static bool IsCompoundAssign(string op) => op is "+=" or "-=" or "*=" or "/=" or "%=" or "&=" or "|=" or "^=" or "<<=" or ">>=";

    static int SwizzleIndex(char c) => c switch
    {
        'x' or 'r' or 's' => 0,
        'y' or 'g' or 't' => 1,
        'z' or 'b' or 'p' => 2,
        'w' or 'a' or 'q' => 3,
        _ => throw new GlslParseException($"invalid swizzle component '{c}'"),
    };

    // Lightweight, non-emitting type inference, used to pick between overloads at a call site.
    GlslType InferType(Expr e)
    {
        if (++_exprDepth > Parser.MaxNestingDepth) { _exprDepth--; throw new GlslParseException($"expression nesting is too deep (limit {Parser.MaxNestingDepth})", _lowerLine); }
        try { return InferTypeCore(e); }
        finally { _exprDepth--; }
    }

    GlslType InferTypeCore(Expr e) => e switch
    {
        Literal { Kind: TokenKind.FloatConst } => Types.Float,
        Literal { Kind: TokenKind.DoubleConst } => Types.Double,
        Literal { Kind: TokenKind.HalfConst } => Types.Half,
        Literal { Kind: TokenKind.IntConst } => Types.Int,
        Literal { Kind: TokenKind.UintConst } => Types.UInt,
        Literal { Kind: TokenKind.Int64Const } => Types.Int64,
        Literal { Kind: TokenKind.Uint64Const } => Types.UInt64,
        Literal { Kind: TokenKind.BoolConst } => Types.Bool,
        Ident id => InferIdent(id.Name),
        Unary { Op: "!" } => Types.Bool,
        Unary u => InferType(u.Operand),
        Binary b when IsComparison(b.Op) || b.Op is "&&" or "||" or "^^" => Types.Bool,
        Binary { Op: "*" } b => InferMul(InferType(b.Left), InferType(b.Right)),
        Binary b => InferType(b.Left) is VectorType v ? v : InferType(b.Right),
        Conditional c => InferType(c.Then),
        Index ix => InferType(ix.Target) switch
        {
            ArrayType a => a.Element,
            RuntimeArrayType r => r.Element,
            MatrixType m => new VectorType(m.Component, m.Rows),
            VectorType v => new ScalarType(v.Component),
            _ => throw new GlslParseException("cannot infer index type"),
        },
        Member m => InferMember(m),
        Method { Name: "length" } => Types.Int,
        Call c when Types.IsTypeName(c.Name) => c.IsArray ? ArrayCtorType(c, Resolve(c.Name)) : Resolve(c.Name),
        Call c when _structs.TryGetValue(c.Name, out var st) => c.IsArray ? ArrayCtorType(c, st) : st,
        Call { IsArray: false } c when _bufferRefs.ContainsKey(c.Name) => new BufferReferenceType(c.Name),
        Call c when CallsUserFunction(c) => ResolveOverload(_userFunctions[c.Name], c.Args).ReturnType,
        Call c => InferBuiltinType(c),
        _ => throw new GlslParseException($"cannot infer type of '{e.GetType().Name}' for overload resolution"),
    };

    // Best-effort return type of a built-in call, used only to classify member access and overloads.
    GlslType InferBuiltinType(Call c) => c.Name switch
    {
        "texture" or "textureLod" or "textureGrad" or "textureOffset" or "textureLodOffset"
            or "textureGradOffset" or "textureProj" or "textureProjLod" or "textureProjOffset"
            or "textureProjGrad" or "textureProjLodOffset" or "textureProjGradOffset"
            or "textureGather" or "textureGatherOffset" or "textureGatherOffsets"
            or "texelFetch" or "texelFetchOffset" => SampleResultType(c),
        "imageLoad" or "subpassLoad" => InferType(c.Args[0]) is ImageType img ? new VectorType(img.Sampled, 4) : Vec4,
        // These reduce to the component scalar (a double matrix/vector yields a double, not a float).
        "dot" or "length" or "distance" => new ScalarType(BaseScalar(InferType(c.Args[0]))),
        "cross" => InferType(c.Args[0]),
        "textureSamples" or "textureQueryLevels" => Types.Int,
        "any" or "all" or "sparseTexelsResidentARB" => Types.Bool,
        "isnan" or "isinf" => ReplaceComponent(InferType(c.Args[0]), Scalar.Bool),
        "lessThan" or "lessThanEqual" or "greaterThan" or "greaterThanEqual" or "equal" or "notEqual" => ReplaceComponent(InferType(c.Args[0]), Scalar.Bool),
        "determinant" => new ScalarType(BaseScalar(InferType(c.Args[0]))),
        "packInt2x32" => Types.Int64,
        "packUint2x32" => Types.UInt64,
        "unpackInt2x32" => new VectorType(Scalar.Int, 2),
        "unpackUint2x32" => new VectorType(Scalar.UInt, 2),
        "packDouble2x32" => Types.Double,
        "unpackDouble2x32" => new VectorType(Scalar.UInt, 2),
        "floatBitsToInt" => ReplaceComponent(InferType(c.Args[0]), Scalar.Int),
        "floatBitsToUint" => ReplaceComponent(InferType(c.Args[0]), Scalar.UInt),
        "intBitsToFloat" or "uintBitsToFloat" => ReplaceComponent(InferType(c.Args[0]), Scalar.Float),
        "packHalf2x16" or "packUnorm2x16" or "packSnorm2x16" or "packUnorm4x8" or "packSnorm4x8" => Types.UInt,
        "unpackHalf2x16" or "unpackUnorm2x16" or "unpackSnorm2x16" => new VectorType(Scalar.Float, 2),
        "unpackUnorm4x8" or "unpackSnorm4x8" => Vec4,
        "bitCount" or "findLSB" or "findMSB" => ReplaceComponent(InferType(c.Args[0]), Scalar.Int),
        // Subgroup operations: return types exactly as lowered (LowerSubgroup / SubgroupElect / ...).
        // The arg0 fallback below is right for the shuffle/broadcast/quad/arithmetic/partitioned/rotate
        // forms (they return their value argument's type) but wrong for the zero-argument subgroupElect
        // (which fell back to float, breaking its use as a ternary condition) and for the ballot family,
        // which crosses bool<->uvec4 and yields uint bit counts.
        "subgroupElect" or "subgroupAll" or "subgroupAny" or "subgroupAllEqual"
            or "subgroupInverseBallot" or "subgroupBallotBitExtract" or "helperInvocationEXT" => Types.Bool,
        "subgroupBallot" or "subgroupPartitionNV" => new VectorType(Scalar.UInt, 4),
        "subgroupBallotBitCount" or "subgroupBallotInclusiveBitCount" or "subgroupBallotExclusiveBitCount"
            or "subgroupBallotFindLSB" or "subgroupBallotFindMSB" => Types.UInt,
        _ => c.Args.Count > 0 ? InferType(c.Args[0]) : Types.Float,
    };

    // A sampling built-in's result follows the sampler's component type. A shadow sampler compares and
    // yields a single float, except when gathering, which returns the four compared values.
    GlslType SampleResultType(Call c) => InferType(c.Args[0]) switch
    {
        SamplerType { Shadow: true } when !c.Name.StartsWith("textureGather", StringComparison.Ordinal) => Types.Float,
        SamplerType s => new VectorType(s.Sampled, 4),
        _ => Vec4,
    };

    // Predefined int constants from extensions: fragment shading rate flags, and the memory scope /
    // semantics values from GL_KHR_memory_scope_semantics. glslang types them all as (signed) int.
    static readonly Dictionary<string, int> ShadingRateFlags = new()
    {
        ["gl_ShadingRateFlag2VerticalPixelsEXT"] = 1,
        ["gl_ShadingRateFlag4VerticalPixelsEXT"] = 2,
        ["gl_ShadingRateFlag2HorizontalPixelsEXT"] = 4,
        ["gl_ShadingRateFlag4HorizontalPixelsEXT"] = 8,
        ["gl_ScopeDevice"] = 1, ["gl_ScopeWorkgroup"] = 2, ["gl_ScopeSubgroup"] = 3,
        ["gl_ScopeInvocation"] = 4, ["gl_ScopeQueueFamily"] = 5,
        ["gl_StorageSemanticsNone"] = 0, ["gl_StorageSemanticsBuffer"] = 0x40, ["gl_StorageSemanticsShared"] = 0x100,
        ["gl_StorageSemanticsImage"] = 0x800, ["gl_StorageSemanticsOutput"] = 0x1000,
        ["gl_SemanticsRelaxed"] = 0, ["gl_SemanticsAcquire"] = 0x2, ["gl_SemanticsRelease"] = 0x4,
        ["gl_SemanticsAcquireRelease"] = 0x8, ["gl_SemanticsMakeAvailable"] = 0x2000,
        ["gl_SemanticsMakeVisible"] = 0x4000, ["gl_SemanticsVolatile"] = 0x8000,
        ["gl_MatrixUseA"] = 0, ["gl_MatrixUseB"] = 1, ["gl_MatrixUseAccumulator"] = 2,
        ["gl_CooperativeMatrixLayoutRowMajor"] = 0, ["gl_CooperativeMatrixLayoutColumnMajor"] = 1,
    };

    // GL_EXT_ray_tracing predefined `const uint` ray-flag and hit-kind constants (compile-time literals).
    static readonly Dictionary<string, int> RayFlags = new()
    {
        ["gl_RayFlagsNoneEXT"] = 0, ["gl_RayFlagsOpaqueEXT"] = 1, ["gl_RayFlagsNoOpaqueEXT"] = 2,
        ["gl_RayFlagsTerminateOnFirstHitEXT"] = 4, ["gl_RayFlagsSkipClosestHitShaderEXT"] = 8,
        ["gl_RayFlagsCullBackFacingTrianglesEXT"] = 16, ["gl_RayFlagsCullFrontFacingTrianglesEXT"] = 32,
        ["gl_RayFlagsCullOpaqueEXT"] = 64, ["gl_RayFlagsCullNoOpaqueEXT"] = 128,
        ["gl_RayFlagsSkipTrianglesEXT"] = 256, ["gl_RayFlagsSkipAABBEXT"] = 512,
        ["gl_HitKindFrontFacingTriangleEXT"] = 254, ["gl_HitKindBackFacingTriangleEXT"] = 255,
        // GL_EXT_ray_query intersection-type constants.
        ["gl_RayQueryCandidateIntersectionEXT"] = 0, ["gl_RayQueryCommittedIntersectionEXT"] = 1,
        ["gl_RayQueryCommittedIntersectionNoneEXT"] = 0, ["gl_RayQueryCommittedIntersectionTriangleEXT"] = 1,
        ["gl_RayQueryCommittedIntersectionGeneratedEXT"] = 2,
        ["gl_RayQueryCandidateIntersectionTriangleEXT"] = 0, ["gl_RayQueryCandidateIntersectionAABBEXT"] = 1,
        // GL_NV_ray_tracing ray-flag and hit-kind constants share the EXT values.
        ["gl_RayFlagsNoneNV"] = 0, ["gl_RayFlagsOpaqueNV"] = 1, ["gl_RayFlagsNoOpaqueNV"] = 2,
        ["gl_RayFlagsTerminateOnFirstHitNV"] = 4, ["gl_RayFlagsSkipClosestHitShaderNV"] = 8,
        ["gl_RayFlagsCullBackFacingTrianglesNV"] = 16, ["gl_RayFlagsCullFrontFacingTrianglesNV"] = 32,
        ["gl_RayFlagsCullOpaqueNV"] = 64, ["gl_RayFlagsCullNoOpaqueNV"] = 128,
        ["gl_HitKindFrontFacingTriangleNV"] = 254, ["gl_HitKindBackFacingTriangleNV"] = 255,
    };

    GlslType InferIdent(string name)
    {
        if (ShadingRateFlags.ContainsKey(name)) return Types.Int;
        if (RayFlags.ContainsKey(name)) return Types.UInt;
        if (_valueParams.TryGetValue(name, out var vp)) return vp.Type;
        if (name == "gl_WorkGroupSize" && stage == ShaderStage.Compute) return UVec3;
        if (_constants.TryGetValue(name, out var c)) return c.Type;
        if (_constValues.TryGetValue(name, out var cv)) return cv is CAgg agg ? agg.Type : new ScalarType(((CScalar)cv).Kind);
        if (_pendingSpecConstants.TryGetValue(name, out var sc)) return sc.Type;
        if (_pendingSpecExprs.TryGetValue(name, out var se)) return InferType(se);
        if (_locals.TryGetValue(name, out var l)) return l.Type;
        if (_pendingLocals.TryGetValue(name, out var pt)) return pt.Type;
        if (_pendingPrivates.TryGetValue(name, out var pp)) return pp;
        if (GlobalType(name) is { } gt) return gt;
        if (_blockMembers.TryGetValue(name, out var bm)) return bm.Type;
        if (Builtins.TryGetValue(name, out var b)) return b.Type;
        if (PerVertexType(name) is { } pv) return pv;
        if (name == "gl_in") return new ArrayType(PerVertexStruct, _inVertexCount);
        if (name == "gl_out") return new ArrayType(PerVertexStruct, _outVertexCount);
        // glslang: "'zz' : undeclared identifier" - an identifier nothing can resolve is undeclared,
        // not an overload-resolution failure.
        throw new GlslParseException($"unresolved identifier '{Clip(name)}'");
    }

    GlslType InferMember(Member m)
    {
        // gl_in[i].gl_Position / gl_out[i].gl_Position: the per-vertex block's position member.
        if (m is { Name: "gl_Position", Target: Index { Target: Ident { Name: "gl_in" or "gl_out" } } }) return Vec4;
        var target = InferType(m.Target);
        if (target is StructType st) return st.Fields[IndexOfField(st, m.Name)].Type;
        if (target is BufferReferenceType brt) { var rst = _bufferRefs[brt.Name]; return rst.Fields[IndexOfField(rst, m.Name)].Type; }
        if (target is VectorType v)
            return m.Name.Length == 1 ? new ScalarType(v.Component) : new VectorType(v.Component, m.Name.Length);
        throw new GlslParseException($"cannot infer member '.{m.Name}'");
    }

    static Scalar BaseScalar(GlslType type) => type switch
    {
        ScalarType s => s.Kind,
        VectorType v => v.Component,
        MatrixType m => m.Component,
        ArrayType a => BaseScalar(a.Element),
        RuntimeArrayType r => BaseScalar(r.Element),
        _ => Scalar.Float,
    };

    // glslang names each user struct type (and its members) when the type is first created.
    uint NameStruct(StructType st, uint id)
    {
        if (_namedStructs.Add(id))
        {
            _m.AddName(id, st.Name);
            for (int i = 0; i < st.Fields.Count; i++) _m.AddMemberName(id, i, st.Fields[i].Name);
        }
        return id;
    }

    GlslType Resolve(string name) =>
        Types.Resolve(name) ?? (_structs.TryGetValue(name, out var s) ? s
            : _bufferRefs.ContainsKey(name) ? new BufferReferenceType(name)
            : throw new GlslParseException($"unknown type '{name}'"));

    // A `layout(buffer_reference) buffer Name { ... };` declares a pointer TYPE, not a variable. Register
    // all names first (so references can be mutually recursive), then resolve the referenced fields.
    void RegisterBufferReferences(TranslationUnit unit)
    {
        var refBlocks = unit.Declarations.OfType<InterfaceBlock>()
            .Where(b => b.Qualifiers.Layout.Any(i => i.Name == "buffer_reference")).ToList();
        foreach (var b in refBlocks) _bufferRefs[b.BlockName] = new StructType(b.BlockName, []);
        foreach (var b in refBlocks)
        {
            var fields = b.Members
                .SelectMany(f => f.Declarators.Select(d => (d.Name, ApplyArrayDims(ResolveTypeRef(f.Type), d.Array))))
                .ToList();
            _bufferRefs[b.BlockName] = new StructType(b.BlockName, fields);

            var quals = new List<Decoration>();
            if (b.Qualifiers.Has("readonly")) quals.Add(Decoration.NonWritable);
            if (b.Qualifiers.Has("writeonly")) quals.Add(Decoration.NonReadable);
            foreach (var (word, decoration) in MemoryQualifiers) if (b.Qualifiers.Has(word)) quals.Add(decoration);
            _bufferRefQuals[b.BlockName] = quals;
            // The Aligned memory operand for a load/store through this reference uses its declared
            // buffer_reference_align, defaulting to 16 (glslang's Type::getBufferReferenceAlignment).
            _bufferRefAlign[b.BlockName] = LayoutInt(b.Qualifiers, "buffer_reference_align") ?? 16;
        }
    }

    readonly Dictionary<string, List<Decoration>> _bufferRefQuals = [];
    readonly Dictionary<string, int> _bufferRefAlign = [];

    // A buffer-reference member is a forward-declared PhysicalStorageBuffer pointer; its pointee struct
    // and the pointer's definition are emitted when the reference is first dereferenced (BufferRefDefine).
    uint BufferRefPtr(string name)
    {
        if (!_bufferRefPtrIds.TryGetValue(name, out uint id))
            id = _bufferRefPtrIds[name] = _m.TypeForwardPointer(StorageClass.PhysicalStorageBuffer);
        return id;
    }

    readonly HashSet<string> _definedBufferRefs = [];
    readonly Dictionary<string, uint> _bufferRefStructIds = [];
    readonly Dictionary<string, uint[]> _bufferRefMemberIds = [];

    // glslang emits a reference type across three sections at different points relative to the block
    // that uses it: the pointee struct+pointer right after the enclosing struct, its names after the
    // enclosing struct's names, and its decorations after the enclosing struct's decorations. So the
    // three phases are separate and driven by BuildBlock; a deref just needs the types to exist.
    // Outside a block (a deref, a local) the reference is a full type use, so its names and decorations
    // follow its types directly.
    void BufferRefDefine(string name)
    {
        foreach (var r in BufferRefTypes(name)) { BufferRefNames(r); BufferRefDecorate(r); }
    }

    // Defines a reference's pointee struct and patches its forward pointer, returning every reference type
    // this defined, in order. Like glslang's deferredForwardPointers, a member referencing a type not yet
    // defined (`A { B b; }`, or a self-reference in progress) gets its struct right after this one, before
    // this pointer is patched.
    List<string> BufferRefTypes(string name)
    {
        var defined = new List<string>();
        if (!_definedBufferRefs.Add(name)) return defined;
        defined.Add(name);
        uint ptrId = BufferRefPtr(name);
        var st = _bufferRefs[name];
        uint[] memberIds = [.. st.Fields.Select(f => BufferRefMemberType(f.Type))];
        uint structId = _m.TypeStructUnique(memberIds);
        _bufferRefStructIds[name] = structId;
        _bufferRefMemberIds[name] = memberIds;
        foreach (var f in st.Fields)
            if (ReferenceIn(f.Type) is string inner) defined.AddRange(BufferRefTypes(inner));
        _m.TypePointerForward(ptrId, StorageClass.PhysicalStorageBuffer, structId);
        return defined;
    }

    static string? ReferenceIn(GlslType t) => t switch
    {
        BufferReferenceType r => r.Name,
        ArrayType a => ReferenceIn(a.Element),
        RuntimeArrayType r => ReferenceIn(r.Element),
        _ => null,
    };

    // Builds a member type without emitting its array-stride decoration; the stride is deferred to the
    // decorate phase so it lands after the enclosing block's decorations, as glslang orders it.
    uint BufferRefMemberType(GlslType t) => t switch
    {
        RuntimeArrayType r => _m.TypeRuntimeArrayUnique(BufferRefMemberType(r.Element)),
        ArrayType a => _m.TypeArrayUnique(BufferRefMemberType(a.Element), _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)a.Length)),
        _ => SpvType(BlockStored(t)),
    };

    void BufferRefNames(string name) => NameStruct(_bufferRefs[name], _bufferRefStructIds[name]);

    void BufferRefDecorate(string name)
    {
        var st = _bufferRefs[name];
        uint[] memberIds = _bufferRefMemberIds[name];
        for (int i = 0; i < st.Fields.Count; i++)
        {
            var element = st.Fields[i].Type switch { RuntimeArrayType r => r.Element, ArrayType a => a.Element, _ => null };
            if (element is not null) _m.Decorate(memberIds[i], Decoration.ArrayStride, (uint)Stride(element, BlockLayout.Std430));
        }
        DecorateLayout(st, _bufferRefStructIds[name], memberIds, BlockLayout.Std430, _bufferRefQuals[name]);
        _m.Decorate(_bufferRefStructIds[name], Decoration.Block);
    }

    // Resolves a type reference, applying any prefix array dimensions (`float[4] x` == `float x[4]`).
    GlslType ResolveTypeRef(TypeRef t) => t.Name == "coopmat" && t.TemplateArgs is { Count: 5 } targ
        ? new CoopMatType(BaseScalar(Resolve(((Ident)targ[0]).Name)), CoopMatParam(targ[1]), CoopMatParam(targ[2]), CoopMatParam(targ[3]), CoopMatParam(targ[4]))
        : ApplyArrayDims(Resolve(t.Name), t.Array ?? []);

    // A cooperative-matrix template argument (scope/rows/cols/use) is a compile-time unsigned integer.
    uint CoopMatParam(Expr e) => EvalConst(e) is CScalar s ? (uint)s.I
        : throw new GlslParseException("a coopmat template argument must be a constant integer");

    static bool InLayout(TranslationUnit unit, string name) => unit.Declarations.Any(d =>
    {
        // A bare `layout(...) in;` or a builtin redeclaration like `layout(pixel_center_integer) in vec4 gl_FragCoord;`.
        var q = d switch { LayoutDecl l => l.Qualifiers, GlobalVar g => g.Qualifiers, _ => (Qualifiers?)null };
        return q is not null && q.Has("in") && q.Layout.Any(i => i.Name == name);
    });

    static bool IsRayTracing(ShaderStage s) => s is ShaderStage.RayGen or ShaderStage.Intersection
        or ShaderStage.AnyHit or ShaderStage.ClosestHit or ShaderStage.Miss or ShaderStage.Callable;

    uint RayQuerySpvType()
    {
        _m.AddCapability(Capability.RayQueryKHR);
        _m.AddExtension("SPV_KHR_ray_query");
        return _m.TypeRayQuery();
    }

    // The acceleration-structure type brings its own capability when the stage does not already provide
    // one: a ray stage carries RayTracingKHR/NV, which covers the type (glslang adds nothing extra there,
    // and the 1.4 ray corpus passes byte-parity without RayQueryKHR); outside ray stages glslang adds
    // RayQueryKHR + SPV_KHR_ray_query (oracle-verified: an accelerationStructureEXT uniform in a compute
    // shader at SPIR-V 1.0 emits both). Without it the module is invalid ("TypeAccelerationStructureKHR
    // requires one of these capabilities"). Capabilities/extensions are serialized sorted, so adding
    // them here cannot reorder an already-parity-passing module.
    uint AccelStructSpvType()
    {
        if (!IsRayTracing(stage))
        {
            _m.AddCapability(Capability.RayQueryKHR);
            _m.AddExtension("SPV_KHR_ray_query");
        }
        return _m.TypeAccelerationStructure();
    }

    static StorageClass? RayTracingStorage(Qualifiers q) =>
        q.Has("rayPayloadEXT") || q.Has("rayPayloadNV") ? StorageClass.RayPayloadKHR :
        q.Has("rayPayloadInEXT") || q.Has("rayPayloadInNV") ? StorageClass.IncomingRayPayloadKHR :
        q.Has("hitAttributeEXT") || q.Has("hitAttributeNV") ? StorageClass.HitAttributeKHR :
        q.Has("callableDataEXT") || q.Has("callableDataNV") ? StorageClass.CallableDataKHR :
        q.Has("callableDataInEXT") || q.Has("callableDataInNV") ? StorageClass.IncomingCallableDataKHR :
        null;

    ExecutionModel ExecutionModelOf(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => ExecutionModel.Vertex,
        ShaderStage.Fragment => ExecutionModel.Fragment,
        ShaderStage.Compute => ExecutionModel.GLCompute,
        ShaderStage.Geometry => ExecutionModel.Geometry,
        ShaderStage.TessControl => ExecutionModel.TessellationControl,
        ShaderStage.TessEvaluation => ExecutionModel.TessellationEvaluation,
        ShaderStage.Mesh => _nvMesh ? ExecutionModel.MeshNV : ExecutionModel.MeshEXT,
        ShaderStage.Task => ExecutionModel.TaskEXT,
        ShaderStage.RayGen => ExecutionModel.RayGenerationKHR,
        ShaderStage.Intersection => ExecutionModel.IntersectionKHR,
        ShaderStage.AnyHit => ExecutionModel.AnyHitKHR,
        ShaderStage.ClosestHit => ExecutionModel.ClosestHitKHR,
        ShaderStage.Miss => ExecutionModel.MissKHR,
        ShaderStage.Callable => ExecutionModel.CallableKHR,
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    uint DecorateSpecId(uint id, int specId) { _m.Decorate(id, Decoration.SpecId, (uint)specId); return id; }

    // gl_WorkGroupSize: a uvec3 of the local sizes decorated BuiltIn WorkgroupSize. A dimension fixed
    // with local_size_N_id is its own OpSpecConstant (SpecId from the layout); if any dimension is
    // specializable the whole uvec3 is a spec composite, otherwise an ordinary constant composite.
    // Built on first use (a body may read gl_WorkGroupSize) and emitted even when never read.
    Value WorkGroupSizeConstant()
    {
        bool anySpec = _localSize.Any(d => d.SpecId is not null);
        if (_wgSizeConst == 0)
        {
            uint u32 = _m.TypeInt(32, signed: false);
            // Ordering follows glslang: a specializable workgroup size materializes its components first
            // (the local_size_N_id spec constants are declared entities, made before the composite's type),
            // while an all-constant one builds the composite type first.
            uint vec3 = 0;
            if (!anySpec) vec3 = _m.TypeVector(u32, 3);
            uint[] comps = [.. _localSize.Select(d => d.SpecId is int sid
                ? DecorateSpecId(_m.SpecConstantInt(u32, d.Value), sid)
                : _m.ConstantInt(u32, d.Value))];
            if (anySpec) vec3 = _m.TypeVector(u32, 3);
            _wgSizeConst = anySpec ? _m.SpecConstantComposite(vec3, comps) : _m.ConstantComposite(vec3, comps);
            _m.Decorate(_wgSizeConst, Decoration.BuiltIn, (uint)BuiltIn.WorkgroupSize);
        }
        // A specializable size is not a compile-time constant, so it must never fold into another constant.
        return new Value(_wgSizeConst, UVec3, IsConstant: !anySpec);
    }

    // The three local-size dimensions, each with its default value and (if fixed with local_size_N_id)
    // the specialization-constant id that drives it. An explicit local_size_N sets the value; a
    // local_size_N_id makes the dimension specializable (its default stays whatever local_size_N gave, or 1).
    static (uint Value, int? SpecId)[] LocalSizeDims(TranslationUnit unit)
    {
        uint[] value = [1, 1, 1];
        int?[] specId = [null, null, null];
        int Axis(string name, string prefix) => name[prefix.Length] switch { 'x' => 0, 'y' => 1, 'z' => 2, _ => -1 };
        foreach (var decl in unit.Declarations.OfType<LayoutDecl>())
            foreach (var item in decl.Qualifiers.Layout)
            {
                if (item.Name.StartsWith("local_size_", StringComparison.Ordinal) && item.Name.EndsWith("_id", StringComparison.Ordinal)
                    && item.Value is Literal idLit && Axis(item.Name, "local_size_") is >= 0 and var ai)
                    specId[ai] = LayoutSpecId(idLit, item.Name);
                else if (item.Name is "local_size_x" or "local_size_y" or "local_size_z" && item.Value is Literal lit)
                {
                    uint size = LayoutSizeValue(lit, item.Name);
                    if (size == 0) throw new GlslParseException($"{item.Name} must be at least 1", lit.Line);
                    value[Axis(item.Name, "local_size_")] = size;
                }
            }
        return [(value[0], specId[0]), (value[1], specId[1]), (value[2], specId[2])];
    }

    // The local_size_* values are parsed here rather than by the shared layout-integer path, so a
    // malformed or overflowing literal must reject cleanly (glslang: "'layout-id value' : scalar
    // integer expression required") instead of escaping as a raw Format/OverflowException from
    // int/uint.Parse - the only layout path that was unguarded.
    static int LayoutSpecId(Literal lit, string qualifier)
    {
        try { return int.Parse(lit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture); }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new GlslParseException($"layout qualifier '{qualifier}' requires an integer value, but got '{Clip(lit.Text)}'", lit.Line);
        }
    }

    static uint LayoutSizeValue(Literal lit, string qualifier)
    {
        try { return uint.Parse(lit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture); }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new GlslParseException($"layout qualifier '{qualifier}' requires an integer value, but got '{Clip(lit.Text)}'", lit.Line);
        }
    }

    // ---- std140 layout ----

    readonly HashSet<uint> _decorated = [];   // guards against decorating a shared type twice

    // Applies std140 (uniform) or std430 (buffer) member offsets and array/matrix strides.
    // The two rulesets differ only in whether array/struct alignment is rounded up to 16.
    // A block member's type carries the block's layout (ArrayStride, and a nested struct's Offsets), so it
    // is a DIFFERENT SPIR-V type from the same GLSL type used outside a block - glslang creates a separate,
    // never-de-duplicated type for it and for every aggregate nested inside it. The nested layout is
    // decorated here as the types are created, which is innermost-first: the order glslang emits.
    uint BlockFieldType(GlslType t, BlockLayout layout, bool rowMajor = false)
    {
        switch (t)
        {
            case RuntimeArrayType r:
            {
                uint id = _m.TypeRuntimeArrayUnique(BlockFieldType(r.Element, layout, rowMajor));
                _m.Decorate(id, Decoration.ArrayStride, (uint)Stride(r.Element, layout));
                return id;
            }
            case ArrayType a:
            {
                uint element = BlockFieldType(a.Element, layout, rowMajor);
                uint id = _m.TypeArrayUnique(element, _m.ConstantInt(_m.TypeInt(32, signed: false), (uint)a.Length));
                _m.Decorate(id, Decoration.ArrayStride, (uint)Stride(a.Element, layout));
                _layoutArrayIds[(BlockStored(a.Element), a.Length, layout)] = id;   // for a whole-array read to find it
                return id;
            }
            // A nested struct gets its own type carrying this block's Offsets, distinct from the same
            // GLSL struct used outside a block. glslang caches one per layout, so a second block with
            // the same ruleset shares it.
            case StructType st:
            {
                // The chain and the loaded value carry the bool-as-uint form of the struct, so key by
                // that (BlockStored is idempotent) and SpvType resolves it back to this block copy.
                if (_layoutStructIds.TryGetValue((st, layout, rowMajor), out var cached)) return cached;
                uint[] memberIds = [.. st.Fields.Select(f => BlockFieldType(f.Type, layout, rowMajor))];
                uint id = NameStruct(st, _m.TypeStructUnique(memberIds));
                _layoutStructIds[(st, layout, rowMajor)] = id;
                DecorateLayout(st, id, memberIds, layout, rowMajorAll: rowMajor);
                return id;
            }
            default: return SpvType(BlockStored(t));
        }
    }

    // A block's ruleset: an explicit layout qualifier wins, else uniform defaults to std140 and
    // buffer/push_constant to std430. Scalar layout is GL_EXT_scalar_block_layout.
    static BlockLayout BlockRulesetOf(Qualifiers q, bool packed)
    {
        foreach (var item in q.Layout)
            switch (item.Name)
            {
                case "scalar": return BlockLayout.Scalar;
                case "std430": return BlockLayout.Std430;
                case "std140": return BlockLayout.Std140;
            }
        return packed ? BlockLayout.Std430 : BlockLayout.Std140;
    }

    // The block ruleset a type is laid out under, or null when it is not an interface block (so an
    // in/out block, which has no explicit layout, stays in the plain universe).
    BlockLayout? BlockRuleset(GlslType t) => t switch
    {
        StructType st => _blockLayout.TryGetValue(st, out var l) ? l : null,
        ArrayType a => BlockRuleset(a.Element),   // a block declared as an instance array
        _ => null,
    };

    // A bool has no defined memory layout, so a block stores its bool members as uint (matching glslang)
    // and converts on access. Maps a member's declared type to the type actually stored in the block.
    static GlslType BlockStored(GlslType t) => t switch
    {
        ScalarType { Kind: Scalar.Bool } => Types.UInt,
        VectorType { Component: Scalar.Bool } v => new VectorType(Scalar.UInt, v.Size),
        ArrayType a when ContainsBool(a.Element) => new ArrayType(BlockStored(a.Element), a.Length),
        _ => t,
    };

    // Converts a value just loaded from a block back to its declared type (a stored uint back to bool).
    Value FromStored(Value loaded, GlslType declared) => loaded.Type.Equals(declared) ? loaded : declared switch
    {
        ScalarType { Kind: Scalar.Bool } => ConvertScalar(Scalar.Bool, loaded),
        VectorType { Component: Scalar.Bool } bv => BoolVectorFrom(bv, (VectorType)loaded.Type, loaded),
        _ => loaded,
    };

    // The inverse, for writing INTO a block: a bool goes back to the uint the block actually stores.
    Value ToStored(Value value, GlslType stored) => value.Type.Equals(stored) ? value : stored switch
    {
        ScalarType { Kind: Scalar.UInt } when value.Type is ScalarType { Kind: Scalar.Bool } => ConvertScalar(Scalar.UInt, value),
        VectorType { Component: Scalar.UInt } uv when value.Type is VectorType { Component: Scalar.Bool } =>
            SelectFromBool(uv, value),
        _ => value,
    };

    // The memory qualifiers a declaration carries, in the order glslang emits them.
    IReadOnlyList<Decoration> MemoryDecorationsOf(Qualifiers q)
    {
        var list = new List<Decoration>();
        if (q.Has("readonly")) list.Add(Decoration.NonWritable);
        if (q.Has("writeonly")) list.Add(Decoration.NonReadable);
        foreach (var (word, decoration) in MemoryQualifiers)
            if (q.Has(word) && !(_vmmPragma && decoration is Decoration.Coherent or Decoration.Volatile))
                list.Add(decoration);
        return list;
    }

    void DecorateLayout(StructType s, uint structTypeId, uint[] memberIds, BlockLayout layout,
        IReadOnlyList<Decoration>? memberQualifiers = null, IReadOnlyList<int?>? explicitOffsets = null,
        IReadOnlyList<bool>? rowMajor = null, IReadOnlyList<int?>? aligns = null,
        IReadOnlyList<IReadOnlyList<Decoration>>? perMemberQualifiers = null, bool rowMajorAll = false)
    {
        if (!_decorated.Add(structTypeId)) return;
        // The member types' own layout (array strides, nested-struct offsets) is already decorated by
        // BlockFieldType as it built them, which is what puts it before this struct's decorations.
        int offset = 0;
        for (int i = 0; i < s.Fields.Count; i++)
        {
            var type = s.Fields[i].Type;
            // layout(offset=N) pins a member; layout(align=N) rounds up to a larger alignment; else pack
            // at natural alignment.
            offset = explicitOffsets?[i] ?? RoundUp(offset, Math.Max(Align(type, layout), aligns?[i] ?? 0));
            // glslang's per-member order: Col/RowMajor (matrix), then memory qualifiers (NonWritable/...),
            // then Offset, then MatrixStride (matrix).
            var matrix = MatrixOf(type) as MatrixType;   // also non-null for arrays of matrices
            if (matrix is not null)
                _m.MemberDecorate(structTypeId, i, rowMajor?[i] ?? rowMajorAll ? Decoration.RowMajor : Decoration.ColMajor);
            if (memberQualifiers is not null)
                foreach (var d in memberQualifiers) _m.MemberDecorate(structTypeId, i, d);
            if (perMemberQualifiers is not null)
                foreach (var d in perMemberQualifiers[i]) if (memberQualifiers?.Contains(d) != true) _m.MemberDecorate(structTypeId, i, d);
            _m.MemberDecorate(structTypeId, i, Decoration.Offset, (uint)offset);
            if (matrix is not null) _m.MemberDecorate(structTypeId, i, Decoration.MatrixStride, (uint)MatrixStride(matrix, layout));
            offset += Size(type, layout);
        }
    }

    // A scalar's size in bytes: the 64-bit kinds are 8, a half is 2, everything else 4.
    static int ScalarSize(Scalar s) => s switch
    {
        Scalar.Double or Scalar.Int64 or Scalar.UInt64 => 8,
        Scalar.Half or Scalar.Int16 or Scalar.UInt16 => 2,
        Scalar.Int8 or Scalar.UInt8 => 1,
        _ => 4,
    };

    // A member's base alignment. std140 pads every aggregate (and every matrix column) up to 16; std430
    // keeps the natural alignment; scalar layout aligns everything to just its component scalar.
    static int Align(GlslType t, BlockLayout layout) => t switch
    {
        ScalarType s => ScalarSize(s.Kind),
        VectorType v => layout == BlockLayout.Scalar ? ScalarSize(v.Component) : (v.Size == 2 ? 2 : 4) * ScalarSize(v.Component),
        MatrixType m => ColumnAlign(m, layout),
        ArrayType a => AggregateAlign(Align(a.Element, layout), layout),
        RuntimeArrayType r => AggregateAlign(Align(r.Element, layout), layout),
        StructType s => AggregateAlign(s.Fields.Max(f => Align(f.Type, layout)), layout),
        BufferReferenceType => 8,
        _ => 16,
    };

    static int AggregateAlign(int natural, BlockLayout layout) => layout == BlockLayout.Std140 ? RoundUp(natural, 16) : natural;

    static int ColumnAlign(MatrixType m, BlockLayout layout) => AggregateAlign(Align(ColumnOf(m), layout), layout);

    static VectorType ColumnOf(MatrixType m) => new(m.Component, m.Rows);

    static int Size(GlslType t, BlockLayout layout) => t switch
    {
        ScalarType s => ScalarSize(s.Kind),
        VectorType v => v.Size * ScalarSize(v.Component),   // vec3 occupies 12 bytes; a following scalar packs at offset 12
        MatrixType m => m.Columns * MatrixStride(m, layout),
        ArrayType a => Stride(a.Element, layout) * a.Length,
        RuntimeArrayType => 0,   // always the last member; contributes no fixed size
        StructType s => StructSize(s, layout),
        BufferReferenceType => 8,
        _ => 16,
    };

    // Array stride: the element size rounded up to the element's *array* base alignment. In std140
    // that alignment is padded up to 16 (so `float[]` has stride 16, not 4); the others pack tightly.
    static int Stride(GlslType element, BlockLayout layout) =>
        RoundUp(Size(element, layout), AggregateAlign(Align(element, layout), layout));

    static int StructSize(StructType s, BlockLayout layout)
    {
        int offset = 0;
        foreach (var f in s.Fields) { offset = RoundUp(offset, Align(f.Type, layout)); offset += Size(f.Type, layout); }
        return RoundUp(offset, Align(s, layout));
    }

    // The byte offset of a struct member under the given layout.
    static int FieldOffset(StructType s, int index, BlockLayout layout)
    {
        int offset = 0;
        for (int i = 0; i < index; i++) { offset = RoundUp(offset, Align(s.Fields[i].Type, layout)); offset += Size(s.Fields[i].Type, layout); }
        return RoundUp(offset, Align(s.Fields[index].Type, layout));
    }

    // The matrix at the core of a (possibly array-wrapped) member type, or null if there is none.
    static MatrixType? MatrixOf(GlslType t) => t switch
    {
        MatrixType m => m,
        ArrayType a => MatrixOf(a.Element),
        RuntimeArrayType r => MatrixOf(r.Element),
        _ => null,
    };

    // std140 always pads a matrix column to 16; std430 keeps the natural column-vector alignment,
    // so a mat with 2-component columns has an 8-byte stride.
    static int MatrixStride(MatrixType m, BlockLayout layout) => RoundUp(Size(ColumnOf(m), layout), ColumnAlign(m, layout));

    static int RoundUp(int value, int align) => (value + align - 1) / align * align;

    static bool ContainsBool(GlslType t) => t switch
    {
        ScalarType s => s.Kind == Scalar.Bool,
        VectorType v => v.Component == Scalar.Bool,
        ArrayType a => ContainsBool(a.Element),
        RuntimeArrayType r => ContainsBool(r.Element),
        StructType st => st.Fields.Any(f => ContainsBool(f.Type)),
        _ => false,
    };

    // Whether a member type (recursively) contains a scalar of the given bit width (8 or 16).
    static bool ContainsWidth(GlslType t, int bits) => t switch
    {
        ScalarType s => ScalarSize(s.Kind) * 8 == bits && s.Kind != Scalar.Bool,
        VectorType v => ScalarSize(v.Component) * 8 == bits,
        MatrixType m => ScalarSize(m.Component) * 8 == bits,
        ArrayType a => ContainsWidth(a.Element, bits),
        RuntimeArrayType r => ContainsWidth(r.Element, bits),
        StructType st => st.Fields.Any(f => ContainsWidth(f.Type, bits)),
        _ => false,
    };

    static int? LayoutInt(Qualifiers q, string name)
    {
        foreach (var item in q.Layout)
            if (item.Name == name && item.Value is Literal lit)
                return int.TryParse(lit.Text, out int v) ? v : throw new GlslParseException($"layout qualifier '{name}' requires an integer value, but got '{lit.Text}'");
        return null;
    }

    // A malformed or out-of-range literal is a source error, so every parse failure below is reported as a
    // GlslParseException rather than escaping as a Format/Overflow exception the caller cannot anticipate.
    static readonly char[] _floatSuffix = ['f', 'F', 'l', 'L', 'h', 'H'];
    static float ParseFloat(string text) =>
        float.TryParse(text.TrimEnd(_floatSuffix), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            ? v : throw new GlslParseException($"invalid floating-point literal '{Clip(text)}'");

    // A double literal keeps full 64-bit precision, so it is parsed as a double rather than via ParseFloat.
    static double ParseDouble(string text) =>
        double.TryParse(text.TrimEnd(_floatSuffix), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : throw new GlslParseException($"invalid floating-point literal '{Clip(text)}'");

    // Diagnostics must not embed unbounded token text: a 100,000-digit literal would produce a
    // 100k-char error message (which also deadlocks naive hosts draining stderr pipes). Glslang
    // shows a short prefix; 32 characters keeps the message identifiable without the megabyte.
    internal static string Clip(string s) => s.Length <= 32 ? s : s[..32] + "...";

    // Parses a GLSL integer literal (decimal, 0x hex, or 0 octal) to its 32-bit two's-complement bits.
    // A literal that does not fit in 32 bits at all is a source error at every 32-bit use (glslang:
    // "numeric literal too big"); values in the 2^31..2^32-1 range are accepted and keep their bit
    // pattern, because glslang silently narrows (`int a = 2147483648` is -2147483648, `-2147483648`
    // still folds to INT_MIN, and hex `0x80000000` reads as its bit pattern).
    static uint ParseIntLiteral(string text)
    {
        ulong v = ParseInt64Literal(text);
        if (v > uint.MaxValue) throw new GlslParseException("numeric literal too big");
        return unchecked((uint)v);
    }

    static readonly char[] _intSuffix = ['u', 'U', 'l', 'L'];
    /// <summary>Parses a GLSL integer literal (decimal, 0x hex, or 0 octal) to its 64-bit value.</summary>
    static ulong ParseInt64Literal(string text)
    {
        string t = text.TrimEnd(_intSuffix);
        try
        {
            return
                t.StartsWith("0x") || t.StartsWith("0X") ? Convert.ToUInt64(t[2..], 16) :
                t.Length > 1 && t[0] == '0' ? Convert.ToUInt64(t, 8) :
                ulong.Parse(t, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is OverflowException)
        {
            // glslang's diagnostic for an integer literal beyond 32 bits - a value that cannot even
            // fit the 64-bit parse certainly exceeds it.
            throw new GlslParseException("numeric literal too big");
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            throw new GlslParseException($"invalid integer literal '{Clip(text)}'");
        }
    }
}
