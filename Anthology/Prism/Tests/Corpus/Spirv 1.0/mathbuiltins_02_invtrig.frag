#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    vec3 v;
};

void main() {
    float s = asin(a) + acos(a) + atan(a) + atan(v.y, v.x);
    vec3 sv = asin(v) + acos(v) + atan(v) + atan(v, v.zyx);
    fragColor = vec4(sv, s);
}
