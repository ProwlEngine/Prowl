#version 450
layout(set=0, binding=0) uniform sampler2D s;
layout(set=0, binding=1) uniform sampler1D s1;
layout(location=0) in vec2 uv;
layout(location=0) out vec4 o;
void main() { vec2 d = dFdx(uv); float l = uv.x; o = textureGrad(s, d, d, d) + textureLod(s1, l, l) + textureGrad(s, uv, gl_PointCoord, gl_PointCoord); }
