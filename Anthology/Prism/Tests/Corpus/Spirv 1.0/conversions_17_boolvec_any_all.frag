#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    vec3 x;
    vec3 y;
};

void main() {
    bvec3 lt = lessThan(x, y);
    bool anyLt = any(lt);
    bool allLt = all(lt);
    bvec3 n = not(lt);
    float r = (anyLt ? 1.0 : 0.0) + (allLt ? 1.0 : 0.0) + float(n.x);
    fragColor = vec4(r);
}
