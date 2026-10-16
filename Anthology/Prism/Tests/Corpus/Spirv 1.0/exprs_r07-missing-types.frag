#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
layout(binding=0) uniform texture2DMS t; void main(){ o = v; }
