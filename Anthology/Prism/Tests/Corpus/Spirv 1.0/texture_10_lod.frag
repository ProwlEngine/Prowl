#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;
layout(set=0, binding=1) uniform samplerCube texCube;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    vec3 dir = normalize(vec3(uv, 1.0));
    vec4 a = textureLod(tex2d, uv, 2.0);
    vec4 b = textureLod(texCube, dir, 1.0);
    fragColor = a + b;
}
