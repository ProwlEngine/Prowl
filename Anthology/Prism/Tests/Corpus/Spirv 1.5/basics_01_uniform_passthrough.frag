#version 460 core

// A minimal SPIR-V 1.4 shader. At 1.4 the OpEntryPoint interface lists ALL global variables the entry
// point statically uses, not just Input/Output, so the uniform block appears in the interface too.

layout(set = 0, binding = 0) uniform UBO
{
    vec4 tint;
} ubo;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 FragColor;

void main()
{
    FragColor = ubo.tint * vec4(uv, 0.0, 1.0);
}
