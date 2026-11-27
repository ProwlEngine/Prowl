#version 450
#extension GL_EXT_scalar_block_layout : require
layout(scalar) uniform;
layout(binding=0) uniform U { float a[2]; float f; } u;
layout(location=0) out vec4 c;
void main(){ c = vec4(u.a[1], u.f, 0, 0); }
