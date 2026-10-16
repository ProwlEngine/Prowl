#version 450
layout(location = 0) in mat3 vM;
layout(location = 0) out vec4 oColor;
void main() {
    mat3 t = transpose(vM);
    mat3 inv = inverse(vM);
    float det = determinant(vM);
    mat3 comp = t * inv;
    oColor = vec4(comp[0], det);
}
