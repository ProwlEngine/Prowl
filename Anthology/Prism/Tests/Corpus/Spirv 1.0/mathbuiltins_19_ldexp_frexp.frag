#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    vec3 v;
};

void main() {
    int se;
    float sm = frexp(a, se);
    float sl = ldexp(sm, se);

    ivec3 ve;
    vec3 vm = frexp(v, ve);
    vec3 vl = ldexp(vm, ve);

    fragColor = vec4(vl + vec3(ve), sl + float(se));
}
