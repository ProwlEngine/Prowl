#version 450

layout(set = 0, binding = 0) uniform texture2D Tex;
layout(set = 0, binding = 1) uniform texture2D Tex11;
layout(set = 0, binding = 2) uniform texture2D Tex22;
layout(set = 0, binding = 3) uniform sampler SS;

layout(location = 0) in vec2 fsin_TexCoords;
layout(location = 0) out vec4 OutColor;

void main()
{
    OutColor = texture(sampler2D(Tex, SS), fsin_TexCoords) + texture(sampler2D(Tex11, SS), fsin_TexCoords) * .01 + texture(sampler2D(Tex22, SS), fsin_TexCoords) * .01;
}
