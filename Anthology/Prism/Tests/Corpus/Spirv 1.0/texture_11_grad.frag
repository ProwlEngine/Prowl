#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;
layout(set=0, binding=1) uniform samplerCube texCube;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    vec3 dir = normalize(vec3(uv, 1.0));
    vec2 dx = vec2(uv.x * 0.01, 0.0);
    vec2 dy = vec2(0.0, uv.y * 0.01);
    vec4 a = textureGrad(tex2d, uv, dx, dy);
    vec4 b = textureGrad(texCube, dir, vec3(dx, 0.0), vec3(dy, 0.0));
    fragColor = a + b;
}
