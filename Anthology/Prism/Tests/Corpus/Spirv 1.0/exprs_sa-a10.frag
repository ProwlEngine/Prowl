#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
layout(binding=0) uniform sampler2D s[]; void main(){ o = texture(s[2], v.xy); }
