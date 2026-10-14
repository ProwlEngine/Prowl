#version 450
layout(triangles) in;
layout(triangle_strip, max_vertices=3) out;
layout(location=0) in vec3 vIn;
layout(location=0) out vec3 vOut;
void main() { vOut = vIn; gl_Position = gl_in[0].gl_Position; EmitVertex(); }
