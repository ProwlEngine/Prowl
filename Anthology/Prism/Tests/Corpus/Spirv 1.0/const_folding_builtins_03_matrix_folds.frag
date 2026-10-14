#version 460 core

// Constant matrix built-ins and products. glslang folds determinant/transpose/matrixCompMult and the
// matrix products themselves, so none of this should reach a runtime opcode.

layout(location = 0) out vec4 FragColor;

const mat3 M = mat3(2.0, 0.0, 0.0,
                    0.0, 3.0, 0.0,
                    0.0, 0.0, 4.0);
const mat3 K = mat3(1.0, 2.0, 3.0,
                    4.0, 5.0, 6.0,
                    7.0, 8.0, 9.0);

void main()
{
    float det = determinant(M);
    mat3 t = transpose(K);
    mat3 cw = matrixCompMult(M, K);
    mat3 prod = M * K;
    vec3 v = M * vec3(1.0, 1.0, 1.0);
    mat2 inv2 = inverse(mat2(2.0, 0.0, 0.0, 4.0));

    vec3 acc = t[0] + cw[1] + prod[2] + v + vec3(inv2[0], 0.0);
    FragColor = vec4(acc, det);
}
