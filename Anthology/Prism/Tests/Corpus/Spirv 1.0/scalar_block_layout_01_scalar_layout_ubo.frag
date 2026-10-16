#version 460 core
#extension GL_EXT_scalar_block_layout : require

layout(scalar, set = 0, binding = 0) uniform Data
{
    vec3 positions[4];
    float scalars[4];
    bool flags[4];
};

layout(location = 0) out vec4 FragColor;

void main()
{
    vec3 accum = vec3(0.0);

    for (int i = 0; i < 4; i++)
    {
        if (flags[i])
        {
            accum += positions[i] * scalars[i];
        }
    }

    FragColor = vec4(accum, 1.0);
}
