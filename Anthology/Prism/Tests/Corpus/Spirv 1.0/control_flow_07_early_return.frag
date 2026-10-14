#version 460 core

layout(location = 0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float input_value;
};

float classify(float x)
{
    if (x < 0.0)
    {
        return 0.0;
    }

    if (x < 0.5)
    {
        // return nested two blocks deep
        if (x < 0.25)
        {
            return 0.25;
        }
        return 0.5;
    }

    return 1.0;
}

void main()
{
    float result = classify(input_value);
    FragColor = vec4(result, 0.0, 0.0, 1.0);
}
