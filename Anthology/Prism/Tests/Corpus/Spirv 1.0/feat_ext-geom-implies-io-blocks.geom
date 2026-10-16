#version 310 es
#extension GL_EXT_geometry_shader : require
layout(points) in;
layout(points, max_vertices=1) out;
layout(location=0) in VIn { vec4 c; } vin[];
layout(location=0) out VOut { vec4 c; } vout;
void main(){ vout.c = vin[0].c; gl_Position = gl_in[0].gl_Position; EmitVertex(); }
