#version 460 core

void main()
{
    // matrix constant folding for mat3 and mat4
    const mat3 m3a = mat3(1.0, 0.0, 0.0,
                           0.0, 1.0, 0.0,
                           0.0, 0.0, 1.0);
    const mat3 m3b = mat3(2.0, 0.0, 0.0,
                           0.0, 2.0, 0.0,
                           0.0, 0.0, 2.0);
    const mat3 m3res = (m3a + m3b) * 0.5;

    const mat4 m4a = mat4(1.0);
    const mat4 m4b = mat4(2.0);
    const mat4 m4res = m4a * m4b;

    const vec4 base_pos = vec4(1.0, 1.0, 1.0, 1.0);
    const vec3 folded = m3res * vec3(base_pos.xyz);

    gl_Position = m4res * vec4(folded, base_pos.w);
}
