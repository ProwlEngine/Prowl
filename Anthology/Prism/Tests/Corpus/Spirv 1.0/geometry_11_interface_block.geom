#version 460

layout(triangles) in;
layout(triangle_strip, max_vertices = 3) out;

layout(location = 0) in Block {
    vec3 c;
} vin[];

layout(location = 0) out Block {
    vec3 c;
} vout;

void main() {
    for (int i = 0; i < 3; ++i) {
        gl_Position = gl_in[i].gl_Position;
        vout.c = vin[i].c;
        EmitVertex();
    }
    EndPrimitive();
}
