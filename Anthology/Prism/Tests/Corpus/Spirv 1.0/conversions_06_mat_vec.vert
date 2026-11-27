#version 460

layout(binding = 0) uniform U {
    mat4 m;
    vec4 v;
};

void main() {
    vec4 col = m * v;
    vec4 row = v * m;
    gl_Position = col + row * float(gl_VertexIndex);
}
