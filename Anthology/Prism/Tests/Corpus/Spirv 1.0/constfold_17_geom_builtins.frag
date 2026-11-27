#version 460
layout(location=0) out vec4 fragColor;

void main() {
    float l = length(vec3(3.0, 4.0, 0.0));                    // 5
    vec3 n = normalize(vec3(0.0, 0.0, 2.0));                  // (0,0,1)
    float dp = dot(vec3(1.0, 2.0, 3.0), vec3(4.0, 5.0, 6.0)); // 32
    vec3 cp = cross(vec3(1.0, 0.0, 0.0), vec3(0.0, 1.0, 0.0));// (0,0,1)
    fragColor = vec4(l / 5.0, n.z, dp / 32.0, cp.z);
}
