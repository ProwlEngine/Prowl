# Prowl.Slang

A .NET wrapper over the [Slang](https://github.com/shader-slang/slang) shader compiler. Supports nearly all of Slang's native COM API and all of the shader reflection and type API.

[![NuGet](https://img.shields.io/nuget/v/Prowl.Slang.svg)](https://www.nuget.org/packages/Prowl.Slang)

## Features

- **Near-Complete Slang Support**
  - Nearly all of Slang's API is exposed to C# through a memory-safe and garbage collected proxy.
  - No need to track references or dispose of native Slang objects--the library handles it automatically using COM reference tracking and C# finalizers.

- **C# Language Conventions**
  - Native Slang methods which use C++-style parameters or type conventions are replaced.
  - Getters in reflection API have been replaced with C# properties (.GetFoo() -> .Foo)
  - No exposed native pointers or custom datatypes--wrapper uses C# datatypes as much as possible.
  - SlangResult has been replaced with C#-style exceptions to allow better use of the try-catch programming paradigm.
  - Compiler error messages in strings can now be parsed into a `Diagnostic` datatype, to simplify reading error codes, source files, line numbers, and more from C#.

- **Extensive XML Documentation**
  - All of the Slang API has been documented, including parts not originally documented in source Slang.
  - Complete XML documentation coverage, with intellisense for every function and type.

- **Cross Platform**
  - Supports all 3 major desktop platforms (Windows, Linux, MacOS)
  - Supports 64-bit x86 and ARM instruction sets

## Usage

### Basic Compilation

```csharp
using Prowl.Slang;

// Description for a single platform target output
TargetDescription targetDesc = new()
{
  Format = CompileTarget.Spirv, // Emit vulkan-compatible SPIR-V words
  Profile = GlobalSession.FindProfile("spirv_1_5") // Use SPIR-V 1.5 capabilities
};

// Description for a compilation session
SessionDescription sessionDesc = new()
{
  Targets = [targetDesc], // Only targeting SPIR-V - add more targets to automatically compile for multiple different APIs
  SearchPaths = ["./", "./Shaders"], // Search paths are relative to CWD
};

Session session = GlobalSession.CreateSession(sessionDesc);

// Load file called 'shaders.slang'. If warnings appear during loading, they will be written into `diagnostics`
Module module = session.LoadModule("shaders", out DiagnosticInfo diagnostics);

EntryPoint vertex = module.FindEntryPointByName("vertexMain");
EntryPoint fragment = module.FindEntryPointByName("fragmentMain");

// Create a composite program from the entrypoints. If warnings appear, they will be written into `diagnostics`
ComponentType program = session.CreateCompositeComponentType([module, vertex, fragment], out diagnostics);

// Vertex entrypoint at index 0. If warnings appear during compilation, they will be written into `diagnostics`
Memory<byte> compiledVertex = program.GetEntryPointCode(0, 0, out diagnostics);

// Fragment entrypoint at index 1. If warnings appear during compilation, they will be written into `diagnostics`
Memory<byte> compiledFragment = program.GetEntryPointCode(1, 0, out diagnostics);
```

### Advanced compilation

Examples of advanced compilation which may involve specializing shader types and performing advanced linking/reflection can be found under both the `Samples/` and `Tests/` directories.

## Limitations
  - Native Unsafe API is internal and not exposed. Marshaling and safety checks are non-negotiable for those looking to squeeze out faster performance from the library.
  - API is not completely memory safe. Certain combinations of reflection, specialization, and linker options can still corrupt process memory if used incorrectly.

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.

This repository includes software from the Slang shader compiler.

The following third-party component is used under the following license:

- Library Name: slang
- Source: https://github.com/shader-slang/slang
- License: Apache Version 2.0 with LLVM Exception license (https://github.com/shader-slang/slang/blob/master/LICENSE)

