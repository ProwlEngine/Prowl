#version 450
#extension GL_EXT_scalar_block_layout : require
layout(scalar) uniform;
layout(binding=0) uniform U { float a; vec3 b; } u;
layout(location=0) out vec4 o;
void main(){ o = vec4(u.b, u.a); }
