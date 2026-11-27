#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    vec3 v;
};

void main() {
    float sip;
    float sfrac = modf(a, sip);

    vec3 vip;
    vec3 vfrac = modf(v, vip);

    fragColor = vec4(vfrac + vip, sfrac + sip);
}
