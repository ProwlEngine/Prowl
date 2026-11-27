#version 450
#extension GL_EXT_fragment_shader_barycentric : require
layout(location=0) pervertexEXT in vec2 uv[3];
layout(location=3) flat in int id;
layout(location=0) out vec4 o;
void main(){ vec3 b = gl_BaryCoordNoPerspEXT; o = vec4(uv[0] * b.x + uv[1] * b.y + uv[2] * b.z, gl_BaryCoordEXT.z, float(id)); }
