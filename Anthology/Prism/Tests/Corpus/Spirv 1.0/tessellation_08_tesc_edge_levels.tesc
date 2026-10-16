#version 460

layout(vertices = 3) out;

void main() {
    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;

    gl_TessLevelInner[0] = 5.0;
    gl_TessLevelOuter[0] = 2.0;
    gl_TessLevelOuter[1] = 4.0;
    gl_TessLevelOuter[2] = 8.0;
}
