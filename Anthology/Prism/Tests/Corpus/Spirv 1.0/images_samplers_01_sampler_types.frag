#version 460 core

// sampler2d, sampler3d, samplerCube, sampler2DArray, samplerCubeArray, sampler2DShadow
layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform sampler2D tex_2d;
layout(binding = 1) uniform sampler3D tex_3d;
layout(binding = 2) uniform samplerCube tex_cube;
layout(binding = 3) uniform sampler2DArray tex_2d_array;
layout(binding = 4) uniform samplerCubeArray tex_cube_array;
layout(binding = 5) uniform sampler2DShadow tex_shadow;

void main() {
    vec4 c0 = texture(tex_2d, vec2(0.5, 0.5));
    vec4 c1 = texture(tex_3d, vec3(0.5, 0.5, 0.5));
    vec4 c2 = texture(tex_cube, vec3(0.5, 0.5, 0.5));
    vec4 c3 = texture(tex_2d_array, vec3(0.5, 0.5, 0.0));
    vec4 c4 = texture(tex_cube_array, vec4(0.5, 0.5, 0.5, 0.0));
    float c5 = texture(tex_shadow, vec3(0.5, 0.5, 0.5));

    FragColor = c0 + c1 + c2 + c3 + c4 + vec4(c5, c5, c5, 1.0);
}
