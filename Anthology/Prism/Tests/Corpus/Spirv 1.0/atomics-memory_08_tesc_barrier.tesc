#version 460
layout(vertices = 3) out;
layout(std430, set = 0, binding = 0) buffer Data { float values[]; };
void main()
{
    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;
    barrier();
    if (gl_InvocationID == 0) {
        gl_TessLevelInner[0] = 1.0;
        gl_TessLevelOuter[0] = 1.0;
        gl_TessLevelOuter[1] = 1.0;
        gl_TessLevelOuter[2] = 1.0;
        values[0] = gl_in[0].gl_Position.x;
    }
}
