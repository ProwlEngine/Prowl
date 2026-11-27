#version 460

layout(binding = 0) uniform U {
    vec3 v;
    float s;
};

void main() {
    vec3 a = v * s;
    vec3 b = s * v;
    vec3 c = (v * s) * s;
    vec3 sum = a + b + c;
    gl_Position = vec4(sum * float(gl_VertexIndex), 1.0);
}
