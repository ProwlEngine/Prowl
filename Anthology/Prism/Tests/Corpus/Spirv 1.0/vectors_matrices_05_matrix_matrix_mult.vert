#version 460 core

layout(location=0) in vec3 in_position;

void main()
{
    mat4 model = mat4(1.0);
    mat4 view = mat4(
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, -3.0, 1.0
    );

    mat4 model_view = view * model;

    mat3 scale3 = mat3(
        vec3(2.0, 0.0, 0.0),
        vec3(0.0, 2.0, 0.0),
        vec3(0.0, 0.0, 2.0)
    );
    mat3 rot3 = mat3(
        vec3(1.0, 0.0, 0.0),
        vec3(0.0, 1.0, 0.0),
        vec3(0.0, 0.0, 1.0)
    );
    mat3 combined3 = scale3 * rot3;

    vec3 transformed = combined3 * in_position;
    gl_Position = model_view * vec4(transformed, 1.0);
}
