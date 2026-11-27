#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    vec3 v;
};

void main() {
    bool sn = isnan(a);
    bool si = isinf(a);
    bvec3 vn = isnan(v);
    bvec3 vi = isinf(v);
    float r = float(sn) + float(si);
    vec3 vr = vec3(vn) + vec3(vi);
    fragColor = vec4(vr, r);
}
