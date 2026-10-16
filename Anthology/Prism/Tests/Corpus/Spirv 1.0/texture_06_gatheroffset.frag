#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    const ivec2 off = ivec2(1, -1);
    vec4 g = textureGatherOffset(tex2d, uv, off);
    vec4 g2 = textureGatherOffset(tex2d, uv, off, 2);
    fragColor = g + g2;
}
