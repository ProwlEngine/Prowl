#version 450
layout(triangles_adjacency) in;
layout(triangle_strip, max_vertices=3) out;
in gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[2]; } gl_in[];
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[2]; };
layout(location=0) in VS { vec3 n; } vs[];
layout(location=0) out vec3 n;
void main(){ for (int i=0;i<3;i++){ gl_Position = gl_in[i*2].gl_Position; gl_ClipDistance[1] = gl_in[i].gl_ClipDistance[1]; n = vs[i].n; gl_ViewportIndex = 2; EmitVertex(); } EndPrimitive(); }
