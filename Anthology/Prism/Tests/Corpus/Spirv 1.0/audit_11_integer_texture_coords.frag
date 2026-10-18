#version 450
layout(set=0,binding=0) uniform sampler2D s;
layout(set=0,binding=1) uniform sampler2DShadow sh;
layout(location=0) flat in ivec3 c;
layout(location=0) out vec4 o;
void main() { o = texture(s, c.xy) + textureGather(s, c.xy) + vec4(texture(sh, c)); }
