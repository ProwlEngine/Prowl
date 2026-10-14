#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    float t;
    vec3 v;
    vec3 w;
    bvec3 sel;
};

void main() {
    float s = mix(a, t, 0.5);
    vec3 lerped = mix(v, w, t);
    vec3 picked = mix(v, w, sel);
    fragColor = vec4(lerped + picked, s);
}
