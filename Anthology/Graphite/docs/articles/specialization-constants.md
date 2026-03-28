---
uid: specialization-constants
---

# Specialization Constants

SPIR-V and Metal shaders both support a notion of "Specialization Constants" (called "function constants" in Metal). These are special constant variables defined in a shader that can be "specialized" after compilation, effectively being replaced with a new value. For example, consider the following GLSL shader, which contains several SPIR-V Specialization Constants.

```GLSL
#version 450

layout (set = 0, binding = 0) uniform texture2D Tex;
layout (set = 0, binding = 1) uniform sampler Smp;

layout (constant_id = 0) const bool UseTexture = false;
layout (constant_id = 1) const bool FlipTexture = false;

layout (constant_id = 2) const float RedChannel = 0.1f;
layout (constant_id = 3) const float GreenChannel = 0.1f;
layout (constant_id = 4) const float BlueChannel = 0.1f;

layout (location = 0) in vec2 fsin_TexCoords;
layout (location = 0) out vec4 fsout_Color0;

void main()
{
    if (UseTexture)
    {
        vec2 uv = fsin_TexCoords;
        if (FlipTexture) { uv.y = 1 - uv.y; }
        fsout_Color0 = texture(sampler2D(Tex, Smp), uv);
    }
    else
    {
        fsout_Color0 = vec4(RedChannel, GreenChannel, BlueChannel, 1.0);
    }
}
```

SPIR-V Specialization Constants are decorated with a `layout(constant_id = x)` qualifier. Each Specialization Constant has a unique ID which can be used to identify it. All Specialization Constants have a default value, and determine how a Shader will behave if not overridden.

To override a Specialization Constant in Veldrid, you define an array of [SpecializationConstants](xref:Veldrid.SpecializationConstant), each element containing a key-value pair describing a single constant substitution. When creating a graphics [Pipeline](xref:Veldrid.Pipeline), use this array to set the [ShaderSetDescription.Specializations field](xref:Veldrid.ShaderSetDescription#Veldrid_ShaderSetDescription_Specializations). For compute Pipelines, set [ComputePipelineDescription.Specializations](xref:Veldrid.ComputePipelineDescription#Veldrid_ComputePipelineDescription_Specializations). Below is an example array that could be used to specialize the values in the shader above:

```C#
ShaderSetDescription shaderSetDesc = new ShaderSetDescription(
    vertexLayoutDescriptions,
    new Shader[] { vertexShader, fragmentShader },
    new SpecializationConstant[]
    {
        new SpecializationConstant(0, true), // UseTexture = true
        new SpecializationConstant(1, true), // FlipTexture = true
        new SpecializationConstant(2, 0.95f), // RedChannel = 0.95f
        new SpecializationConstant(3, 0.0f), // GreenChannel = 0f
        new SpecializationConstant(4, 0.5f), // BlueChannel = 0.5f
    });
```

If this ShaderSetDescription is used to create a Vulkan or Metal Pipeline, then the SpecializationConstant values listed in the array will replace the pre-defined constants in the shader. It is therefore trivial to create another Pipeline which substitutes different constant values by passing in a different array. SPIR-V Specialization Constants always contain default values, so providing SpecializationConstants is optional. You may override a subset (or none) of the Specialization Constants defined in the shader.

## HLSL and GLSL

Unfortunately, HLSL and OpenGL-style GLSL do not support any kind of specialization constants. All constant values used in the shader must be baked into the shader itself when it is compiled. However, [Veldrid.SPIRV](xref:portable-shaders#veldridspirv) allows you to substitute new values in for each Specialization Constant before the shader is translated from SPIR-V into the target language. In practice, this allows you to use SPIR-V shaders with all of Veldrid’s backends and still take advantage of the flexibility of Specialization Constants.
