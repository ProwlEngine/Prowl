#version 460

layout(set=0, binding=0) uniform isampler2D iTex;
layout(set=0, binding=1) uniform usampler2D uTex;

layout(location=0) out vec4 fragColor;

void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    ivec4 a = texelFetch(iTex, p, 0);
    uvec4 b = texelFetch(uTex, p, 0);
    fragColor = vec4(a) + vec4(b);
}
