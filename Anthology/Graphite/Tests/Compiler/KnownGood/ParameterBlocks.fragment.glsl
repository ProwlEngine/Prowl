#version 450
layout(row_major) uniform;
layout(row_major) buffer;

#line 10 0
layout(binding = 1)
uniform texture2D albedo_0;


#line 11
layout(binding = 2)
uniform sampler samp_0;


#line 13
struct PerObject_0
{
    vec4 color_0;
    vec2 uvOffset_0;
};


#line 18
layout(binding = 0, set = 1)
layout(std140) uniform block_PerObject_0
{
    vec4 color_0;
    vec2 uvOffset_0;
}perObject_0;

#line 18
layout(binding = 1, set = 1)
uniform texture2D perObject_detail_0;


#line 18
layout(binding = 2, set = 1)
uniform sampler perObject_detailSamp_0;


#line 18
layout(binding = 0, set = 2)
uniform texture2D onlyTex_tex_0;


#line 18
layout(binding = 1, set = 2)
uniform sampler onlyTex_s_0;


#line 1084 1
layout(location = 0)
out vec4 entryPointParam_fragment_0;


#line 1084
layout(location = 0)
in vec2 uv_0;


#line 30 0
void main()
{

#line 30
    entryPointParam_fragment_0 = (texture(sampler2D(albedo_0,samp_0), (uv_0))) * perObject_0.color_0 + (texture(sampler2D(perObject_detail_0,perObject_detailSamp_0), (uv_0 + perObject_0.uvOffset_0))) + (texture(sampler2D(onlyTex_tex_0,onlyTex_s_0), (uv_0)));

#line 30
    return;
}

