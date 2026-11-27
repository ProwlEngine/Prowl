#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    vec4 p4 = vec4(uv, 0.0, 2.0);
    const ivec2 off = ivec2(2, -3);
    vec4 a = textureProjLod(tex2d, p4, 1.5);
    vec4 b = textureProjOffset(tex2d, p4, off);
    fragColor = a + b;
}
