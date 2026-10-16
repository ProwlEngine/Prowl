#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    vec3 a;
    vec3 b;
};

void main() {
    bvec3 lt = lessThan(a, b);
    bvec3 le = lessThanEqual(a, b);
    bvec3 gt = greaterThan(a, b);
    bvec3 ge = greaterThanEqual(a, b);
    bvec3 eq = equal(a, b);
    bvec3 ne = notEqual(a, b);
    bool anyLt = any(lt);
    bool allGe = all(ge);
    float r = float(anyLt) + float(allGe)
            + float(le.x) + float(gt.y) + float(eq.z) + float(ne.x);
    fragColor = vec4(r, float(lt.x), float(gt.z), 1.0);
}
