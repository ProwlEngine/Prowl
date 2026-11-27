#version 460

layout(vertices = 3) out;

layout(location = 0) out vec3 tcShared[];

void main() {
    tcShared[gl_InvocationID] = gl_in[gl_InvocationID].gl_Position.xyz;

    barrier();

    int next = (gl_InvocationID + 1) % 3;
    gl_out[gl_InvocationID].gl_Position = vec4(tcShared[next], 1.0);

    gl_TessLevelInner[0] = 1.0;
    gl_TessLevelOuter[0] = 1.0;
    gl_TessLevelOuter[1] = 1.0;
    gl_TessLevelOuter[2] = 1.0;
}
