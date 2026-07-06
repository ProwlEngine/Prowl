#version 450
layout(row_major) uniform;
layout(row_major) buffer;

#line 13 0
struct Material_0
{
    vec4 baseColor_0;
    vec2 tiling_0;
    int flags_0;
};


layout(binding = 1)
layout(std140) uniform block_Material_0
{
    vec4 baseColor_0;
    vec2 tiling_0;
    int flags_0;
}material_0;

#line 6
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

#line 93 1
layout(location = 0)
out vec4 entryPointParam_fragment_0;


#line 93
layout(location = 0)
in vec2 uv_0;


#line 30 0
void main()
{

#line 30
    entryPointParam_fragment_0 = material_0.baseColor_0 + vec4(uv_0 * material_0.tiling_0, camera_0.cameraPos_0.z, float(material_0.flags_0));

#line 30
    return;
}

