#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    vec2 u_v2;
    vec4 u_v4;
};

void main() {
    uint p_unorm2x16 = packUnorm2x16(u_v2);
    vec2 u_unorm2x16 = unpackUnorm2x16(p_unorm2x16);

    uint p_snorm2x16 = packSnorm2x16(u_v2);
    vec2 u_snorm2x16 = unpackSnorm2x16(p_snorm2x16);

    uint p_half2x16 = packHalf2x16(u_v2);
    vec2 u_half2x16 = unpackHalf2x16(p_half2x16);

    uint p_unorm4x8 = packUnorm4x8(u_v4);
    vec4 u_unorm4x8 = unpackUnorm4x8(p_unorm4x8);

    vec2 total2 = u_unorm2x16 + u_snorm2x16 + u_half2x16;
    vec4 total = u_unorm4x8 + vec4(total2, 0.0, 0.0);

    FragColor = total;
}
