#version 450
layout(triangles, fractional_even_spacing) in;
layout(location=0) in vec4 c[gl_MaxPatchVertices];
void main(){ gl_Position = c[0] * gl_TessCoord.x; }
