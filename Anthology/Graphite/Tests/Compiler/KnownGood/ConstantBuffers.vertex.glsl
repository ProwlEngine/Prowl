#version 450
layout(row_major) uniform;
layout(row_major) buffer;

#line 6 0
struct Camera_0
{
    mat4x4 viewProj_0;
    vec3 cameraPos_0;
    float time_0;
};


#line 20
layout(binding = 0)
layout(std140) uniform block_Camera_0
{
    mat4x4 viewProj_0;
    vec3 cameraPos_0;
    float time_0;
}camera_0;

#line 13369 1
layout(location = 0)
in vec3 position_0;


#line 24 0
void main()
{

#line 24
    gl_Position = (((vec4(position_0, 1.0)) * (camera_0.viewProj_0))) + camera_0.time_0;

#line 24
    return;
}

