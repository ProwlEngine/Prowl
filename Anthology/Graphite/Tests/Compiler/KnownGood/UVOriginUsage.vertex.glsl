#version 450
layout(row_major) uniform;
layout(row_major) buffer;

#line 8 0
layout(location = 0)
out vec2 entryPointParam_vertex_uv_0;


#line 8
layout(location = 0)
in vec3 input_position_0;


#line 8
layout(location = 1)
in vec2 input_uv_0;




struct VertexOutput_0
{
    vec4 clipPosition_0;
    vec2 uv_0;
};


void main()
{
    VertexOutput_0 output_0;
    output_0.clipPosition_0 = vec4(input_position_0, 1.0);
    output_0.uv_0 = vec2(input_uv_0.x, 1.0 - input_uv_0.y);
    VertexOutput_0 _S1 = output_0;

#line 26
    gl_Position = output_0.clipPosition_0;

#line 26
    entryPointParam_vertex_uv_0 = _S1.uv_0;

#line 26
    return;
}

