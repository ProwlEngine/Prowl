#version 460 core
#extension GL_EXT_fragment_shader_barycentric : require

// Barycentric interpolation of a pervertexEXT input using gl_BaryCoordEXT.

layout(location = 0) pervertexEXT in vec4 v_color[3];
layout(location = 0) out vec4 fragColor;

void main()
{
    vec3 bc = gl_BaryCoordEXT;
    fragColor = v_color[0] * bc.x + v_color[1] * bc.y + v_color[2] * bc.z;
}
