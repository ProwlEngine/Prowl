#version 460 core

// exercises gl_TessCoord barycentric interpolation of gl_in positions

layout(triangles, equal_spacing, cw) in;

void main()
{
    vec4 pos0 = gl_in[0].gl_Position;
    vec4 pos1 = gl_in[1].gl_Position;
    vec4 pos2 = gl_in[2].gl_Position;

    gl_Position = gl_TessCoord.x * pos0 + gl_TessCoord.y * pos1 + gl_TessCoord.z * pos2;
}
