#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    const ivec2 off = ivec2(1, 2);
    vec2 dx = vec2(uv.x * 0.01, 0.0);
    vec2 dy = vec2(0.0, uv.y * 0.01);
    vec4 a = textureOffset(tex2d, uv, off);
    vec4 b = textureLodOffset(tex2d, uv, 1.0, off);
    vec4 c = textureGradOffset(tex2d, uv, dx, dy, off);
    fragColor = a + b + c;
}
