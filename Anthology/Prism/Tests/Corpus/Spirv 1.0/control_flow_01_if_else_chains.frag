#version 460 core

layout(location = 0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float level;
};

void main()
{
    vec4 color;

    if (level < 0.25)
    {
        color = vec4(1.0, 0.0, 0.0, 1.0);
    }
    else if (level < 0.5)
    {
        color = vec4(0.0, 1.0, 0.0, 1.0);

        // nested if inside a branch
        if (level > 0.4)
        {
            color.a = 0.5;
        }
    }
    else if (level < 0.75)
    {
        color = vec4(0.0, 0.0, 1.0, 1.0);
    }
    else
    {
        color = vec4(1.0, 1.0, 1.0, 1.0);
    }

    FragColor = color;
}
