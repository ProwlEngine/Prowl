#version 460

layout(vertices = 3) out;

layout(location = 0) out vec3 tcColor[];
layout(location = 1) patch out vec3 patchCenter;

void main() {
    tcColor[gl_InvocationID] = gl_in[gl_InvocationID].gl_Position.xyz;

    barrier();

    if (gl_InvocationID == 0) {
        patchCenter = (tcColor[0] + tcColor[1] + tcColor[2]) / 3.0;
    }

    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;

    gl_TessLevelInner[0] = 3.0;
    gl_TessLevelOuter[0] = 3.0;
    gl_TessLevelOuter[1] = 3.0;
    gl_TessLevelOuter[2] = 3.0;
}
