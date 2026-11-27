#version 460
layout(triangles) in;
layout(triangle_strip, max_vertices = 3) out;
in gl_PerVertex {
    vec4 gl_Position;
    float gl_PointSize;
} gl_in[];
void main() {
    for (int i = 0; i < 3; i++) {
        gl_Position = gl_in[i].gl_Position;
        EmitVertex();
    }
    EndPrimitive();
}
