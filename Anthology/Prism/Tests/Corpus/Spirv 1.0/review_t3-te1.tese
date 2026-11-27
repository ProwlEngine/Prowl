#version 450
layout(triangles) in;
layout(cw) in;
layout(fractional_odd_spacing) in;
void main(){ gl_Position = vec4(gl_TessCoord, 1); }
