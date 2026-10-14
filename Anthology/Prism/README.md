# Prowl.Prism

An **GLSL to SPIR-V** shader compiler for the Prowl Game Engine. Hand a shader source string to the library and get a SPIR-V module back, ready for a Vulkan pipeline, with no external toolchain needed on the target machine.

## Features

- **Preprocessor**
  - Object and function-like macros
  - `#if/#ifdef/#elif/#else/#endif` with full constant-expression evaluation
  - `##` token pasting
  - Predefined `__LINE__`, `__FILE__`, and `__VERSION__`
  - Recursion and expansion-depth guards

- **Parser**
  - GLSL 3.3-4.6 grammar
  - All scalar, vector, and matrix types
  - Control flow, functions (overloads, `out`/`inout`), structs, and interface blocks
  - Precision and layout qualifiers

- **Lowerer**
  - SPIR-V emission matched byte-for-byte against glslang's front-end output
  - Validated against a 1200+ shader real-world corpus
  - std140, std430, and scalar block layout
  - Full GLSL.std.450 and core builtin library
  - Images, atomics, and barriers
  - Ray tracing and ray query, mesh and task shaders
  - Specialization constants and full compile-time constant folding

- **Optimizer**
  - spirv-opt-shaped pipeline (DeadBranchElim, SsaRewrite, Ccp, GlobalCse, LoopUnroll, BlockMerge, ...)
  - Structural validation gate

- **Targets**
  - Vulkan SPIR-V 1.0-1.4
  - All shader stages: vertex, fragment, compute, geometry, tessellation, mesh, task, ray generation, intersection, any-hit, closest-hit, miss, and callable

## Usage

### Basic Compilation

```csharp
using Prowl.Prism;
using Prowl.Prism.Compiler;

SpirVModule module = GlslCompiler.Compile(source, ShaderStage.Fragment);
byte[] spirv = module.ToBytes(); // Feed to vkCreateShaderModule
```

### Variants and Options

```csharp
// Defines, SPIR-V version, optimizer pass, and opt-in location auto-mapping
byte[] variant = GlslCompiler.CompileToBytes(source, ShaderStage.Fragment,
    SpirVVersion.V1_4, defines: new Dictionary<string, string> { ["SHADOWS"] = "1" }, optimize: true,
    autoMapLocations: true);
```

### Error Handling

Malformed input always surfaces as a `Prowl.Prism.Glsl.GlslParseException` with the source line and a readable message. You will never get a raw exception, a hang, or silently wrong output. Malformed SPIR-V passed to the optimizer is rejected the same way.

## Testing

The test suite compiles the embedded corpus, which includes 1200+ real-world shaders and an intentional-error corpus whose expected diagnostics are captured as error-message goldens. Each output is byte-compared against pre-generated golden reference modules.

## License

This component is part of the Prowl Game Engine. See the LICENSE file in the project root for details.
