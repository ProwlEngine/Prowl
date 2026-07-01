#version 450
layout(row_major) uniform;
layout(row_major) buffer;

#line 7 0
struct Globals_0
{
    mat4x4 viewProj_0;
    vec4 tint_0;
};


#line 9
layout(binding = 0)
layout(std140) uniform block_Globals_0
{
    mat4x4 viewProj_0;
    vec4 tint_0;
}globals_0;

#line 26
layout(location = 0)
in vec3 position_0;


#line 26
void main()
{

#line 26
    gl_Position = (((vec4(position_0, 1.0)) * (globals_0.viewProj_0))) * globals_0.tint_0;

#line 26
    return;
}

