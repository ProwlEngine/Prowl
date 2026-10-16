#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float e0;
    float e1;
    float a;
    vec3 v;
    vec3 lo;
    vec3 hi;
};

void main() {
    float s = step(e0, a) + smoothstep(e0, e1, a);
    vec3 sv = step(e0, v) + smoothstep(e0, e1, v);
    vec3 vv = step(lo, v) + smoothstep(lo, hi, v);
    fragColor = vec4(sv + vv, s);
}
