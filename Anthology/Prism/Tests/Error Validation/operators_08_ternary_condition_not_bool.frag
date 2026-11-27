#version 450
// Ternary condition is a vec3 but it must be a scalar bool
layout(location=0) out vec4 outColor;
void main() {
    vec3 cond = vec3(1.0, 0.0, 1.0);
    float f = cond ? 1.0 : 2.0;
    outColor = vec4(f);
}
