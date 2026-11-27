#version 450
layout(location = 0) in vec3 vA;
layout(location = 1) in vec3 vB;
layout(location = 0) out vec4 oColor;
void main() {
    float len = length(vA);
    float dist = distance(vA, vB);
    float d = dot(vA, vB);
    vec3 c = cross(vA, vB);
    vec3 n = normalize(vA + vec3(1e-5));
    oColor = vec4(n, len + dist + d + c.x);
}
