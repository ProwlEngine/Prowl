#version 450
// Matrix constructor is given a single vec2 which cannot build a mat3
layout(location=0) out vec4 outColor;
void main() {
    mat3 m = mat3(vec2(1.0, 2.0));
    outColor = vec4(m[0], 1.0);
}
