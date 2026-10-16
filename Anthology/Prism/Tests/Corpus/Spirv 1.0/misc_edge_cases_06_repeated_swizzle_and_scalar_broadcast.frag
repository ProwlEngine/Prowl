#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float scalar_input;
};

void main()
{
    vec4 broadcast = vec4(scalar_input);

    vec4 v = vec4(1.0, 2.0, 3.0, 4.0);
    float single_swizzle = v.x + 1.0;

    vec3 swizzle_of_swizzle = v.xyz.zyx;

    FragColor = broadcast * 0.1 + vec4(swizzle_of_swizzle, single_swizzle) * 0.1;
}
