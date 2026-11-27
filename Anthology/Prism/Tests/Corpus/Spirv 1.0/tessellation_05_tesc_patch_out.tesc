#version 460

layout(vertices = 3) out;

layout(location = 0) patch out vec3 patchData;

void main() {
    if (gl_InvocationID == 0) {
        patchData = vec3(1.0, 2.0, 3.0);
    }

    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;

    gl_TessLevelInner[0] = 4.0;
    gl_TessLevelOuter[0] = 4.0;
    gl_TessLevelOuter[1] = 4.0;
    gl_TessLevelOuter[2] = 4.0;
}
