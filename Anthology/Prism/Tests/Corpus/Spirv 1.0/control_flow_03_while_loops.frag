#version 460 core

layout(location = 0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float limit;
};

void main()
{
    float counter = 0.0;

    while (true)
    {
        if (counter >= limit)
        {
            break;
        }
        counter += 1.0;
    }

    FragColor = vec4(counter * 0.01, 0.0, 0.0, 1.0);
}
