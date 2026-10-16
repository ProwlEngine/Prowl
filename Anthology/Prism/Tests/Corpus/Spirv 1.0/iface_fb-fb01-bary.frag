#version 450
#extension GL_EXT_fragment_shader_barycentric : require
layout(location=0) pervertexEXT in vec4 c[];
layout(location=0) out vec4 o;
void main(){ o = gl_BaryCoordEXT.x * c[0] + gl_BaryCoordNoPerspEXT.y * c[1] + c[2]; }
