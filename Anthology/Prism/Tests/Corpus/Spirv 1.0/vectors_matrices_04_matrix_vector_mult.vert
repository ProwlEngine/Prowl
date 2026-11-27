#version 460 core

layout(location=0) in vec3 in_position;

void main()
{
    mat4 transform = mat4(
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.2, 0.1, 0.0, 1.0
    );
    vec4 pos = vec4(in_position, 1.0);

    // mat4 * vec4 and vec4 * mat4 give different results
    vec4 result_mv = transform * pos;
    vec4 result_vm = pos * transform;

    mat3 rot = mat3(
        vec3(1.0, 0.0, 0.0),
        vec3(0.0, 1.0, 0.0),
        vec3(0.0, 0.0, 1.0)
    );
    vec3 rotated = rot * in_position;

    gl_Position = result_mv * 0.5 + result_vm * 0.5 + vec4(rotated, 1.0) * 0.0;
}
