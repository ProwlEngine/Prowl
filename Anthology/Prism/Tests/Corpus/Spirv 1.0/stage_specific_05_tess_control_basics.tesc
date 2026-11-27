#version 460 core

// exercises gl_TessLevelOuter, gl_TessLevelInner, gl_out, gl_in

layout(vertices = 3) out;

void main()
{
    if (gl_InvocationID == 0)
    {
        gl_TessLevelOuter[0] = 4.0;
        gl_TessLevelOuter[1] = 4.0;
        gl_TessLevelOuter[2] = 4.0;
        gl_TessLevelInner[0] = 4.0;
    }

    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;
}
