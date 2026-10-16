#version 450
layout(triangles) in;
layout(triangle_strip, max_vertices = 3) out;
layout(location = 0) in vec3 vNormal[];
layout(location = 0) out vec3 gNormal;
void main() {
    for (int i = 0; i < 3; ++i) {
        gNormal = vNormal[i];
        gl_Position = gl_in[i].gl_Position;
        EmitVertex();
    }
    EndPrimitive();
}
