#version 460 core

layout(location=0) in vec3 in_position;

void main()
{
    // diagonal from scalar
    mat4 identity_mat = mat4(1.0);

    // mat3 built from three vec3 columns
    mat3 col_mat = mat3(
        vec3(1.0, 0.0, 0.0),
        vec3(0.0, 1.0, 0.0),
        vec3(0.0, 0.0, 1.0)
    );

    // mat4 built from 16 individual floats
    mat4 full_mat = mat4(
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.1, 0.2, 0.3, 1.0
    );

    vec3 transformed = col_mat * in_position;
    gl_Position = identity_mat * full_mat * vec4(transformed, 1.0);
}
