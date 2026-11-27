#version 460

layout(set=0, binding=0) uniform samplerCubeArray texCubeArr;
layout(set=0, binding=1) uniform sampler2DArrayShadow texArrShadow;

layout(location=0) out vec4 fragColor;

void main() {
    vec2 uv = gl_FragCoord.xy * 0.001;
    vec3 dir = normalize(vec3(uv, 1.0));
    float depth = gl_FragCoord.z;
    vec4 a = texture(texCubeArr, vec4(dir, 1.0));
    float b = texture(texArrShadow, vec4(uv, 0.0, depth));
    fragColor = a + vec4(b);
}
