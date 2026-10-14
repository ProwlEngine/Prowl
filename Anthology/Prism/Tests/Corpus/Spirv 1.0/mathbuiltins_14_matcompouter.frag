#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    mat3 m;
    mat3 n;
    vec3 a;
    vec3 b;
};

void main() {
    mat3 cm = matrixCompMult(m, n);
    mat3 op = outerProduct(a, b);
    mat3 sum = cm + op;
    fragColor = vec4(sum[0] + sum[1] + sum[2], 1.0);
}
