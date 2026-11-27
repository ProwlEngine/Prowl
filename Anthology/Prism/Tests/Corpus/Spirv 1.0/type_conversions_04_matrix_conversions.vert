#version 460 core

layout(location = 0) in vec3 in_position;

void main()
{
    mat4 m4 = mat4(1.0);

    // mat4 -> mat3 truncation
    mat3 m3_from_m4 = mat3(m4);

    // mat3 -> mat4 extension, fills identity for missing rows and columns
    mat4 m4_from_m3 = mat4(m3_from_m4);

    // mat2 built from 4 floats
    mat2 m2 = mat2(1.0, 0.0, 0.0, 1.0);

    // mat3 built from 3 vec3 columns
    vec3 col0 = vec3(1.0, 0.0, 0.0);
    vec3 col1 = vec3(0.0, 1.0, 0.0);
    vec3 col2 = vec3(0.0, 0.0, 1.0);
    mat3 m3 = mat3(col0, col1, col2);

    vec3 transformed = m3 * in_position;
    vec4 final_pos = m4_from_m3 * vec4(transformed, 1.0);
    final_pos.xy += m2 * vec2(0.0, 0.0);

    gl_Position = final_pos;
}
