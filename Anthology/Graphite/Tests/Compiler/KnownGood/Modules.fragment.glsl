#version 450
layout(row_major) uniform;
layout(row_major) buffer;

#line 6 0
struct Lighting_0
{
    vec4 sunDirection_0;
    vec4 sunColor_0;
    float ambientIntensity_0;
};

layout(binding = 1)
layout(std140) uniform block_Lighting_0
{
    vec4 sunDirection_0;
    vec4 sunColor_0;
    float ambientIntensity_0;
}lighting_0;

#line 15
vec3 shade_0(vec3 albedo_0, vec3 normal_0)
{

    return albedo_0 * (lighting_0.ambientIntensity_0 + max(0.0, dot(normalize(normal_0), normalize(lighting_0.sunDirection_0.xyz)))) * lighting_0.sunColor_0.xyz;
}


#line 18
layout(location = 0)
out vec4 entryPointParam_fragment_0;


#line 18
layout(location = 0)
in vec3 input_normal_0;


#line 18
layout(location = 1)
in vec4 input_color_0;


#line 41 1
void main()
{

#line 41
    entryPointParam_fragment_0 = vec4(shade_0(input_color_0.xyz, input_normal_0), input_color_0.w);

#line 41
    return;
}

