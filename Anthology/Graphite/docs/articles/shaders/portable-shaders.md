---
uid: portable-shaders
---

# Writing Portable Shaders

In NeoVeldrid, shader code is backend-specific. The [Shaders](xref:shaders-and-resources) article documents what kind of shader data must be supplied for each [GraphicsBackend](xref:NeoVeldrid.GraphicsBackend). Except for small-scale projects, it is usually a waste of effort to manually write each of your shaders in 4-5 different languages. Instead, it is recommended to use a system where you write your shaders once, in some source language, and automatically cross-compile or translate them into all other languages. The recommended approach is to use NeoVeldrid.SPIRV.

## NeoVeldrid.SPIRV

[![NuGet](https://img.shields.io/nuget/v/NeoVeldrid.SPIRV.svg)](https://www.nuget.org/packages/NeoVeldrid.SPIRV)

SPIR-V is a portable bytecode language for graphics and compute shaders. It is the primary shader format for Vulkan, and can be used directly in NeoVeldrid to create Shader modules with that backend. SPIR-V is designed to be portable and platform-agnostic, and there are a variety of tools available for manipulating, debugging, and optimizing its bytecode. NeoVeldrid.SPIRV is a portable .NET library that utilizes [SPIRV-Cross](https://github.com/KhronosGroup/SPIRV-Cross) to translate SPIR-V shaders into HLSL, GLSL, and MSL shaders. It exposes extension methods on the [ResourceFactory](xref:NeoVeldrid.ResourceFactory) type that allow you to create Shader modules for any backend with SPIR-V bytecode data. Using NeoVeldrid.SPIRV, it is feasible to simply compile your shaders once to SPIR-V, and then rely on the library to translate your bytecode into the necessary format at runtime.

SPIR-V is a compilation target for several languages, including GLSL and HLSL. Other languages have plans to add SPIR-V as a compilation target. The [SPIRV-Tools repository](https://github.com/KhronosGroup/SPIRV-Tools) has a number of tools that aid in the use and analysis of SPIR-V. Overall, building shaders in SPIR-V is a well-supported and effective solution for creating portable shaders.