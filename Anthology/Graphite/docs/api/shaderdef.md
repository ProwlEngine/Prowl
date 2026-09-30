# ShaderDef

Reference for the ShaderDef shader file format (`.shader`), the parser and compiler API, and the SlangQuickCompile tool.

- [Overview](#overview)
- [Quick example](#quick-example)
- [File format](#file-format)
  - [Structure](#structure)
  - [Comments](#comments)
  - [Shader](#shader)
  - [Properties](#properties)
  - [Pass](#pass)
  - [Name](#name)
  - [Tags](#tags)
  - [Render state commands](#render-state-commands)
  - [Stencil](#stencil)
  - [SLANGPROGRAM](#slangprogram)
  - [Fallback](#fallback)
  - [Variant axes (in Slang)](#variant-axes-in-slang)
  - [Parse errors](#parse-errors)
- [Object model](#object-model)
- [Compiler API](#compiler-api)
- [SlangQuickCompile](#slangquickcompile)
- [Common patterns](#common-patterns)
- [Pitfalls](#pitfalls)
- [See also](#see-also)

## Overview

A ShaderDef file wraps a Slang shader in a small declarative markup: a name, optional material properties, and one or more passes, each with fixed-function state and an embedded Slang program. The markup is deliberately thin. Everything between `SLANGPROGRAM` and `ENDSLANG` is handed to Slang untouched, and shader variants come from the Slang source rather than from the markup.

`ShaderParser.Parse` turns text into a `ShaderDefinition`. `ShaderDefinition.Create` binds it to a device and a compiler. After that you pick keywords on a `ShaderPass` and call `cmd.SetShader(pass)`.

[ShaderSpec.md](../../ShaderDef/Core/ShaderSpec.md) describes the same format.

## Quick example

[Bloom.shader](../../Samples/PBRRenderer/Shaders/Bloom.shader) from the PBR sample, trimmed to its structure (the `static if` branches and sample math are cut for brevity):

```
Shader "PBRRenderer/Bloom"
{
    Pass
    {
        Name "Bloom"

        Cull Off
        ZTest Disabled

        SLANGPROGRAM
        import VariantAttributes;
        import UVOrigin;

        [VariantAxis]
        extern static const bool Upsample;

        struct BloomData
        {
            Sampler2D<float4> sourceTexture;
            float2 halfPixel;
            float offset;
        };
        ParameterBlock<BloomData> BloomParams;

        [shader("vertex")]
        float4 Vertex(uint vertexIndex : SV_VertexID, out float2 UV : UV0) : SV_Position
        {
            float2 baseUV = float2(float((vertexIndex << 1) & 2), float(vertexIndex & 2));

            static if (IsUVOriginTopLeft)
                UV = float2(baseUV.x, 1.0 - baseUV.y);
            else
                UV = float2(baseUV.x, baseUV.y);

            return float4(baseUV * 2.0 - 1.0, 0.0, 1.0);
        }
        ENDSLANG
    }
}
```

Loading and using it from C#:

```csharp
SlangShaderCompiler compiler = new();
compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
compiler.BeginSession(FileLoader.SearchDirectories, FileLoader.Load);

ShaderDefinition def = ShaderParser.Parse(source);
def.Create(device, compiler, new Variant(), CompileMode.All);
compiler.EndSession();

ShaderPass pass = def.Passes![0];
pass.SetKeyword(new Keyword("Upsample", "true"));
cmd.SetShader(pass);
```

## File format

### Structure

```
Shader "Name"
{
    Properties { ... }
    Pass [index] { ... }
    Pass [index] { ... }
    Fallback "Name"
}
```

| Element | Required | Notes |
|---|---|---|
| `Shader "Name"` | Yes | Name must not be empty or whitespace. |
| `Properties` | No | At most one, before the first pass. |
| `Pass` | Yes, one or more | Named passes must have unique names. |
| `Fallback` | No | After the last pass. Stored as a string; see [Fallback](#fallback). |

The elements must appear in this order. Anything after the closing brace of `Shader` is an error.

### Comments

`// line` and `/* block */` comments are stripped by the tokenizer everywhere outside a `SLANGPROGRAM` block. Inside the block they are passed to Slang as part of the source.

```
Offset -1 -1   /* block comment */
```

Structural words (`Shader`, `Properties`, `Pass`, `Name`, `Tags`, `Fallback`) match case-insensitively. Render-state command names, enum values, property types and the `SLANGPROGRAM`/`ENDSLANG` markers are case-sensitive.

### Shader

```
Shader "Example/Standard"
{
    ...
}
```

The name is stored in `ShaderDefinition.Name`. It is only a label; the library does not look shaders up by name.

### Properties

```
Properties
{
    PropertyName("Display Name", Type) = DefaultValue
}
```

The default is optional; without it the value is zero (or an empty texture name). Property names must be unique.

| Type | Default syntax | Stored in |
|---|---|---|
| `Float` | `= 0.25` | `Value.X` |
| `Integer` | `= 7` | `Value.X`, as a float |
| `Vector` | `= (1, 2, 3, 4)` | `Value` |
| `Color` | `= (1, 0.5, 0.25, 1)` | `Value` |
| `Matrix` | `= ((1,0,0,0)(0,1,0,0)(0,0,1,0)(0,0,0,1))` | `MatrixValue` |
| `Texture2D`, `Texture2DArray`, `Texture3D`, `TextureCubemap`, `TextureCubemapArray` | `= "white" {}` | `TextureValue` (the quoted name) |

Vectors and colors take exactly four comma-separated numbers. A matrix is four vectors in a row with no commas between them. Numbers may have a leading `-`. A default whose shape does not match the type is an error, for example `_R("Rough", Float) = (1, 2, 3, 4)` reports `Float property expects a scalar number, but found '('`.

Real example, from [Unlit.shader](../../Samples/PBRRenderer/Shaders/Unlit.shader):

```
Properties
{
    _AlbedoTex ("Albedo", Texture2D) = "white" {}
    _BaseColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
}
```

Properties are parsed into `ShaderDefinition.Properties` (`ShaderProperty` with `Name`, `DisplayName`, `PropertyType`, `Value`, `MatrixValue`, `TextureValue`). Graphite does not interpret them.

### Pass

```
Pass
{
    ...
}

Pass 0
{
    ...
}
```

The optional integer after `Pass` is parsed and discarded. Passes are found by position in `ShaderDefinition.Passes`, by `Name` or by tags.

Order inside a pass is fixed: `Name`, then `Tags`, then render-state commands (any order), then the `SLANGPROGRAM` block.

### Name

```
Name "ForwardLit"
```

Optional. Used by `ShaderDefinition.GetPass(name)` and `GetPassIndex(name)`, and as the Slang module name for the pass. Unnamed passes are given `pass_N` for the Slang module name and can be duplicated freely.

### Tags

```
Tags { "LightMode" = "ForwardBase" "Queue" = "Transparent" }
```

Optional. Key/value string pairs, no separators between them. Duplicate keys in one pass are an error. Tags are stored in `ShaderPass.Tags` (`null` when no `Tags` block was written, an empty dictionary for `Tags { }`). The library does not interpret them; render pipelines use them to pick passes with `ShaderDefinition.GetPassWithTag(tag, value)`, `GetPassesWithTag` and `PassHasTag`.

### Render state commands

Zero or more, any order, one per line by convention. Any command left out is left unset (`null`) in `PassState` and inherits from the base state given at bind time. If the same command appears twice, the earlier one wins.

| Command | Syntax | Effect on `PassState` |
|---|---|---|
| `Cull` | `Cull Back`, `Cull Front`, `Cull Off` | `CullMode` (`Off` is `FaceCullMode.None`) |
| `ZTest` | `ZTest Disabled` or `ZTest <Comparison>` | `Disabled` sets `EnableDepthTest = false`. Anything else sets it true and sets `DepthFunc`. |
| `ZWrite` | `ZWrite On`, `ZWrite Off` | `DepthWriteMask` |
| `ZClip` | `ZClip On`, `ZClip Off` | `EnableDepthClamp`, inverted: `ZClip Off` enables clamping. |
| `Blend` | `Blend <SrcFactor> <DstFactor>` | Enables blending; sets RGB and alpha factors to the same pair. |
| `BlendRGB` | `BlendRGB <Src> <Dst>` | Enables blending; RGB factors only. |
| `BlendAlpha` | `BlendAlpha <Src> <Dst>` | Enables blending; alpha factors only. |
| `BlendOp` | `BlendOp <Function>` | Sets the RGB and alpha blend function. |
| `ColorMask` | `ColorMask RGBA` | Any combination of `R`, `G`, `B`, `A` written as one word. Other characters are an error. |
| `Offset` | `Offset <factor> <units>` | Depth bias. Factor is the slope factor, units the constant factor. Whitespace separated, no comma. |
| `AlphaToMask` | `AlphaToMask On`, `AlphaToMask Off` | `AlphaToMask` |
| `Stencil` | `Stencil { ... }` | See [Stencil](#stencil). |

Value sets (names are the enum member names, exactly):

| Kind | Values |
|---|---|
| Comparison (`ZTest`, stencil `Comp*`) | `Never`, `Less`, `Equal`, `LessEqual`, `Greater`, `NotEqual`, `GreaterEqual`, `Always` |
| Blend factor | `Zero`, `One`, `SourceAlpha`, `InverseSourceAlpha`, `DestinationAlpha`, `InverseDestinationAlpha`, `SourceColor`, `InverseSourceColor`, `DestinationColor`, `InverseDestinationColor`, `BlendFactor`, `InverseBlendFactor` |
| Blend function | `Add`, `Subtract`, `ReverseSubtract`, `Minimum`, `Maximum` |
| Stencil operation | `Keep`, `Zero`, `Replace`, `IncrementAndClamp`, `DecrementAndClamp`, `Invert`, `IncrementAndWrap`, `DecrementAndWrap` |

Examples from the tests and samples:

```
Cull Off
ZTest Disabled
```

```
Cull Front
ZWrite Off
ZTest Greater
```

```
Blend SourceAlpha InverseSourceAlpha
```

```
BlendRGB One Zero
```

```
ColorMask RGB
```

```
Offset -1 -2
```

Common blend presets:

| Intent | Command |
|---|---|
| Alpha blending | `Blend SourceAlpha InverseSourceAlpha` |
| Additive | `Blend One One` |
| Premultiplied alpha | `Blend One InverseSourceAlpha` |
| Multiply | `Blend DestinationColor Zero` |

A pass state overlays onto whatever base blend, depth and rasterizer state the caller provides; `Blend` only touches the first color attachment ([PassState.ToBlendState](../../ShaderDef/Core/PassState.cs)).

### Stencil

```
Stencil
{
    Ref 2
    ReadMask 15
    WriteMask 7
    Comp Equal
    Pass Keep
    Fail Keep
    ZFail Invert
}
```

All commands are optional and may appear in any order. Commands without a `Front` or `Back` suffix set both faces.

| Command | Argument | Notes |
|---|---|---|
| `Ref` | integer | Reference value. |
| `ReadMask` | integer | |
| `WriteMask` | integer | |
| `Comp`, `CompFront`, `CompBack` | comparison | Stencil compare function. |
| `Pass`, `PassFront`, `PassBack` | stencil operation | Stencil and depth passed. |
| `Fail`, `FailFront`, `FailBack` | stencil operation | Stencil test failed. |
| `ZFail`, `ZFailFront`, `ZFailBack` | stencil operation | Stencil passed, depth failed. |

A `Stencil` block turns the stencil test on for the pass. Without one, whether the test runs comes from the base `DepthStencilStateDescription` you overlay onto.

Real examples, from `StencilTests`:

```
Stencil { Ref 2 Comp Equal Pass Keep }
```

```
Stencil
{
    PassFront Replace
    PassBack Keep
    FailFront Zero
    FailBack Invert
    ZFailFront IncrementAndClamp
    ZFailBack DecrementAndClamp
}
```

A mistyped command inside the block, such as `Reff 3`, reports `Unknown command 'Reff'`.

### SLANGPROGRAM

```
SLANGPROGRAM
    ...Slang source...
ENDSLANG
```

Required, exactly one per pass, after the render-state commands. The text between the markers is stored verbatim (trimmed) in `ShaderPass.InlineSlang`. The parser does not tokenize it, so braces, comments and even ShaderDef keywords inside are fine:

```
SLANGPROGRAM
struct V { float4 Pass; }
// Cull Back ZTest Always
ENDSLANG
```

Entry points and stages are not declared in ShaderDef. Slang finds them from `[shader("vertex")]`, `[shader("fragment")]` and similar attributes. If the pass defines more than one entry point for the same stage, only the first is used. If it defines none, compilation throws `Shader pass contains no entrypoints.`

Two helper modules are always available to `import`:

| Module | Purpose |
|---|---|
| `VariantAttributes` | Defines the `[VariantAxis]` attribute. |
| `UVOrigin` | Declares `IsUVOriginTopLeft`, a `bool` constant that is `true` for Vulkan and `false` otherwise. It is used in `static if` to flip UVs. |

Other `import`s resolve through the search paths and file provider given to `BeginSession`.

### Fallback

```
Fallback "Hidden/Fallback"
```

Optional. Stored as a string in `ShaderDefinition.Fallback` (empty if absent). The library does not resolve it. This is unrelated to the `Variant fallback` argument of `ShaderDefinition.Create`, which is a compiled variant used when a compile fails.

### Variant axes (in Slang)

Variant axes are declared in the Slang source, not in markup. Mark an `extern` constant with `[VariantAxis]`:

```slang
import VariantAttributes;

module EnumVariants;

public enum Lighting { None, Baked, Realtime }

[VariantAxis]
extern static const Lighting LightingMode;

[VariantAxis]
extern static const bool Shadows;
```

This is [EnumVariants.slang](../../Tests/ShaderDef.Compiler/Shaders/EnumVariants.slang). Rules:

- The type must be `bool` (values `"false"`, `"true"`) or an enum (values are the case names). Anything else throws `Variant axis '<name>' has unsupported type '<type>'. Only bool and enum axes are supported.`
- An enum used as an axis type must be `public`.
- Axes declared in modules the pass imports count, as long as the pass transitively imports that module.
- The axis name is the variable name; that is what you pass to `new Keyword(name, value)`.

The number of variants is the product of the value counts of all axes. See [the variant model](../internals/06-shader-compiler.md#the-variant-and-keyword-model) for a worked example.

### Parse errors

All structural problems throw `ParseException` (with `Line` and `Column`, and `at line N, column M.` in the message):

| Condition | Message |
|---|---|
| Pass without `SLANGPROGRAM` | `Each Pass must contain a SLANGPROGRAM block` |
| No `ENDSLANG` | `Unterminated SLANGPROGRAM block: missing closing 'ENDSLANG'` |
| No passes | `Shader must contain at least one Pass` |
| Text after the final `}` | `Unexpected content '...' after shader` |
| Duplicate tag key, property name or pass name | `Duplicate tag key '...'`, `Duplicate property '...'`, `Duplicate pass name '...'` |
| Misspelled command | `Unknown command '...'` |
| Wrong enum value | `Expected any of '...' but got '...'` |
| Bad number | `'...' is not a valid integer` or `number` |
| Property default of the wrong shape | `<Type> property expects <shape>, but found '...'` |

## Object model

Namespace `Prowl.Graphite.ShaderDef` (Core assembly).

### ShaderDefinition

[ShaderDefinition](../../ShaderDef/Core/ShaderDefinition.cs) is a parsed shader plus its device binding.

| Member | Signature | Description |
|---|---|---|
| `Name` | `string? Name` | Shader name. |
| `Fallback` | `string? Fallback` | Fallback name, empty if none. |
| `Properties` | `ShaderProperty[]? Properties` | Parsed properties. |
| `Passes` | `ShaderPass[]? Passes` | Parsed passes, in file order. |
| `IsCreated` | `bool IsCreated { get; }` | True after any `Create`. |
| `Create` | `void Create(GraphicsDevice device, CompileMode mode = CompileMode.OnDemand)` | No compiler, no variants. Used for structure inspection. |
| `Create` | `void Create(GraphicsDevice device, IShaderCompiler compiler, Variant fallback, CompileMode mode = CompileMode.OnDemand)` | Discovers axes through the compiler and compiles on demand or all at once. `fallback` is required. |
| `Create` | `void Create(GraphicsDevice device, ShaderSnapshot snapshot)` | Restore from a snapshot with no compiler. Pass count must match. |
| `Create` | `void Create(GraphicsDevice device, ShaderSnapshot snapshot, IShaderCompiler compiler, Variant fallback)` | Restore, and allow missing variants to compile. |
| `Snapshot` | `ShaderSnapshot Snapshot()` | Capture axes and every populated variant. |
| `GetPassIndex` | `int GetPassIndex(string passName)` | -1 if not found. |
| `GetPass` | `ShaderPass GetPass(string passName)` | Throws if not found. |
| `GetPassWithTag` | `int? GetPassWithTag(string tag, string? tagValue = null)` | First pass with the tag. |
| `GetPassesWithTag` | `List<int> GetPassesWithTag(string tag, string? tagValue = null)` | All passes with the tag. |
| `PassHasTag` | `static bool PassHasTag(ShaderPass pass, string tag, string? tagValue = null)` | |

### ShaderPass

[ShaderPass](../../ShaderDef/Core/ShaderPass.cs#L11) holds one pass. Fields set by the parser:

| Member | Signature | Description |
|---|---|---|
| `Name` | `string Name` | Empty if unnamed. |
| `Tags` | `Dictionary<string, string>? Tags` | Null if no `Tags` block. |
| `State` | `required PassState State` | Fixed-function state from the markup. |
| `InlineSlang` | `required string InlineSlang` | The Slang source. |

The keyword and variant members (`SetKeyword`, `ApplyKeywords`, `ActiveVariant`, `CompileAll`, `Axes`, and so on) are documented in [Shader programs](shader-programs.md#keywords-and-variants). You can construct a `ShaderPass` directly (both required fields must be set) to compile a bare Slang file, as [ShaderLoader](../../Samples/Shared/ShaderLoader.cs) does.

### PassState

[PassState](../../ShaderDef/Core/PassState.cs) is a class of nullable fields, one per markup concept (`CullMode`, `EnableDepthTest`, `DepthFunc`, `DepthWriteMask`, `EnableDepthClamp`, `EnableBlend`, `BlendSrcRgb`, ..., `WriteMask`, `AlphaToMask`, `StencilRef`, `StencilFrontFunc`, and so on). Null means "not specified".

| Member | Signature | Description |
|---|---|---|
| `ToBlendState` | `BlendStateDescription ToBlendState(BlendStateDescription baseState)` | Overlay onto a base; set fields win. Writes attachment 0 only. |
| `ToDepthStencilState` | `DepthStencilStateDescription ToDepthStencilState(DepthStencilStateDescription baseState)` | Overlay onto a base. |
| `ToRasterizerState` | `RasterizerStateDescription ToRasterizerState(RasterizerStateDescription baseState)` | Overlay onto a base. Applies `CullMode`, `FrontFace` and depth clamp. |
| `Apply` | `PassState Apply(PassState other)` | Merge; `this` wins per field. |

### Keyword, VariantSpace, Variant, ShaderSnapshot

| Type | Kind | Description |
|---|---|---|
| `Keyword` | readonly struct | `new Keyword(string name, string value)`. Interned, cheap to compare. |
| `VariantSpace` | readonly struct | One axis: `Name`, `DeclType`, `Values`, `IsEnum`, `TypeModule`. |
| `Variant` | sealed class | `Keywords` plus `Compiled`, an array of `(GraphicsBackend, ShaderDescription)`. `IsCompiledFor(backend)` and `TryGetDescription(backend, out description)`. `new Variant()` is an empty variant. |
| `ShaderSnapshot` / `PassSnapshot` | structs | Serializable capture: per pass `Axes` and `Variants`. |
| `CompileMode` | enum | `OnDemand` (compile when first needed) or `All` (compile every variant in `Create`). |

## Compiler API

Namespace `Prowl.Graphite.ShaderDef.Compiler` (Compiler assembly; references native Slang).

### ShaderParser

[ShaderParser](../../ShaderDef/Compiler/ShaderParser.cs) is a static class.

| Member | Signature | Description |
|---|---|---|
| `Parse` | `static ShaderDefinition Parse(string source)` | Parse a whole file. Throws `ParseException`. |
| `ParsePass` | `static ShaderPass ParsePass(string source)` | Parse a single `Pass { ... }` block. |
| `ParsePassState` | `static PassState ParsePassState(string source)` | Parse render-state commands only. Stops at the first unrecognized identifier. |
| `ParseProperty` | `static ShaderProperty ParseProperty(string source)` | Parse one property line. |

### SlangShaderCompiler

[SlangShaderCompiler](../../ShaderDef/Compiler/SlangShaderCompiler.cs) implements `IShaderCompiler`. One instance compiles many shaders and shares the Slang module cache.

| Member | Signature | Description |
|---|---|---|
| `Modules` | `ReadOnlyCollection<CompilerModule> Modules { get; }` | Registered backends. |
| `RegisterModule` | `void RegisterModule(CompilerModule module)` | Add a backend. Must precede `BeginSession`. No way to remove. |
| `GetModuleIndex` | `int GetModuleIndex(CompilerModule module)` | Position in the list. |
| `RegisterDiagnosticHandler` | `void RegisterDiagnosticHandler(DiagnosticHandler handler)` | Receives Slang errors and warnings. Default writes messages to the console. |
| `BeginSession` | `void BeginSession(DirectoryInfo[] searchPaths, Func<string, Memory<byte>?>? provider = null, (string, string)[]? pragmas = null)` | Start a Slang session. `provider` returns file bytes by path; `pragmas` are preprocessor macro name/value pairs. Matrices are column-major. |
| `EndSession` | `void EndSession()` | Drop the session and all cached modules. Registered modules and handler survive. |
| `GetAxes` | `IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass)` | Discover the pass's variant axes. Cached per pass instance. |
| `Compile` | `ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend)` | Compile one variant. `combo` has one keyword per axis, in axis order; `[]` for a shader without axes. |

### CompilerModule implementations

| Class | Constructor | Status |
|---|---|---|
| `VulkanCompiler` | `VulkanCompiler(string profileString = "spirv_1_5")` | Compiles SPIR-V. The samples use `"spirv_1_4"`. |
| `MetalCompiler` | `MetalCompiler(string profileString = "metal_2_0")` | `Backend` and `CompileForTarget` throw `NotImplementedException`. |
| `WebGPUCompiler` | `WebGPUCompiler(string profileString = "wgsl_1_0")` | Same as `MetalCompiler`. |
| `DXCompiler` | `DXCompiler(string profileString = "sm_5_0")` | Same as `MetalCompiler` (D3D11 is not a Graphite backend). |

`CompilerModule` is a public interface whose key members are internal, so you cannot implement your own backend outside the assembly.

### IShaderCompiler

```csharp
public interface IShaderCompiler
{
    IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass);
    ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend);
}
```

An implementation replaces the compiler, for example with a stub in tests. The result of `Compile` must not include fixed-function state; `ShaderPass` adds it.

## SlangQuickCompile

[Tools/SlangQuickCompile](../../Tools/SlangQuickCompile/Program.cs) is a generator for the compiler test suite's known-good SPIR-V files. For each shader in its manifest (`Graphics`, `Modules`, `ConstantBuffers`, `ParameterBlocks`, `Variants`, `UVOriginUsage`) it reads the `.slang` file, discovers the axes through `GetAxes`, compiles every combination for Vulkan, and lists or writes one file per stage and combination named `<module>.<stage><suffix>.spv`.

```
dotnet run Tools/SlangQuickCompile/Program.cs -- [--dump] [--write] [shaderName ...]
```

| Flag | Effect |
|---|---|
| `--dump` (default) | Print the files that would be produced and their sizes. |
| `--write` | Write them into the known-good directory. |
| `shaderName` | Restrict to named manifest entries. |

The tool reads shaders from `Tests/ShaderDef.Compiler/Shaders` and compares against `Tests/ShaderDef.Compiler/KnownGood`.

## Common patterns

### Preload everything, then end the session

`CompileMode.All` compiles every variant in `Create`, so `EndSession` can follow and no draw-time compile occurs, as in the PBR sample.

### Compile on demand while developing

```csharp
def.Create(device, compiler, new Variant());
```

The default `CompileMode.OnDemand` compiles each variant when it is first needed. On-demand compilation requires the session to stay open for the life of the shader. The first use of each variant pauses command recording while Slang runs.

### Ship baked shaders

```csharp
ShaderSnapshot snapshot = def.Snapshot();
```

A snapshot serializes in the application's own format; at runtime `def.Create(device, snapshot)` restores it on a definition parsed from the same source. The runtime build needs only the Core assembly.

### Tag-driven pass selection

```csharp
int? shadowPass = def.GetPassWithTag("LightMode", "ShadowCaster");
```

## Pitfalls

- `Offset` takes two space separated floats: `Offset -1 -1`. A comma between them is a parse error.
- Blend factor names are `SourceAlpha`, `InverseSourceAlpha` and so on, as listed above; `SrcAlpha` and `OneMinusSrcAlpha` are not valid.
- `Fallback` is optional.
- `Stencil { ... }` never enables the stencil test by itself.
- Keyword values for bool axes are lowercase `"true"` and `"false"`.
- `Create` with a compiler requires a fallback `Variant`.
- After `EndSession`, `GetAxes` and `Compile` throw `Compile called before BeginSession!`. `GetAxes` and `Compile` require an open session; `EndSession` closes it.
- Only Vulkan compiles. The other backend modules throw `NotImplementedException` when their `Backend` property is read.

## See also

- [Internals: Shader compiler](../internals/06-shader-compiler.md)
- [Internals: Programs and pipelines](../internals/05-programs-and-pipelines.md)
- [Shader programs](shader-programs.md)
- [Command buffers](command-buffers.md)
- [ShaderDef README](../../ShaderDef/README.md) and [ShaderSpec.md](../../ShaderDef/Core/ShaderSpec.md)
