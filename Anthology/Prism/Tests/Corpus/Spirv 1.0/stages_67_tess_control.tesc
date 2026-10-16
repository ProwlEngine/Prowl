#version 450
layout(vertices = 3) out;
layout(location = 0) in vec3 vPos[];
layout(location = 0) out vec3 tcPos[];
void main() {
    if (gl_InvocationID == 0) {
        gl_TessLevelInner[0] = 4.0;
        gl_TessLevelOuter[0] = 4.0;
        gl_TessLevelOuter[1] = 4.0;
        gl_TessLevelOuter[2] = 4.0;
    }
    tcPos[gl_InvocationID] = vPos[gl_InvocationID];
    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;
}
