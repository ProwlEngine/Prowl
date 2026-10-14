#version 450
layout(triangles) in;
layout(triangle_strip, max_vertices=3) out;
layout(location=0) in vec3 vIn[];
layout(location=0) out vec3 vOut;
void main(){ for(int i=0;i<3;i++){ vOut=vIn[i]; gl_Position=gl_in[i].gl_Position; EmitVertex(); } EndPrimitive(); }
