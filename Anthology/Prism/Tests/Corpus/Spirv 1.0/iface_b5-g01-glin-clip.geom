#version 450
layout(triangles) in;
layout(triangle_strip, max_vertices=3) out;
in gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[2]; } gl_in[];
void main(){ gl_Position = vec4(gl_in[0].gl_ClipDistance[1]); EmitVertex(); }
