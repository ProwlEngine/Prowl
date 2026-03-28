---
uid: portable-shaders
---

# Writing Portable Shaders

In Veldrid, shader code is backend-specific. The [Shaders](xref:shaders-and-resources) article documents what kind of shader data must be supplied for each [GraphicsBackend](xref:Veldrid.GraphicsBackend). Except for small-scale projects, it is usually a waste of effort to manually write each of your shaders in 4-5 different languages. Instead, it is recommended to use a system where you write your shaders once, in some source language, and automatically cross-compile or translate them into all other languages. There are two main supported libraries for creating such portable shaders with Veldrid.

## Veldrid.SPIRV

[![NuGet](https://img.shields.io/nuget/v/Veldrid.SPIRV.svg)](https://www.nuget.org/packages/Veldrid.SPIRV)

SPIR-V is a portable bytecode language for graphics and compute shaders. It is the primary shader format for Vulkan, and can be used directly in Veldrid to create Shader modules with that backend. SPIR-V is designed to be portable and platform-agnostic, and there are a variety of tools available for manipulating, debugging, and optimizing its bytecode. Veldrid.SPIRV is a portable .NET library that utilizes [SPIRV-Cross](https://github.com/KhronosGroup/SPIRV-Cross) to translate SPIR-V shaders into HLSL, GLSL, and MSL shaders. It exposes extension methods on the [ResourceFactory](xref:Veldrid.ResourceFactory) type that allow you to create Shader modules for any backend with SPIR-V bytecode data. Using Veldrid.SPIRV, it is feasible to simply compile your shaders once to SPIR-V, and then rely on the library to translate your bytecode into the necessary format at runtime.

SPIR-V is a compilation target for several languages, including GLSL and HLSL. Other languages have plans to add SPIR-V as a compilation target. The [SPIRV-Tools repository](https://github.com/KhronosGroup/SPIRV-Tools) has a number of tools that aid in the use and analysis of SPIR-V. Overall, building shaders in SPIR-V is a well-supported and effective solution for creating portable shaders.

## ShaderGen

[![NuGet](https://img.shields.io/nuget/v/ShaderGen.svg)](https://www.nuget.org/packages/ShaderGen)

ShaderGen is a .NET library which allows you to write shaders in C#. It takes in regular C# code and spits out HLSL, GLSL, and MSL shaders, by analyzing and manipulating the Roslyn compiler's semantic model. This allows your shader code to more cleanly integrate with your C# codebase, and allows you to share logic between your application and your shaders. However, it is less feature-rich than a true shading language like HLSL or GLSL, and requires offline compilation of shaders (at the moment). Since it is a kind of "custom-built shading language", there is also less documentation than GLSL or HLSL, which are thoroughly documented on their official web pages.