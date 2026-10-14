#version 460

layout(binding = 0) uniform U {
    mat4 m;
    float s;
};

void main() {
    mat4 a = m * s;
    mat4 b = s * m;
    gl_Position = (a + b) * vec4(float(gl_VertexIndex), 1.0, 1.0, 1.0);
}
