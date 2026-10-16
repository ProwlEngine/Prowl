#version 460 core

// exercises gl_FragCoord, gl_FrontFacing, gl_FragDepth, gl_PointCoord

layout(location = 0) out vec4 FragColor;

void main()
{
    float depth_bias = gl_FrontFacing ? 0.0 : 0.001;
    vec2 coord_offset = gl_PointCoord - vec2(0.5);
    float coord_len = length(coord_offset);

    gl_FragDepth = clamp(gl_FragCoord.z + depth_bias, 0.0, 1.0);

    FragColor = vec4(gl_FragCoord.xy * 0.001, coord_len, 1.0);
}
