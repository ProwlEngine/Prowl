#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    float b;
    vec3 x;
    vec3 y;
};

void main() {
    bool s = a < b;
    bvec3 lt = lessThan(x, y);
    bvec3 gt = greaterThan(x, y);
    float r = (s ? 1.0 : 0.0) + float(lt.x) + float(gt.z);
    fragColor = vec4(r);
}
