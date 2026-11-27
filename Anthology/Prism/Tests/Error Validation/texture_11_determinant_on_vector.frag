// determinant is given a vec4 but it requires a matrix argument
#version 450
layout(location=0) out vec4 outColor;
layout(location=0) in vec4 v;
void main() {
    float d = determinant(v);
    outColor = vec4(d);
}
