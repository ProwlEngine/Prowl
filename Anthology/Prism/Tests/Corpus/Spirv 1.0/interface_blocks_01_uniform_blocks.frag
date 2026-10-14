#version 460 core

layout(std140, binding=0) uniform Camera
{
    mat4 view;
    mat4 proj;
    float near_plane;
    float far_plane;
};

layout(location=0) out vec4 FragColor;

void main()
{
    float depth_range = far_plane - near_plane;
    vec4 view_row = view[0];
    vec4 proj_row = proj[0];
    FragColor = vec4(view_row.xyz + proj_row.xyz, depth_range * 0.001);
}
