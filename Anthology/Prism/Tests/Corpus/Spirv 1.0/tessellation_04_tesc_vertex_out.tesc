#version 460

layout(vertices = 3) out;

layout(location = 0) out vec3 tcColor[];

void main() {
    tcColor[gl_InvocationID] = vec3(float(gl_InvocationID), 0.5, 1.0);

    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;

    gl_TessLevelInner[0] = 2.0;
    gl_TessLevelOuter[0] = 2.0;
    gl_TessLevelOuter[1] = 2.0;
    gl_TessLevelOuter[2] = 2.0;
}
