#version 460 core

layout(location = 0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float threshold;
};

void main()
{
    vec4 color;

    // genuine bool from a relational expression
    if (threshold > 0.5)
    {
        color = vec4(1.0, 0.0, 0.0, 1.0);
    }
    else
    {
        color = vec4(0.0, 1.0, 0.0, 1.0);
    }

    float counter = 5.0;

    // bool() constructor from a nonzero float used as loop condition
    while (bool(counter))
    {
        counter -= 1.0;
    }

    color.b = counter;
    FragColor = color;
}
