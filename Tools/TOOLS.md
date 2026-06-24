## Precompilation/Static tool utilities for the runtime

This repository houses all static/pre-build tools required to generate, or re-generate default assets for the Prowl game engine that cannot, or should not be generated at runtime due to requiring pulling in an unwanted dependency or similar.

### Running a tool

Use the default .net 10 file-based app runner to run this. If you have a .net 10 installation, running `dotnet run ./MyScript.cs` should *just work*.

### UIShaderCompiler.cs

Compiles the Slang UI shaders into serialized Echo blobs for all supported graphics backends that the GUI render reads at runtime.

### BrdfGen.cs

Generates the BRDF lookup texture used for PBR rendering