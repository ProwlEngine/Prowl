#version 450
layout(points) in;
layout(points, max_vertices=2) out;
layout(stream=1) out;
layout(location=0) out vec4 a;
layout(location=1, stream=0) out vec4 b;
void main(){ a = vec4(1); EmitStreamVertex(1); b = vec4(0); gl_Position = vec4(0); EmitStreamVertex(0); }
