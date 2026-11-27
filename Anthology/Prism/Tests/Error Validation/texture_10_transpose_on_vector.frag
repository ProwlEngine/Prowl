// transpose is given a vec3 but it requires a matrix argument
#version 450
layout(location=0) out vec4 outColor;
layout(location=0) in vec3 v;
void main() {
    vec3 r = transpose(v);
    outColor = vec4(r, 1.0);
}
