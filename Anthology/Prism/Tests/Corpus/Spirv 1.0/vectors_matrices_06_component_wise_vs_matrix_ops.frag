#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    mat3 m1 = mat3(
        vec3(1.0, 2.0, 3.0),
        vec3(0.5, 1.5, 2.5),
        vec3(0.2, 0.4, 0.6)
    );
    mat3 m2 = mat3(
        vec3(0.1, 0.2, 0.3),
        vec3(1.0, 1.0, 1.0),
        vec3(2.0, 2.0, 2.0)
    );

    // component wise multiply vs linear algebra matrix multiply
    mat3 comp_mult = matrixCompMult(m1, m2);
    mat3 mat_mult = m1 * m2;

    mat3 transposed = transpose(m1);
    mat3 inverted = inverse(m1);
    float det = determinant(m1);

    mat3 outer = outerProduct(vec3(1.0, 0.5, 0.25), vec3(0.3, 0.6, 0.9));

    vec3 combined = comp_mult[0] + mat_mult[1] + transposed[2] + inverted[0] * det + outer[1];

    FragColor = vec4(combined, 1.0);
}
