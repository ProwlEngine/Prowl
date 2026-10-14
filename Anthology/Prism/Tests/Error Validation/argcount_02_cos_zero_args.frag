// cos takes 1 argument but is called with 0
#version 450
layout(location=0) out vec4 outColor;
void main() {
    float r = cos();
    outColor = vec4(r, r, r, 1.0);
}
