#version 450
layout(row_major) uniform;
layout(row_major) buffer;

#line 14 0
layout(location = 0)
out vec4 entryPointParam_fragment_0;


#line 8
layout(location = 0)
in vec2 input_uv_0;


#line 30
void main()
{

#line 30
    entryPointParam_fragment_0 = vec4(input_uv_0, 0.0, 1.0);

#line 30
    return;
}

