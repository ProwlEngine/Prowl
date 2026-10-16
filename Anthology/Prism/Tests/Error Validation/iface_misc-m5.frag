#version 450
layout(location=0) out vec4 o;
layout(binding=0) uniform sampler2D s;
void main(){ o = texture(s, gl_PointCoord) * float(gl_SampleMaskIn.length()); }
