#version 460

layout(triangles, invocations = 4) in;
layout(triangle_strip, max_vertices = 3) out;

void main() {
    float offset = float(gl_InvocationID) * 0.1;
    for (int i = 0; i < 3; ++i) {
        gl_Position = gl_in[i].gl_Position + vec4(offset, 0.0, 0.0, 0.0);
        EmitVertex();
    }
    EndPrimitive();
}
