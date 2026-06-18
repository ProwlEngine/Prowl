#version 450
layout(row_major) uniform;
layout(row_major) buffer;

#line 8 0
struct Globals_0
{
    mat4x4 viewProj_0;
    vec4 tint_0;
};

layout(binding = 0)
layout(std140) uniform block_Globals_0
{
    mat4x4 viewProj_0;
    vec4 tint_0;
}globals_0;

#line 16
layout(location = 0)
out vec3 entryPointParam_vertex_normal_0;


#line 16
layout(location = 1)
out vec4 entryPointParam_vertex_color_0;


#line 16
layout(location = 0)
in vec3 input_position_0;


#line 16
layout(location = 1)
in vec3 input_normal_0;


#line 16
layout(location = 2)
in vec4 input_color_0;


#line 23
struct VertexOutput_0
{
    vec4 clipPosition_0;
    vec3 normal_0;
    vec4 color_0;
};


void main()
{
    VertexOutput_0 output_0;
    output_0.clipPosition_0 = (((vec4(input_position_0, 1.0)) * (globals_0.viewProj_0)));
    output_0.normal_0 = input_normal_0;
    output_0.color_0 = input_color_0 * globals_0.tint_0;
    VertexOutput_0 _S1 = output_0;

#line 37
    gl_Position = output_0.clipPosition_0;

#line 37
    entryPointParam_vertex_normal_0 = _S1.normal_0;

#line 37
    entryPointParam_vertex_color_0 = _S1.color_0;

#line 37
    return;
}

