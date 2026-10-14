#version 460

layout(location = 0) in vec4 inCol;

layout(binding = 0) uniform U {
    mat3 m;
} u;

void main() {
    mat3 cm = matrixCompMult(u.m, u.m);
    mat3 op = outerProduct(inCol.xyz, inCol.xyz);
    mat3 t = transpose(cm);
    float d = determinant(u.m);
    mat3 inv = inverse(u.m);
    vec3 r = (cm + op + t + inv) * inCol.xyz * d;
    gl_Position = vec4(r, 1.0);
}
