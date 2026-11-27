#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    vec4 g = textureGather(tex2d, uv);
    vec4 g0 = textureGather(tex2d, uv, 0);
    vec4 g1 = textureGather(tex2d, uv, 1);
    vec4 g2 = textureGather(tex2d, uv, 2);
    vec4 g3 = textureGather(tex2d, uv, 3);
    fragColor = g + g0 + g1 + g2 + g3;
}
